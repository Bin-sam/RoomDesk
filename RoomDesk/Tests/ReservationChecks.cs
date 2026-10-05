using System.Text;
using Microsoft.Data.Sqlite;
using RoomDesk.Core;

static class ReservationChecks
{
    public static async Task Run(string root, Action<bool,string> check, Func<Func<Task>,string,Task> rejected)
    {
        var store=new BoardStore(Path.Combine(root,"reservations.db"));await store.InitializeAsync();
        async Task<RoomCard> Room()=> (await store.ReadAsync()).Rooms.Single(r=>r.Number==101);
        var room=await Room();
        foreach(var input in new ReservationInput?[]{null,new(" "),new("测试",new string('a',41)),new(new string('a',81)),new("测试","携程",new string('0',41))})
            await rejected(()=>store.ChangeAsync(room.Id,room.Version,"Reserve",reservation:input),"invalid reservation rejected without saving");
        foreach(var platform in BoardStore.DefaultPlatforms)
        {
            room=await Room();await store.ChangeAsync(room.Id,room.Version,"Reserve",reservation:new("  预订测试  ",platform," TEST-ONLY "));
            var reserved=await Room();var input=await store.GetReservationAsync(room.Id);
            check(reserved.ReservationName=="预订测试"&&reserved.BoardCaption=="预订测试"&&reserved.GuestLabel=="预订人：预订测试","reserved card displays reservation name");
            check(input?.Platform==platform&&input.Phone=="TEST-ONLY","reservation platform and phone persist");
            await rejected(()=>store.ChangeAsync(room.Id,room.Version,"Reserve",reservation:new("重复预订")),"duplicate stale reservation rejected");
            var reopened=new BoardStore(store.DatabasePath);await reopened.InitializeAsync();
            check((await reopened.GetReservationAsync(room.Id))==input,"reservation survives reopening and idempotent migration");
            await store.ChangeAsync(room.Id,reserved.Version,"CancelReservation");
            check((await Room()).ReservationName==null&&await store.GetReservationAsync(room.Id)==null,"cancel clears reservation details");
        }
        room=await Room();await store.ChangeAsync(room.Id,room.Version,"Reserve",reservation:new("预订测试","携程","TEST-ONLY"));
        room=await Room();await rejected(()=>store.ChangeAsync(room.Id,room.Version,"CheckIn",new("入住测试",SalePrice:168,Platform:"\n")),"blank check-in platform rejected");
        check((await Room()).ReservationName=="预订测试","failed check-in preserves reservation");
        await store.ChangeAsync(room.Id,room.Version,"CheckIn",new("入住测试", "TEST-ONLY", "身份证", SalePrice:168,Platform:"美团"));
        var guest=await store.GetCurrentGuestAsync(room.Id);
        check(guest?.Platform=="美团"&&guest.DocumentType=="身份证"&&guest.SalePrice==168,"check-in saves selected platform, document type and price");
        check((await Room()).ReservationName==null&&await store.GetReservationAsync(room.Id)==null,"check-in consumes reservation details");
        room=await Room();await store.ChangeAsync(room.Id,room.Version,"CheckOut");
        check((await store.SearchStaysAsync("美团")).Total==1,"platform remains searchable after checkout");
        await store.SetPasswordAsync("Reservation-test-only-2026");
        check(Encoding.UTF8.GetString(await store.ExportStaysCsvAsync(password:"Reservation-test-only-2026")).Contains("\"美团\""),"history CSV contains platform");
        check(Encoding.UTF8.GetString(await store.ExportBillAsync("2000-01-01T00:00","2099-12-31T23:59","Reservation-test-only-2026")).Contains("\"美团\""),"bill CSV contains platform");
        // Simulate last release's schema; names/prices and board versions must survive migration.
        using(var db=new SqliteConnection($"Data Source={store.DatabasePath}"))
        {
            db.Open();using var cmd=db.CreateCommand();
            cmd.CommandText="ALTER TABLE BoardStates DROP COLUMN ReservationName; ALTER TABLE BoardStates DROP COLUMN ReservationPhone; ALTER TABLE BoardStates DROP COLUMN ReservationPlatform; DROP INDEX IF EXISTS IX_Stays_TextScan; DROP TRIGGER StayText_insert; DROP TRIGGER StayText_update; DROP INDEX IX_Stays_ShortText; ALTER TABLE GuestRegistrations DROP COLUMN SearchText; DROP TRIGGER StaySearch_insert; DROP TRIGGER StaySearch_delete; DROP TRIGGER StaySearch_update; DROP TABLE StaySearch; ALTER TABLE GuestRegistrations DROP COLUMN Platform;";
            cmd.ExecuteNonQuery();
        }
        var version=(await RoomVersion(store.DatabasePath,room.Id));
        await store.InitializeAsync();await store.InitializeAsync();
        var history=await store.SearchStaysAsync();
        check(history.Total==1&&history.Records[0].Platform==""&&history.Records[0].SalePrice==168,"old stays keep price and unknown platform after migration");
        check((await Room()).Version==version,"migration does not rewrite room version");
    }
    static async Task<long> RoomVersion(string path,int id)
    {
        await using var db=new SqliteConnection($"Data Source={path}");await db.OpenAsync();await using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT Version FROM BoardStates WHERE RoomId=$id";cmd.Parameters.AddWithValue("$id",id);return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
