using Microsoft.EntityFrameworkCore;
namespace RoomDesk.Core;

public sealed record RoomEdit(int Floor,string Type,decimal? DefaultPrice,string StatusKey,bool IsClean);
public sealed partial class BoardStore
{
    public async Task EditRoomAsync(int id,long version,RoomEdit input)
    {
        if(input is null)throw new BoardException("请填写房间资料。");
        if(input.Floor is <1 or >99)throw new BoardException("楼层须为 1–99。");
        var type=ValidateRoomType(input.Type);var price=input.DefaultPrice.HasValue?PriceCents(input.DefaultPrice.Value):(long?)null;
        await gate.WaitAsync();
        try{
            await using var db=Open();await using var tx=await db.Database.BeginTransactionAsync();
            var s=await db.BoardStates.Include(s=>s.Room).SingleOrDefaultAsync(s=>s.RoomId==id)??throw new BoardException("房间不存在或已删除。");
            if(s.Version!=version)throw new BoardException("房间已更新，请刷新后重新编辑。");
            var before=$"{s.Room.FloorNumber} 楼 / {Card(s).Type} / {PriceText(s.DefaultPriceCents)} / {Description(s)}";
            if(s.Occupancy!=Occupancy.Vacant){
                if(input.StatusKey!=Card(s).StatusKey)throw new BoardException("请先办理退房或取消预订，再修改为空房状态。");
                s.IsClean=input.IsClean;
            }else{
                switch(input.StatusKey){
                    case "ready":s.Service=ServiceState.Normal;s.IsClean=true;break;
                    case "dirty":s.Service=ServiceState.Normal;s.IsClean=false;break;
                    case "maintenance":s.Service=ServiceState.Maintenance;s.IsClean=false;break;
                    case "disabled":s.Service=ServiceState.Disabled;s.IsClean=false;break;
                    default:throw new BoardException("入住或预订须填写客人信息，请在房态页办理。");
                }
            }
            s.Room.FloorNumber=input.Floor;s.TypeName=type;s.DefaultPriceCents=price;s.Version++;s.Room.Status=LegacyStatus(s);
            if(s.IsClean)s.Room.LastCleaned=DateTime.Now;
            db.Activities.Add(new RoomActivity{RoomNumber=s.Room.RoomNumber,Action="编辑房间资料与状态",Before=before,After=$"{input.Floor} 楼 / {type} / {PriceText(price)} / {Description(s)}",AtUtc=DateTime.UtcNow});
            await db.SaveChangesAsync();await tx.CommitAsync();
        }catch(DbUpdateConcurrencyException){throw new BoardException("房间已更新，请刷新后重新编辑。");}finally{gate.Release();}
    }
    public async Task EditGuestAsync(int id,long version,GuestInput input)
    {
        var g=ValidateGuest(input);await gate.WaitAsync();
        try{
            await using var db=Open();await using var tx=await db.Database.BeginTransactionAsync();
            var s=await db.BoardStates.Include(s=>s.Room).SingleOrDefaultAsync(s=>s.RoomId==id)??throw new BoardException("房间不存在。");
            if(s.Version!=version)throw new BoardException("房间已更新，请刷新后重新编辑。");
            if(s.Occupancy!=Occupancy.Occupied)throw new BoardException("此房间已不在住，不能修改入住信息。");
            var row=await db.GuestRegistrations.SingleOrDefaultAsync(g=>g.RoomId==id&&g.CheckedOutAtUtc==null);
            var before=row==null?"未登记":$"{row.Name} / {PriceText(row.SalePriceCents)}";
            if(row==null){row=new GuestRegistration{RoomId=id,CheckedInAtUtc=DateTime.UtcNow};db.GuestRegistrations.Add(row);}
            row.Name=g.Name;row.Phone=g.Phone!;row.DocumentType=g.DocumentType!;row.DocumentNumber=g.DocumentNumber!;row.Platform=g.Platform!;row.Notes=g.Notes!;row.SalePriceCents=PriceCents(g.SalePrice!.Value);s.Version++;
            db.Activities.Add(new RoomActivity{RoomNumber=s.Room.RoomNumber,Action="编辑入住信息",Before=before,After=$"{g.Name} / {PriceText(row.SalePriceCents)}",AtUtc=DateTime.UtcNow});
            await db.SaveChangesAsync();await tx.CommitAsync();
        }catch(DbUpdateConcurrencyException){throw new BoardException("房间已更新，请刷新后重新编辑。");}finally{gate.Release();}
    }
    public async Task EditReservationAsync(int id,long version,ReservationInput input)
    {
        var r=ValidateReservation(input);await gate.WaitAsync();
        try{
            await using var db=Open();var s=await db.BoardStates.Include(s=>s.Room).SingleOrDefaultAsync(s=>s.RoomId==id)??throw new BoardException("房间不存在。");
            if(s.Version!=version)throw new BoardException("房间已更新，请刷新后重新编辑。");
            if(s.Occupancy!=Occupancy.Reserved)throw new BoardException("预订已变更，不能修改预订信息。");
            var before=s.ReservationName??"未登记";s.ReservationName=r.Name;s.ReservationPhone=r.Phone;s.ReservationPlatform=r.Platform;s.Version++;
            db.Activities.Add(new RoomActivity{RoomNumber=s.Room.RoomNumber,Action="编辑预订信息",Before=before,After=r.Name,AtUtc=DateTime.UtcNow});await db.SaveChangesAsync();
        }catch(DbUpdateConcurrencyException){throw new BoardException("房间已更新，请刷新后重新编辑。");}finally{gate.Release();}
    }
}
