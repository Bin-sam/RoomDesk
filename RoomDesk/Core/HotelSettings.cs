using Microsoft.EntityFrameworkCore;
namespace RoomDesk.Core;
public sealed class HotelSetting
{
    public int Id {get;set;}=1;
    public string Name {get;set;}="栖间";
}
public sealed partial class BoardStore
{
    public async Task<string> ReadHotelNameAsync(){await using var db=Open();return await db.HotelSettings.Where(s=>s.Id==1).Select(s=>s.Name).SingleAsync();}
    public async Task SetHotelNameAsync(string? name)
    {
        var value=name?.Trim()??"";
        if(value.Length is <1 or >60 || value.Any(char.IsControl))throw new BoardException("酒店名称须为 1–60 个字，不能包含换行。");
        await gate.WaitAsync();try{await using var db=Open();var settings=await db.HotelSettings.SingleAsync(s=>s.Id==1);var before=settings.Name;settings.Name=value;db.Activities.Add(new(){RoomNumber=0,Action="修改酒店名称",Before=before,After=value,AtUtc=DateTime.UtcNow});await db.SaveChangesAsync();}finally{gate.Release();}
    }
    public async Task DeleteRoomAsync(int id,long version,string? password)
    {
        await RequirePasswordAsync(password);await gate.WaitAsync();
        try{
            await using var db=Open();await using var tx=await db.Database.BeginTransactionAsync();
            var state=await db.BoardStates.Include(s=>s.Room).SingleOrDefaultAsync(s=>s.RoomId==id)??throw new BoardException("房间不存在或已删除。");
            if(state.Version!=version)throw new BoardException("房间已更新，请刷新后重试。");
            if(state.Occupancy!=Occupancy.Vacant || await db.GuestRegistrations.AnyAsync(g=>g.RoomId==id&&g.CheckedOutAtUtc==null))throw new BoardException("在住或已预订房间不能删除，请先退房或取消预订。");
            state.IsDeleted=true;state.Version++;
            db.Activities.Add(new(){RoomNumber=state.Room.RoomNumber,Action="删除房间",Before=Description(state),After="已移入已删除房间，可恢复；历史记录保留",AtUtc=DateTime.UtcNow});
            await db.SaveChangesAsync();await tx.CommitAsync();
        }finally{gate.Release();}
    }
    public async Task RestoreRoomAsync(int id,long version)
    {
        await gate.WaitAsync();try{
            await using var db=Open();var state=await db.BoardStates.IgnoreQueryFilters().Include(s=>s.Room).SingleOrDefaultAsync(s=>s.RoomId==id&&s.IsDeleted)??throw new BoardException("已删除房间不存在。");
            if(state.Version!=version)throw new BoardException("房间已更新，请刷新后重试。");
            state.IsDeleted=false;state.Version++;state.Service=ServiceState.Normal;state.IsClean=false;state.Room.Status=LegacyStatus(state);
            db.Activities.Add(new(){RoomNumber=state.Room.RoomNumber,Action="恢复房间",Before="已删除",After="空房 / 待清扫 / 正常",AtUtc=DateTime.UtcNow});await db.SaveChangesAsync();
        }finally{gate.Release();}
    }
}
