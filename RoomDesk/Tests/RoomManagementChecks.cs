using RoomDesk.Core;
using Microsoft.Data.Sqlite;
static class RoomManagementChecks
{
    public static async Task Run(string root,Action<bool,string> check,Func<Func<Task>,string,Task> rejected)
    {
        var store=new BoardStore(Path.Combine(root,"room-batch.db"));await store.InitializeAsync();
        var baseline=(await store.ReadAsync()).Rooms;
        check(baseline.First().Type=="标准间","old enum room type still displayed");
        check(BoardStore.ParseRoomNumbers(" 401 – 403、405，407\n409至410 ").SequenceEqual(new[]{401,402,403,405,407,409,410}),"mixed batch ranges and separators parsed");
        foreach(var text in new string?[]{null," ","0","100000","408-401","401,401","401-403,403-405","1-201","1.5","a","1,,a",",,,"})
            await rejected(()=>store.AddRoomsAsync(text,4,"亲子房"),"invalid batch rejected without writing");
        await rejected(()=>store.AddRoomsAsync("401-403",0,"亲子房"),"invalid batch floor rejected");
        await rejected(()=>store.AddRoomsAsync("401-403",4," "),"empty custom room type rejected");
        await rejected(()=>store.AddRoomsAsync("401-403",4,new string('x',41)),"long custom room type rejected");
        await rejected(()=>store.AddRoomsAsync("401-403",4,"亲子房",-1),"invalid batch price rejected");
        await rejected(()=>store.AddRoomsAsync("401,101,402",4,"亲子房",288),"existing room rejects entire batch");
        check((await store.ReadAsync()).Rooms.Count==baseline.Count,"all invalid batches leave room count unchanged");
        var added=await store.AddRoomsAsync("401-403,405",4,"  亲子房  ",288.50m);
        var snapshot=await store.ReadAsync();var newRooms=snapshot.Rooms.Where(r=>added.Contains(r.Number)).ToList();
        check(newRooms.Count==4 && newRooms.All(r=>r.Type=="亲子房"&&r.Floor==4&&r.StatusKey=="dirty"&&r.DefaultPrice==288.50m),"batch saves custom type floor price and dirty state for each room");
        check(snapshot.Activities.Count(a=>a.Action=="批量新增房间")==4,"batch produces individual room activity records");
        await rejected(()=>store.AddRoomsAsync("403,406",4,"亲子房"),"overlap on resubmission rejects entire batch");
        check(!(await store.ReadAsync()).Rooms.Any(r=>r.Number==406),"batch conflict creates no partial rooms");
        await store.AddRoomAsync(501,5,"Deluxe");await store.AddRoomAsync(502,5,"影音房");
        check((await store.ReadAsync()).Rooms.Single(r=>r.Number==501).Type=="豪华房","legacy API room type names remain compatible");
        var presets=await store.ReadRoomTypePresetsAsync();check(presets.Select(p=>p.Name).SequenceEqual(BoardStore.DefaultRoomTypes),"default room type chips seeded");
        await store.AddRoomTypePresetAsync("  亲子房 ");await rejected(()=>store.AddRoomTypePresetAsync("亲子房"),"duplicate room type chip rejected");
        await store.AddRoomTypePresetAsync("VIP双床");await rejected(()=>store.AddRoomTypePresetAsync("vip双床"),"room type chips case-insensitive");
        await rejected(()=>store.AddRoomTypePresetAsync("\n"),"invalid room type chip rejected");
        await rejected(()=>store.RemoveRoomTypePresetAsync(presets[0].Id,null),"room type delete requires password setup");
        const string password="Room-type-test-only-2026";await store.SetPasswordAsync(password);
        await rejected(()=>store.RemoveRoomTypePresetAsync(presets[0].Id,"wrong"),"room type delete rejects wrong password");
        await store.RemoveRoomTypePresetAsync(presets[0].Id,password);
        var reopen=new BoardStore(store.DatabasePath);await reopen.InitializeAsync();
        check(!(await reopen.ReadRoomTypePresetsAsync()).Any(p=>p.Name=="标准间"),"deleted default type chip stays hidden after restart");
        check((await reopen.ReadAsync()).Rooms.Single(r=>r.Number==401).Type=="亲子房"&&(await reopen.ReadAsync()).Rooms.First().Type=="标准间","custom types persist and removed chips do not change room types");
        check((await reopen.ReadPlatformPresetsAsync()).Count==4,"room type vocabulary independent of reservation platforms");
        await reopen.AddRoomTypePresetAsync("标准间");check((await reopen.ReadRoomTypePresetsAsync()).Single(p=>p.Name=="标准间").Id==presets[0].Id,"re-adding room type restores same chip");
        // Concurrent submissions must create one complete batch only.
        async Task<bool> Attempt(){try{await reopen.AddRoomsAsync("601-603",6,"双床房",99);return true;}catch(BoardException){return false;}}
        var results=await Task.WhenAll(Attempt(),Attempt());check(results.Count(x=>x)==1&&(await reopen.ReadAsync()).Rooms.Count(r=>r.Number>=601&&r.Number<=603)==3,"concurrent duplicate batches do not create duplicates");
        await reopen.AddRoomsAsync("1000-1199",10,"团体房",0);check((await reopen.ReadAsync()).Rooms.Count(r=>r.Number>=1000)==200,"200-room boundary accepted with zero price");
        using(var db=new SqliteConnection("Data Source="+store.DatabasePath)){db.Open();using var cmd=db.CreateCommand();cmd.CommandText="DROP TABLE RoomTypePresets";cmd.ExecuteNonQuery();}
        await reopen.InitializeAsync();check((await reopen.ReadRoomTypePresetsAsync()).Count==5&&(await reopen.ReadAsync()).Rooms.Single(r=>r.Number==402).Type=="亲子房","preset migration preserves custom room data");
    }
}
