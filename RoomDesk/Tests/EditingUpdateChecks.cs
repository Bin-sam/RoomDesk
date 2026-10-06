using RoomDesk.Core;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

static class EditingUpdateChecks
{
    public static async Task Run(string root,Action<bool,string> check,Func<Func<Task>,string,Task> rejected){
        var clock=new ManualClock();var store=new BoardStore(Path.Combine(root,"editing.db"),clock);await store.InitializeAsync(24);
        async Task<RoomCard> Room(int n=101)=>(await store.ReadAsync()).Rooms.Single(r=>r.Number==n);
        var room=await Room();await store.EditRoomAsync(room.Id,room.Version,new(9,"影音房",258.88m,"maintenance",false));room=await Room();
        check(room.Floor==9&&room.Type=="影音房"&&room.DefaultPrice==258.88m&&room.StatusKey=="maintenance","room edit persists floor type price and status together");
        await rejected(()=>store.EditRoomAsync(room.Id,room.Version-1,new(2,"标准间",1,"ready",true)),"stale room edits rejected");
        await rejected(()=>store.EditRoomAsync(room.Id,room.Version,new(0,"标准间",1,"ready",true)),"invalid floor rejected atomically");
        await rejected(()=>store.EditRoomAsync(room.Id,room.Version,new(2,"标准间",-1,"ready",true)),"negative edited price rejected");
        await store.EditRoomAsync(room.Id,room.Version,new(9,"影音房",258.88m,"ready",true));room=await Room();
        await store.ChangeAsync(room.Id,room.Version,"Reserve",reservation:new("预订初始","携程","PHONE-A"));room=await Room();
        await store.EditReservationAsync(room.Id,room.Version,new("预订修改","线下","PHONE-B"));
        check((await store.GetReservationAsync(room.Id))?.Name=="预订修改"&&(await Room()).ReservationName=="预订修改","reservation edits persist and update board name");
        await rejected(()=>store.EditReservationAsync(room.Id,room.Version,new("旧窗口","飞猪")),"stale reservation cannot overwrite edits");room=await Room();
        await rejected(()=>store.EditRoomAsync(room.Id,room.Version,new(9,"标准间",10,"ready",true)),"room editor cannot discard reservation");
        await store.ChangeAsync(room.Id,room.Version,"CheckIn",new("入住初始","PHONE-A","其他","OLD-DOC",SalePrice:258,Platform:"美团"));room=await Room();
        var original=await store.GetCurrentGuestAsync(room.Id);await store.EditGuestAsync(room.Id,room.Version,new("入住修改","PHONE-B","护照","NEW-DOC","备注修改",280,"飞猪"));var edited=await store.GetCurrentGuestAsync(room.Id);
        check(edited!.Id==original!.Id&&edited.CheckedInAtUtc==original.CheckedInAtUtc&&edited.Name=="入住修改"&&edited.SalePrice==280&&edited.Platform=="飞猪","guest edit preserves identity and timestamp while updating data");
        check((await store.SearchStaysAsync("NEW-DOC")).Total==1&&(await store.SearchStaysAsync("OLD-DOC")).Total==0,"edited document refreshes search index");
        check((await store.SuggestGuestsAsync("入住修改")).Single().Phone=="PHONE-B","edited guest refreshes historical matching");
        await rejected(()=>store.EditGuestAsync(room.Id,room.Version,new("旧窗口",SalePrice:1)),"stale guest edits rejected");room=await Room();
        await rejected(()=>store.EditRoomAsync(room.Id,room.Version,new(2,"标准间",1,"dirty",false)),"room editor cannot bypass checkout");
        await store.EditRoomAsync(room.Id,room.Version,new(8,"亲子房",300,"occupied",false));room=await Room();
        check((await store.GetCurrentGuestAsync(room.Id))!.SalePrice==280&&!room.IsClean&&room.Floor==8,"room changes preserve current billed price and guest");
        await store.ChangeAsync(room.Id,room.Version,"CheckOut");room=await Room();
        await rejected(()=>store.EditGuestAsync(room.Id,room.Version,new("已退房修改",SalePrice:1)),"checked-out records cannot be changed through current guest editor");
        await rejected(()=>store.EditReservationAsync(room.Id,room.Version,new("非预订")),"nonreserved room rejects reservation edit");
        await rejected(()=>store.UnlockAsync("incorrect"),"session cannot be issued before password setup");
        await store.SetPasswordAsync("Update-Test-2026");await rejected(()=>store.UnlockAsync("incorrect"),"wrong password cannot obtain session");
        var session=await store.UnlockAsync("Update-Test-2026");check(await store.SessionValidAsync(session.Token),"valid password issues session");
        var backup=await store.BackupAsync(session.Token);check(File.Exists(backup),"session authorizes backup without plaintext password");
        clock.Now=clock.Now.AddMinutes(4.99);check(await store.SessionValidAsync(session.Token),"session remains valid before five minutes");
        clock.Now=clock.Now.AddSeconds(1);check(!await store.SessionValidAsync(session.Token),"session expires at five minutes without sliding extension");
        await rejected(()=>store.BackupAsync(session.Token),"expired session rejected by protected backend");
        session=await store.UnlockAsync("Update-Test-2026");await store.LockSessionAsync(session.Token);check(!await store.SessionValidAsync(session.Token),"explicit lock revokes session");
        session=await store.UnlockAsync("Update-Test-2026");await store.SetPasswordAsync("New-Update-2026","Update-Test-2026");check(!await store.SessionValidAsync(session.Token),"password change revokes all sessions");
        session=await store.UnlockAsync("New-Update-2026");var reopened=new BoardStore(store.DatabasePath);check(!await reopened.SessionValidAsync(session.Token),"authorization does not survive process/store restart");
        var data=Encoding.UTF8.GetBytes("synthetic installer test payload");var sha=Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        object Release(string tag,string version,string? digest=null,string? url=null,bool draft=false)=>new{tag_name=tag,draft,body="升级说明",assets=new[]{new{name=$"RoomDesk-Setup-{version}-win-x64.exe",state="uploaded",digest=digest??"sha256:"+sha,browser_download_url=url??$"https://github.com/Bin-sam/RoomDesk/releases/download/{tag}/RoomDesk-Setup-{version}-win-x64.exe",size=data.Length}}};
        string List(params object[] rows)=>JsonSerializer.Serialize(rows);
        check(SoftwareUpdates.SelectRelease(List(Release(SoftwareUpdates.CurrentTag,"0.8.1")))==null,"updater ignores current version");
        check(SoftwareUpdates.SelectRelease(List(Release(SoftwareUpdates.CurrentTag,SoftwareUpdates.CurrentVersion)),"roomdesk-v0.8.0-preview.1")?.Tag==SoftwareUpdates.CurrentTag,"0.8.0 updater detects 0.8.1 patch release");
        var selected=SoftwareUpdates.SelectRelease(List(Release("roomdesk-v0.9.0-preview.2","0.9.0"),Release("roomdesk-v0.9.0","0.9.0"),Release("roomdesk-v1.0.0","1.0.0",draft:true)));
        check(selected?.Tag=="roomdesk-v0.9.0","updater prefers stable and ignores draft releases");
        check(SoftwareUpdates.SelectRelease(List(Release("roomdesk-v0.8.1-preview.2","0.8.1")))!=null,"updater recognizes higher preview revision");
        check(SoftwareUpdates.SelectRelease(List(Release("roomdesk-v0.7.0-preview.9","0.7.0")))==null,"updater never downgrades");
        check(SoftwareUpdates.SelectRelease(List(Release("roomdesk-v0.9.0","0.9.0",digest:"bad")))==null,"updater ignores unverified installer metadata");
        check(SoftwareUpdates.SelectRelease(List(Release("roomdesk-v0.9.0","0.9.0",url:"https://example.com/payload.exe")))==null,"updater rejects installer URLs outside this release");
        var target=Path.Combine(root,"updates");using var client=new HttpClient(new PayloadHandler(data));var updater=new SoftwareUpdates(client);
        var file=await updater.DownloadAsync(selected!,target);check(File.ReadAllBytes(file).SequenceEqual(data),"update download validates size and hash before finalizing");
        var failed=false;try{await updater.DownloadAsync(selected! with{Sha256=new string('0',64)},target);}catch(BoardException){failed=true;}
        check(failed&&!Directory.EnumerateFiles(target,"*.partial").Any(),"corrupt update rejected and partial file removed");
        check(File.ReadAllBytes(file).SequenceEqual(data),"failed download preserves previous verified package");
    }
    private sealed class ManualClock:TimeProvider{public DateTimeOffset Now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now;}
    private sealed class PayloadHandler(byte[] payload):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(payload)});}
}
