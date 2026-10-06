using System.Diagnostics;
using Microsoft.Data.Sqlite;
using RoomDesk.Core;

var root = Path.Combine(Path.GetTempPath(), "RoomDeskTests-" + Guid.NewGuid());
Directory.CreateDirectory(root);
var store = new BoardStore(Path.Combine(root, "test.db"));
var passed = 0;
void Assert(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
async Task Rejected(Func<Task> action, string name) { try { await action(); } catch (BoardException) { Assert(true, name); return; } throw new Exception("FAIL expected rejection: " + name); }
async Task<RoomCard> Room(int number) => (await store.ReadAsync()).Rooms.Single(r => r.Number == number);
async Task Change(int number, string action) { var r = await Room(number); await store.ChangeAsync(r.Id, r.Version, action, action == "CheckIn" ? new GuestInput("测试住客", SalePrice: 199m) : null, action == "Reserve" ? new ReservationInput("预订测试", "线下") : null); }
try
{
    await store.InitializeAsync(24);
    await store.SetPasswordAsync("Test-only-pass-2026");
    var initial = await store.ReadAsync();
    Assert(initial.Rooms.Count == 24 && initial.Rooms.Select(r => r.Floor).Distinct().Count() == 3, "24 sample rooms / 3 floors");
    Assert(initial.Ready == 13 && initial.Occupied == 3 && initial.Reserved == 3 && initial.Dirty == 4 && initial.Unavailable == 2, "initial counters, including dirty maintenance room");
    await store.InitializeAsync(24); Assert((await store.ReadAsync()).Rooms.Count == 24, "initialization is idempotent");
    var stale = await Room(101);
    await Rejected(() => store.ChangeAsync(stale.Id, stale.Version, "CheckIn"), "check-in requires guest information");
    await Rejected(() => store.ChangeAsync(stale.Id, stale.Version, "CheckIn", new GuestInput("  ", SalePrice: 199m)), "whitespace-only guest rejected");
    await Rejected(() => store.ChangeAsync(stale.Id, stale.Version, "CheckIn", new GuestInput(new string('a', 81), SalePrice: 199m)), "oversized guest field rejected");
    await Rejected(() => store.ChangeAsync(stale.Id, stale.Version, "CheckIn", new GuestInput("测试", DocumentNumber: "TEST-001", SalePrice: 199m)), "document number requires type");
    Assert((await Room(101)).Version == stale.Version && await store.GetCurrentGuestAsync(stale.Id) == null, "invalid registration leaves room and guest data unchanged");
    await Change(101, "CheckIn"); Assert((await Room(101)).Occupancy == "Occupied", "check-in persists");
    Assert((await store.GetCurrentGuestAsync(stale.Id))?.Name == "测试住客", "guest and occupancy saved together");
    Assert((await new BoardStore(store.DatabasePath).GetCurrentGuestAsync(stale.Id))?.Name == "测试住客", "guest persists on reopen");
    await Rejected(() => store.ChangeAsync(stale.Id, stale.Version, "CheckIn", new GuestInput("不能覆盖", SalePrice: 199m)), "duplicate registration rejected");
    Assert((await store.GetCurrentGuestAsync(stale.Id))?.Name == "测试住客", "stale request cannot overwrite guest");
    await Rejected(() => store.ChangeAsync(stale.Id, stale.Version, "Reserve", reservation: new("预订测试")), "stale update rejected");
    Assert((await Room(101)).GuestName == "测试住客" && (await Room(101)).GuestLabel == "入住人：测试住客", "board includes current guest name");
    Assert((await Room(102)).GuestLabel == "入住人：未登记", "legacy occupied room indicates missing registration");
    await Change(101, "MarkDirty"); var occupiedDirty = await Room(101);
    Assert(!occupiedDirty.IsClean && occupiedDirty.Occupancy == "Occupied" && occupiedDirty.StatusKey == "occupied", "occupancy and cleanliness remain independent");
    await Rejected(() => store.ChangeAsync(occupiedDirty.Id, occupiedDirty.Version, "StartMaintenance"), "occupied room cannot enter maintenance");
    await Change(101, "CheckOut"); var dirty = await Room(101);
    Assert(await store.GetCurrentGuestAsync(stale.Id) == null, "checkout archives guest and clears current registration");
    Assert(dirty.GuestName == null && dirty.GuestLabel == "", "checkout clears guest from board while keeping history");
    Assert(dirty.StatusKey == "dirty" && dirty.Occupancy == "Vacant", "checkout produces dirty vacant room");
    await Rejected(() => store.ChangeAsync(dirty.Id, dirty.Version, "CheckIn", new GuestInput("测试", SalePrice: 199m)), "dirty room cannot check in");
    await Rejected(() => store.ChangeAsync(dirty.Id, dirty.Version, "Reserve", reservation: new("预订测试")), "dirty room cannot reserve");
    await Change(101, "Clean"); await Change(101, "Reserve");
    var reserved = await Room(101);
    await Rejected(() => store.ChangeAsync(reserved.Id, reserved.Version, "Disable"), "reserved room cannot be disabled");
    await Change(101, "CancelReservation"); await Change(101, "Reserve"); await Change(101, "CheckIn");
    Assert((await Room(101)).StatusKey == "occupied", "reservation cancellation and arrival transitions");
    await Change(101, "CheckOut"); await Change(101, "StartMaintenance");
    var maintenance = await Room(101);
    await Rejected(() => store.ChangeAsync(maintenance.Id, maintenance.Version, "CheckIn", new GuestInput("测试", SalePrice: 199m)), "maintenance blocks check-in");
    await Change(101, "Restore"); Assert((await Room(101)).StatusKey == "dirty", "restored maintenance room needs cleaning");
    await Change(101, "Clean"); await Change(101, "Disable"); await Change(101, "Restore");
    Assert((await Room(101)).StatusKey == "dirty", "restored disabled room needs cleaning");
    await Rejected(() => store.ChangeAsync(99999, 0, "Clean"), "missing room rejected");
    await Rejected(() => store.ChangeAsync(1, 0, "999"), "undefined action rejected");
    var beforeRejected = (await store.ReadAsync()).Activities.Count;
    await Rejected(() => store.AddRoomAsync(101, 1, "Standard"), "duplicate room rejected");
    await Rejected(() => store.AddRoomAsync(999, -1, "Standard"), "invalid floor rejected");
    await Rejected(() => store.AddRoomAsync(999, 9, " "), "invalid type rejected");
    Assert((await store.ReadAsync()).Activities.Count == beforeRejected, "rejected actions do not write history");
    await store.AddRoomAsync(401, 4, "Deluxe"); Assert((await Room(401)).StatusKey == "dirty", "new room starts dirty");
    var reopened = new BoardStore(store.DatabasePath);
    Assert((await reopened.ReadAsync()).Rooms.Single(r => r.Number == 101).StatusKey == "dirty", "new service instance reads saved state");
    var fresh = await Room(105);
    async Task<bool> Race(string action) { try { await store.ChangeAsync(fresh.Id, fresh.Version, action, action == "CheckIn" ? new GuestInput("并发测试住客", SalePrice: 199m) : null, action == "Reserve" ? new ReservationInput("并发预订") : null); return true; } catch (BoardException) { return false; } }
    var race = await Task.WhenAll(Task.Run(() => Race("CheckIn")), Task.Run(() => Race("Reserve")));
    Assert(race.Count(v => v) == 1, "simultaneous stale writes have exactly one winner");
    var backup = await store.BackupAsync("Test-only-pass-2026");
    var copied = await new BoardStore(backup).ReadAsync();
    Assert(copied.Rooms.Count == 25 && copied.Activities.Count == (await store.ReadAsync()).Activities.Count, "online backup includes rooms and committed history");
    using (var db = new SqliteConnection($"Data Source={backup}")) { db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "PRAGMA integrity_check"; Assert((string?)cmd.ExecuteScalar() == "ok", "backup SQLite integrity_check"); }
    // Emulate a v0.1 database by removing only the new table from this disposable test DB.
    using (var old = new SqliteConnection($"Data Source={store.DatabasePath}")) { old.Open(); using var cmd = old.CreateCommand(); cmd.CommandText = "DROP TABLE GuestRegistrations; DROP TABLE StaySearch"; cmd.ExecuteNonQuery(); }
    await store.InitializeAsync(24); await store.InitializeAsync(24);
    Assert((await store.ReadAsync()).Rooms.Count == 25 && (await Room(101)).StatusKey == "dirty", "v0.1 additive migration preserves existing room data and is repeatable");
    await Change(401, "Clean"); var newRoom = await Room(401);
    await store.ChangeAsync(newRoom.Id, newRoom.Version, "CheckIn", new GuestInput(" 测试住客二 ", "TEST-PHONE", "其他", "TEST-ONLY-001", "测试备注", SalePrice: 199m));
    var registered = await store.GetCurrentGuestAsync(newRoom.Id);
    Assert(registered?.Name == "测试住客二" && registered.DocumentNumber == "TEST-ONLY-001" && registered.Notes == "测试备注", "all registration fields persist after schema upgrade");
    var guestBackup = new BoardStore(await store.BackupAsync("Test-only-pass-2026"));
    Assert((await guestBackup.GetCurrentGuestAsync(newRoom.Id))?.Name == "测试住客二", "online backup includes guest registrations");
    var allStays = await store.SearchStaysAsync();
    Assert(allStays.Total == 1 && allStays.Records[0].Name == "测试住客二", "saved registration appears in history");
    Assert((await store.SearchStaysAsync("测试住客")).Total == 1 && (await store.SearchStaysAsync("401")).Total == 1, "search by name and room");
    Assert((await store.SearchStaysAsync("test-phone")).Total == 1 && (await store.SearchStaysAsync("TEST-ONLY-001")).Total == 1, "case-insensitive phone and document search");
    Assert((await store.SearchStaysAsync("不存在")).Total == 0, "no-result search");
    var emptyPage=await store.SearchStaysAsync("无",page:100,pageSize:10);
    Assert(emptyPage.Total==0 && emptyPage.Records.Count==0 && emptyPage.Page==1 && emptyPage.PageSize==10,"empty short search returns a normalized empty page");
    await Change(401, "CheckOut");
    Assert((await store.SearchStaysAsync(status:"checkedout")).Total == 1 && (await store.SearchStaysAsync(status:"current")).Total == 0, "checkout preserves searchable historical record");
    await Change(401, "Clean");var again=await Room(401);
    await store.ChangeAsync(again.Id,again.Version,"CheckIn",new GuestInput("CSV,测试\"姓名",Notes:"=SUM(1,2)\n第二行", SalePrice: 199m));
    var history=await store.SearchStaysAsync(pageSize:1);
    Assert(history.Total==2 && history.Records.Count==1 && history.Records[0].Name.StartsWith("CSV"), "history pagination and newest-first order");
    Assert((await store.SearchStaysAsync(page:2,pageSize:1)).Records[0].Name=="测试住客二", "second page preserves previous stay");
    var csvBytes=await store.ExportStaysCsvAsync(password:"Test-only-pass-2026");var csvText=System.Text.Encoding.UTF8.GetString(csvBytes);
    Assert(csvBytes.Take(3).SequenceEqual(new byte[]{239,187,191}) && csvText.Contains("入住人姓名"), "CSV UTF-8 BOM and Chinese headers");
    Assert(csvText.Contains("CSV,测试\"\"姓名") && csvText.Contains("'=SUM(1,2)\n第二行"), "CSV quotes multiline fields and neutralizes formula input");
    var filteredCsv=System.Text.Encoding.UTF8.GetString(await store.ExportStaysCsvAsync(status:"checkedout",password:"Test-only-pass-2026"));
    Assert(filteredCsv.Contains("测试住客二") && !filteredCsv.Contains("CSV,测试"), "CSV follows applied search/status filters");
    Assert((await new BoardStore(store.DatabasePath).SearchStaysAsync()).Total==2, "historical records persist on reopen");
    Assert((await store.ExportStaysCsvAsync("NO-MATCH",password:"Test-only-pass-2026")).Length>3, "empty CSV still contains header");
    await Rejected(()=>store.SearchStaysAsync(status:"bad"), "invalid history filter rejected");
    await Rejected(()=>store.SearchStaysAsync(page:0), "invalid history page rejected");
    for(int i=0;i<31;i++){await Change(401,"CheckOut");await Change(401,"Clean");var room=await Room(401);await store.ChangeAsync(room.Id,room.Version,"CheckIn",new GuestInput($"分页住客{i:000}", SalePrice: 199m));}
    var paged=await store.SearchStaysAsync();
    var allCsv=System.Text.Encoding.UTF8.GetString(await store.ExportStaysCsvAsync(password:"Test-only-pass-2026"));
    Assert(paged.Total==33 && paged.Records.Count==30 && allCsv.Split("分页住客").Length==32, "CSV exports all matching rows beyond first page");
    Assert((await store.SuggestGuestsAsync("测试住客二")).Single().DocumentNumber == "TEST-ONLY-001", "history name lookup includes checked-out guests");
    Assert((await store.SuggestGuestsAsync("only-001", "document")).Single().Name == "测试住客二", "partial case-insensitive document lookup");
    Assert((await store.SuggestGuestsAsync("分页住客")).Count == 8, "history suggestions capped at eight");
    Assert((await store.SuggestGuestsAsync(" ")).Count == 0 && (await store.SuggestGuestsAsync("没有这个人")).Count == 0, "blank and no-match suggestions empty");
    await Rejected(() => store.SuggestGuestsAsync("姓名", "bad"), "unknown lookup field rejected");
    async Task Register(GuestInput guestInput) { await Change(401,"CheckOut"); await Change(401,"Clean"); var room = await Room(401); await store.ChangeAsync(room.Id,room.Version,"CheckIn",guestInput); }
    await Register(new GuestInput("匹配同名", "OLD-PHONE", "其他", "HISTORY-A", SalePrice: 199m));
    await Register(new GuestInput("匹配同名", "SECOND-PHONE", "其他", "HISTORY-B", SalePrice: 199m));
    await Register(new GuestInput("匹配同名", "NEW-PHONE", "其他", "history-a", "不会复制到新入住的备注", SalePrice: 199m));
    var suggested = await store.SuggestGuestsAsync("匹配同名");
    Assert(suggested.Count == 2 && suggested[0].Phone == "NEW-PHONE" && suggested[1].DocumentNumber == "HISTORY-B", "same-name guests stay distinct; repeated document uses latest contact details");
    Assert(suggested[0].MaskedDocument == "his••••ry-a", "suggestion display masks middle of document");
    await Register(new GuestInput("无证件同名", "PHONE-A", SalePrice: 199m)); await Register(new GuestInput("无证件同名", "PHONE-B", SalePrice: 199m));
    Assert((await store.SuggestGuestsAsync("无证件同名")).Count == 2, "undocumented same-name guests with different phones stay distinct");
    await SecurityBillingChecks.Run(root,Assert,Rejected);
    await ReservationChecks.Run(root,Assert,Rejected);
    await PlatformPresetChecks.Run(root,Assert,Rejected);
    await RoomManagementChecks.Run(root,Assert,Rejected);
    await EditingUpdateChecks.Run(root,Assert,Rejected);
    await LongevityChecks.Run(root,Assert,Rejected);
    await DefaultRoomChecks.Run(root,Assert);
    var large = new BoardStore(Path.Combine(root, "large.db")); await large.InitializeAsync(300); await large.ReadAsync();
    var timings = new List<double>();
    for (int i = 0; i < 20; i++) { var sw = Stopwatch.StartNew(); var snapshot = await large.ReadAsync(); sw.Stop(); if(snapshot.Rooms.Count != 300)throw new Exception("incomplete snapshot"); timings.Add(sw.Elapsed.TotalMilliseconds); }
    timings.Sort(); Console.WriteLine($"BENCHMARK shared core only: 300-room snapshot, warmed, 20 runs, median={timings[10]:F2}ms p95={timings[18]:F2}ms. NOT a Windows UI benchmark.");
    Console.WriteLine($"RESULT: {passed} integration checks passed. OS={System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
}
finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
