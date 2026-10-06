using RoomDesk.Core;
using Microsoft.Data.Sqlite;
static class LongevityChecks
{
    public static async Task Run(string root,Action<bool,string> check,Func<Func<Task>,string,Task> rejected)
    {
        var store=new BoardStore(Path.Combine(root,"durable.db"));await store.InitializeAsync(24);
        await store.SetHotelNameAsync("  云栖酒店  ");check(await store.ReadHotelNameAsync()=="云栖酒店","hotel name trimmed and saved");
        await rejected(()=>store.SetHotelNameAsync(" "),"blank hotel name rejected");await rejected(()=>store.SetHotelNameAsync(new string('x',61)),"long hotel name rejected");
        const string password="Longevity-test-only-2026";await store.SetPasswordAsync(password);
        var occupied=(await store.ReadAsync()).Rooms.Single(r=>r.Number==102);await rejected(()=>store.DeleteRoomAsync(occupied.Id,occupied.Version,password),"occupied room deletion rejected");
        var reserved=(await store.ReadAsync()).Rooms.Single(r=>r.Number==103);await rejected(()=>store.DeleteRoomAsync(reserved.Id,reserved.Version,password),"reserved room deletion rejected");
        var room=(await store.ReadAsync()).Rooms.Single(r=>r.Number==101);
        await store.ChangeAsync(room.Id,room.Version,"CheckIn",new("历史测试",SalePrice:200,Notes:"文字%_引号\"AB",Platform:"线下"));await store.ChangeAsync(room.Id,room.Version+1,"CheckOut");
        await rejected(()=>store.DeleteRoomAsync(room.Id,room.Version+2,null),"room deletion requires password");
        await rejected(()=>store.DeleteRoomAsync(room.Id,room.Version,password),"stale room deletion rejected");
        await store.DeleteRoomAsync(room.Id,room.Version+2,password);
        check(!(await store.ReadAsync()).Rooms.Any(r=>r.Id==room.Id),"deleted room hidden from board");
        check((await store.SearchStaysAsync("历史测试")).Total==1,"deleting room preserves searchable historical stays");
        check((await store.ReadBillAsync("2000-01-01T00:00","2100-01-01T00:00")).TotalCents==20000,"deleting room preserves bill totals");
        await rejected(()=>store.ChangeAsync(room.Id,room.Version+3,"Clean"),"deleted room rejects stale board action");
        await store.AddRoomAsync(101,9,"双床",388);
        var added=(await store.ReadAsync()).Rooms.Single(r=>r.Number==101);
        check(added.Id==room.Id&&added.Floor==9&&added.Type=="双床"&&added.DefaultPriceCents==38800&&added.StatusKey=="dirty","re-add deleted number uses new details and retains identity");
        check((await store.ReadAsync(true)).Rooms.Count(r=>r.Number==101)==1,"re-add has no duplicate archived room");
        check((await store.SearchStaysAsync("历史测试")).Total==1&&(await store.ReadBillAsync("2000-01-01T00:00","2100-01-01T00:00")).TotalCents==20000,"re-add retains historical stays and bills");
        await rejected(()=>store.RestoreRoomAsync(room.Id,room.Version+3),"stale archived restore rejected after re-add");
        await store.DeleteRoomAsync(added.Id,added.Version,password);
        await rejected(()=>store.AddRoomsAsync("101,102,901",4,"亲子房",288),"mixed batch active conflict rejects all additions");
        check(!(await store.ReadAsync()).Rooms.Any(r=>r.Number==101||r.Number==901),"failed batch does not reactivate deleted room or create new room");
        await store.AddRoomsAsync("101,901",4,"亲子房");
        added=(await store.ReadAsync()).Rooms.Single(r=>r.Number==101);
        check(added.Floor==4&&added.Type=="亲子房"&&added.DefaultPriceCents==null&&(await store.ReadAsync()).Rooms.Any(r=>r.Number==901),"mixed batch re-add and new room succeeds and clears omitted price");
        for(var cycle=0;cycle<3;cycle++){
            await store.DeleteRoomAsync(added.Id,added.Version,password);await store.AddRoomAsync(101,4,"亲子房");
            var next=(await store.ReadAsync()).Rooms.Single(r=>r.Number==101);
            check(next.Id==added.Id&&next.Version==added.Version+2,"repeated delete and re-add increments version");added=next;
        }
        await store.DeleteRoomAsync(added.Id,added.Version,password);
        var deleted=(await store.ReadAsync(true)).Rooms.Single(r=>r.Id==room.Id);check(deleted.IsDeleted,"management can see deleted room");
        await store.RestoreRoomAsync(deleted.Id,deleted.Version);check((await store.ReadAsync()).Rooms.Single(r=>r.Id==room.Id).StatusKey=="dirty","restored room requires cleaning");
        var reopened=new BoardStore(store.DatabasePath);await reopened.InitializeAsync(24);check(await reopened.ReadHotelNameAsync()=="云栖酒店","hotel name persists across restart");
        check((await reopened.ReadStorageInfoAsync()) is {JournalMode:"wal",Synchronous:2},"every opened connection uses WAL FULL durable commits");
        check((await store.SearchStaysAsync("%_引号\"")).Total==1,"indexed search treats wildcard and quote characters literally");
        await reopened.MaintainAsync();check(reopened.MaintenanceError==null&&File.Exists(reopened.LastAutomaticBackup),"incremental automatic backup succeeds");
        var backup=new BoardStore(reopened.LastAutomaticBackup!);await backup.InitializeAsync(24);check(await backup.ReadHotelNameAsync()=="云栖酒店"&&(await backup.SearchStaysAsync()).Total==1,"automatic backup opens with hotel name and history intact");
        var before=reopened.LastAutomaticBackup;await reopened.MaintainAsync();check(reopened.LastAutomaticBackup==before,"daily backup is reused on repeated maintenance");
        // Additive upgrade from a database without new room lifecycle/name/search schema.
        using(var db=new SqliteConnection("Data Source="+store.DatabasePath)){db.Open();using var cmd=db.CreateCommand();cmd.CommandText="ALTER TABLE BoardStates DROP COLUMN IsDeleted; DROP TABLE HotelSettings; DROP TRIGGER StaySearch_insert; DROP TRIGGER StaySearch_delete; DROP TRIGGER StaySearch_update; DROP TABLE StaySearch;";cmd.ExecuteNonQuery();}
        await reopened.InitializeAsync(24);check((await reopened.SearchStaysAsync("历史测试")).Total==1,"upgrade rebuilds historical search index without losing stays");
    }
}
