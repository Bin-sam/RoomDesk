using RoomDesk.Core;
static class DefaultRoomChecks
{
    public static async Task Run(string root,Action<bool,string> check)
    {
        var store=new BoardStore(Path.Combine(root,"default-rooms.db"));await store.InitializeAsync();
        var rooms=(await store.ReadAsync()).Rooms;
        var expected=new Dictionary<string,int[]> {
            ["标准双人间"]=new[]{8510,8402,8512,8410,8502,8602,8610},
            ["豪华单人房"]=new[]{8403,8611,8505,8608,8605,8503,8603,8508,8411,8405,8511,8408},
            ["豪华双人房"]=new[]{8606,8506,8406},
            ["商务双人房【棋牌】"]=new[]{8507,8509,8401,8609,8409,8407,8607,8601,8501},
            ["商务套房"]=new[]{8612,8412}
        };
        check(rooms.Count==33&&rooms.Select(r=>r.Number).Distinct().Count()==33,"fresh default has exactly 33 unique photo room numbers");
        foreach(var group in expected)check(rooms.Where(r=>r.Type==group.Key).Select(r=>r.Number).Order().SequenceEqual(group.Value.Order()),"photo room type matches: "+group.Key);
        check(rooms.All(r=>r.Floor==(r.Number/100)%10)&&new[]{4,5,6}.All(f=>rooms.Count(r=>r.Floor==f)==11),"photo defaults have 11 rooms on each of floors 4,5,6");
        check(rooms.All(r=>r.StatusKey=="dirty"&&r.DefaultPriceCents==null&&r.GuestName==null&&r.ReservationName==null),"defaults are vacant awaiting cleaning, no invented price or guest");
        check((await store.ReadRoomTypePresetsAsync()).Select(p=>p.Name).SequenceEqual(expected.Keys),"fresh default room type chips match photo types");
        await store.SetPasswordAsync("Defaults-test-2026");
        var room=rooms.Single(r=>r.Number==8401);await store.DeleteRoomAsync(room.Id,room.Version,"Defaults-test-2026");
        var other=rooms.Single(r=>r.Number==8402);await store.EditRoomAsync(other.Id,other.Version,new(7,"自定义房型",299,"ready",true));
        var reopened=new BoardStore(store.DatabasePath);await reopened.InitializeAsync();
        var after=(await reopened.ReadAsync()).Rooms;
        check(after.Count==32&&!after.Any(r=>r.Number==8401),"startup does not resurrect removed defaults");
        check(after.Single(r=>r.Number==8402) is {Floor:7,Type:"自定义房型",DefaultPriceCents:29900},"startup preserves edited default room metadata");
        var legacy=new BoardStore(Path.Combine(root,"legacy-defaults.db"));await legacy.InitializeAsync(24);
        var before=(await legacy.ReadAsync()).Rooms;await legacy.InitializeAsync();
        check(System.Text.Json.JsonSerializer.Serialize((await legacy.ReadAsync()).Rooms)==System.Text.Json.JsonSerializer.Serialize(before),"upgrade does not replace or append to existing room list");
    }
}
