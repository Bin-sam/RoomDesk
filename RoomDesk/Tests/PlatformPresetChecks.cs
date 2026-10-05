using RoomDesk.Core;
using Microsoft.Data.Sqlite;
static class PlatformPresetChecks
{
    public static async Task Run(string root,Action<bool,string> check,Func<Func<Task>,string,Task> rejected)
    {
        var store=new BoardStore(Path.Combine(root,"platform-presets.db"));await store.InitializeAsync();
        const string password="Platform-test-only-2026";await store.SetPasswordAsync(password);
        var defaults=await store.ReadPlatformPresetsAsync();
        check(defaults.Select(p=>p.Name).SequenceEqual(BoardStore.DefaultPlatforms),"four default platform chips seeded in order");
        await store.InitializeAsync();check((await store.ReadPlatformPresetsAsync()).Count==4,"initialization does not duplicate chips");
        await store.AddPlatformPresetAsync("  微信  ");
        check((await store.ReadPlatformPresetsAsync()).Last().Name=="微信","custom chip trimmed and persisted");
        await rejected(()=>store.AddPlatformPresetAsync("微信"),"duplicate chip rejected");
        foreach(var invalid in new string?[]{null,"  ",new string('a',41),"A\nB"})
            await rejected(()=>store.AddPlatformPresetAsync(invalid),"invalid chip rejected");
        await store.AddPlatformPresetAsync("OTA");
        await rejected(()=>store.AddPlatformPresetAsync("ota"),"chip uniqueness is case-insensitive");
        var room=(await store.ReadAsync()).Rooms.Single(r=>r.Number==101);
        await store.ChangeAsync(room.Id,room.Version,"Reserve",reservation:new("自由平台预订"," 小红书 "));
        check((await store.GetReservationAsync(room.Id))?.Platform=="小红书","reservation accepts a platform outside saved chips");
        room=(await store.ReadAsync()).Rooms.Single(r=>r.Number==101);
        await store.ChangeAsync(room.Id,room.Version,"CheckIn",new("自由平台入住",SalePrice:199,Platform:"OTA官网"));
        check((await store.GetCurrentGuestAsync(room.Id))?.Platform=="OTA官网","check-in accepts a platform outside saved chips");
        check((await store.SearchStaysAsync("ota")).Total==1,"custom platform search is case-insensitive");
        var target=defaults.Single(p=>p.Name=="携程");
        await rejected(()=>store.RemovePlatformPresetAsync(target.Id,null),"chip deletion requires operation password");
        await rejected(()=>store.RemovePlatformPresetAsync(target.Id,"wrong"),"chip deletion rejects incorrect password");
        await store.RemovePlatformPresetAsync(target.Id,password);
        check(!(await store.ReadPlatformPresetsAsync()).Any(p=>p.Id==target.Id),"deleted chip hidden");
        var reopen=new BoardStore(store.DatabasePath);await reopen.InitializeAsync();
        check(!(await reopen.ReadPlatformPresetsAsync()).Any(p=>p.Id==target.Id),"deleted default chip stays deleted after reopening");
        check((await reopen.GetCurrentGuestAsync(room.Id))?.Platform=="OTA官网","chip deletion leaves guest records unchanged");
        await reopen.AddPlatformPresetAsync("携程");
        check((await reopen.ReadPlatformPresetsAsync()).Single(p=>p.Name=="携程").Id==target.Id,"adding same name restores removed chip");
        foreach(var p in await reopen.ReadPlatformPresetsAsync())await reopen.RemovePlatformPresetAsync(p.Id,password);
        await reopen.InitializeAsync();
        check((await reopen.ReadPlatformPresetsAsync()).Count==0,"removing all chips does not reseed on initialization");
        check(BoardStore.ValidateGuest(new("任意输入",SalePrice:1,Platform:"无气泡仍可输入")).Platform=="无气泡仍可输入","empty chip list still allows arbitrary platform input");
        for(int i=0;i<30;i++)await reopen.AddPlatformPresetAsync("自定义"+i);
        await rejected(()=>reopen.AddPlatformPresetAsync("超出上限"),"chip count limited to 30");
        // Reproduce old database migration; original stay fields remain untouched.
        using(var db=new SqliteConnection("Data Source="+store.DatabasePath)){db.Open();using var cmd=db.CreateCommand();cmd.CommandText="DROP TABLE PlatformPresets";cmd.ExecuteNonQuery();}
        await reopen.InitializeAsync();
        check((await reopen.ReadPlatformPresetsAsync()).Count==4&&(await reopen.GetCurrentGuestAsync(room.Id))?.Platform=="OTA官网","old database gets default chips without changing stays");
    }
}
