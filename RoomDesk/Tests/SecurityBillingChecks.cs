using Microsoft.Data.Sqlite;
using RoomDesk.Core;
using System.Text;
public static class SecurityBillingChecks
{
    public static async Task Run(string root,Action<bool,string> assert,Func<Func<Task>,string,Task> reject)
    {
        var store=new BoardStore(Path.Combine(root,"billing.db"));await store.InitializeAsync();
        const string pass="Only-test-secret-123";
        assert(!await store.HasPasswordAsync(),"fresh database has no default password");
        await reject(()=>store.ExportStaysCsvAsync(),"unset password blocks record export");await reject(()=>store.BackupAsync(),"unset password blocks backup");
        await reject(()=>store.SetPasswordAsync("short"),"short password rejected");await store.SetPasswordAsync(pass);
        assert(await new BoardStore(store.DatabasePath).HasPasswordAsync(),"password configuration survives reopening");
        await reject(()=>store.ExportStaysCsvAsync(password:"wrong"),"wrong password blocks export");await reject(()=>store.BackupAsync("wrong"),"wrong password blocks backup");
        await reject(()=>store.SetPasswordAsync("replacement-123"),"password change requires current password");
        await store.SetPasswordAsync("replacement-123",pass);await reject(()=>store.BackupAsync(pass),"old password invalid after change");await store.SetPasswordAsync(pass,"replacement-123");
        using(var db=new SqliteConnection($"Data Source={store.DatabasePath}")){db.Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT Salt,Hash FROM SecuritySettings";using var reader=cmd.ExecuteReader();reader.Read();assert(reader.GetString(0)!=pass&&reader.GetString(1)!=pass&&reader.GetString(1).Length>30,"password stored as salted hash, not plaintext");}
        for(int i=0;i<5;i++)await reject(()=>store.ExportStaysCsvAsync(password:"bad"),"wrong password counted");
        await reject(()=>new BoardStore(store.DatabasePath).BackupAsync(pass),"lockout persists across service instances");
        using(var db=new SqliteConnection($"Data Source={store.DatabasePath}")){db.Open();using var cmd=db.CreateCommand();cmd.CommandText="UPDATE SecuritySettings SET LockedUntilUtc = '2000-01-01 00:00:00'";cmd.ExecuteNonQuery();}
        await store.ExportStaysCsvAsync(password:pass);
        async Task<RoomCard> Room(int n)=>(await store.ReadAsync()).Rooms.Single(r=>r.Number==n);
        var r=await Room(101);await store.SetRoomPriceAsync(r.Id,r.Version,188.88m);assert((await Room(101)).DefaultPrice==188.88m,"room default price persists exactly");
        await reject(()=>store.SetRoomPriceAsync(r.Id,r.Version,200m),"stale default price change rejected");
        await reject(async()=>await store.SetRoomPriceAsync(r.Id,(await Room(101)).Version,0.001m),"fractional cent rejected");
        await reject(async()=>await store.SetRoomPriceAsync(r.Id,(await Room(101)).Version,-1m),"negative price rejected");
        r=await Room(101);await reject(()=>store.ChangeAsync(r.Id,r.Version,"CheckIn",new GuestInput("缺少价格")),"new check-in requires sale price");
        foreach(var entry in new[]{(101,100.10m),(105,50.25m),(106,0m),(201,77m)}){var room=await Room(entry.Item1);await store.ChangeAsync(room.Id,room.Version,"CheckIn",new GuestInput("账单测试",SalePrice:entry.Item2));}
        r=await Room(101);await store.SetRoomPriceAsync(r.Id,r.Version,999m);assert((await store.GetCurrentGuestAsync(r.Id))?.SalePrice==100.10m,"default price change does not rewrite existing sale price");
        using(var db=new SqliteConnection($"Data Source={store.DatabasePath}")){db.Open();using var cmd=db.CreateCommand();cmd.CommandText="""
        UPDATE GuestRegistrations SET CheckedInAtUtc = CASE (SELECT RoomNumber FROM Rooms WHERE Id=RoomId)
          WHEN 101 THEN '2026-01-02 00:00:00' WHEN 105 THEN '2026-01-02 00:00:59.999' WHEN 106 THEN '2026-01-02 00:01:00' ELSE '2026-01-02 00:00:30' END;
        UPDATE GuestRegistrations SET SalePriceCents = NULL WHERE RoomId=(SELECT Id FROM Rooms WHERE RoomNumber=201);
        """;cmd.ExecuteNonQuery();}
        var bill=await store.ReadBillAsync("2026-01-02T08:00","2026-01-02T08:00");assert(bill.Count==3&&bill.TotalCents==15035&&bill.UnpricedCount==1,"minute inclusive boundaries and exact cents, unknown prices counted separately");
        var expanded=await store.ReadBillAsync("2026-01-02T08:00","2026-01-02T08:01");assert(expanded.Count==4&&expanded.TotalCents==15035&&expanded.UnpricedCount==1,"zero-price stays are valid; following minute included only when requested");
        await reject(()=>store.ReadBillAsync("bad","2026-01-02T08:00"),"malformed bill date rejected");await reject(()=>store.ReadBillAsync("2026-01-02T08:01","2026-01-02T08:00"),"reversed bill range rejected");
        await reject(()=>store.ExportBillAsync("2026-01-02T08:00","2026-01-02T08:00",null),"bill export requires password");
        var csv=Encoding.UTF8.GetString(await store.ExportBillAsync("2026-01-02T08:00","2026-01-02T08:00",pass));assert(csv.Contains("150.35")&&csv.Contains("1 笔未登记价格")&&!csv.Contains("\"106\""),"bill CSV has filtered rows and exact total");
        var stay=(await store.GetCurrentGuestAsync((await Room(101)).Id))!;
        await reject(()=>store.DeleteStayAsync(stay.Id,pass),"current stay cannot be deleted");var room101=await Room(101);await store.ChangeAsync(room101.Id,room101.Version,"CheckOut");
        await reject(()=>store.DeleteStayAsync(stay.Id,null),"deletion requires password");await store.DeleteStayAsync(stay.Id,pass);
        bill=await store.ReadBillAsync("2026-01-02T08:00","2026-01-02T08:00");assert(bill.Count==2&&bill.TotalCents==5025,"deleted archived stay excluded from bill totals");assert(!(await store.SearchStaysAsync()).Records.Any(x=>x.Id==stay.Id),"deleted stay excluded from search");
        assert(!(await store.SuggestGuestsAsync("账单测试")).Any(x=>x.RegistrationId==stay.Id),"deleted guest excluded from suggestions");
        using(var db=new SqliteConnection($"Data Source={store.DatabasePath}")){db.Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT count(*) FROM GuestRegistrations WHERE DeletedAtUtc IS NOT NULL";assert(Convert.ToInt32(cmd.ExecuteScalar())==1,"deletion is retained internally for recovery and audit");}
        using(var db=new SqliteConnection($"Data Source={store.DatabasePath}")){db.Open();using var cmd=db.CreateCommand();cmd.CommandText="""
        WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x<105)
        INSERT INTO GuestRegistrations (RoomId,Name,Phone,DocumentType,DocumentNumber,Notes,CheckedInAtUtc,CheckedOutAtUtc,SalePriceCents)
        SELECT (SELECT Id FROM Rooms WHERE RoomNumber=101),'分页账单','','','','','2026-01-02 00:00:30','2026-01-02 01:00:00',1 FROM n;
        """;cmd.ExecuteNonQuery();}
        var many=await store.ReadBillAsync("2026-01-02T08:00","2026-01-02T08:00");
        var manyCsv=Encoding.UTF8.GetString(await store.ExportBillAsync("2026-01-02T08:00","2026-01-02T08:00",pass));
        assert(many.Count==107&&many.Records.Count==100&&many.TotalCents==5130,"bill preview limit does not limit total count or money");
        assert(manyCsv.Split("分页账单").Length==106,"bill CSV exports all rows beyond preview limit");
        var backup=await store.BackupAsync(pass);assert(await new BoardStore(backup).HasPasswordAsync(),"protected backup retains security configuration");
        // Simulate the prior schema, preserving an existing stay. Re-initialization must be additive.
        var old=new BoardStore(Path.Combine(root,"old-schema.db"));await old.InitializeAsync();
        using(var db=new SqliteConnection($"Data Source={old.DatabasePath}")){db.Open();using var cmd=db.CreateCommand();cmd.CommandText="ALTER TABLE BoardStates DROP COLUMN DefaultPriceCents; DROP INDEX IX_Stays_BillCover; ALTER TABLE GuestRegistrations DROP COLUMN SalePriceCents; DROP INDEX IX_GuestRegistrations_BillTime; DROP INDEX IF EXISTS IX_Stays_Suggest; DROP INDEX IF EXISTS IX_Stays_TextScan; DROP INDEX IF EXISTS IX_Stays_ShortText; DROP INDEX IF EXISTS IX_Stays_Page; DROP INDEX IF EXISTS IX_Stays_VisibleId; DROP INDEX IF EXISTS IX_Stays_StatusId; DROP INDEX IF EXISTS IX_Stays_Identity; ALTER TABLE GuestRegistrations DROP COLUMN DeletedAtUtc; DROP TABLE SecuritySettings;";cmd.ExecuteNonQuery();}
        await old.InitializeAsync();await old.InitializeAsync();assert((await old.ReadAsync()).Rooms.Count==24&&!(await old.HasPasswordAsync()),"old schema upgrades without resetting rooms or installing a default password");
    }
}
