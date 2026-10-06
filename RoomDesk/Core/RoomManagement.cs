using System.Globalization;
using System.Text.RegularExpressions;
using LantelHotelApp.Models;
using LantelHotelApp.Models.Enums;
using Microsoft.EntityFrameworkCore;
namespace RoomDesk.Core;

public sealed partial class BoardStore
{
    public static readonly IReadOnlyList<string> DefaultRoomTypes = new[]{"标准双人间","豪华单人房","豪华双人房","商务双人房【棋牌】","商务套房"};
    public static string ValidateRoomType(string? type)
    {
        var value=(type??"").Trim();
        if(value.Length is <1 or >40 || value.Any(char.IsControl))throw new BoardException("房型须为 1–40 个字符，不能包含换行或控制字符。");
        return value switch {"Standard"=>"标准间","Deluxe"=>"豪华房","ExecutiveSuite"=>"行政套房","VipSuite"=>"贵宾套房","Penthouse"=>"顶层套房",_=>value};
    }
    public static IReadOnlyList<int> ParseRoomNumbers(string? text)
    {
        if(string.IsNullOrWhiteSpace(text)||text.Length>5000)throw new BoardException("请填写房号，每批最多 200 间。");
        var normalized=Regex.Replace(text.Trim(),@"\s*[-–—~～至]\s*","-");
        var numbers=new List<int>();var seen=new HashSet<int>();
        foreach(var part in Regex.Split(normalized,@"[\s,，、;；]+"))
        {
            if(part.Length==0)continue;
            var match=Regex.Match(part,@"^([0-9]{1,5})(?:-([0-9]{1,5}))?$");
            if(!match.Success)throw new BoardException("房号格式不正确，请输入如 401-408、410。房号须为 1–99999。");
            var first=int.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture);
            var last=match.Groups[2].Success?int.Parse(match.Groups[2].Value,CultureInfo.InvariantCulture):first;
            if(first<1||last<first||last>99999)throw new BoardException("房号须为 1–99999，范围的结束房号不能小于起始房号。");
            if(last-first+1>200 || numbers.Count+last-first+1>200)throw new BoardException("每批最多添加 200 间，请分批添加。");
            for(int n=first;n<=last;n++){if(!seen.Add(n))throw new BoardException($"本批房号 {n} 重复，请修改后再添加。");numbers.Add(n);}
        }
        if(numbers.Count==0)throw new BoardException("请至少填写一个房号。");
        return numbers;
    }
    public async Task<IReadOnlyList<int>> AddRoomsAsync(string? numbersText,int floor,string? type,decimal? defaultPrice=null)
    {
        var numbers=ParseRoomNumbers(numbersText);
        if(floor is <1 or >99)throw new BoardException("楼层须为 1–99。");
        var name=ValidateRoomType(type);
        var price=defaultPrice.HasValue?PriceCents(defaultPrice.Value):(long?)null;
        var roomType=name switch {"豪华房"=>RoomType.Deluxe,"行政套房"=>RoomType.ExecutiveSuite,"贵宾套房"=>RoomType.VipSuite,"顶层套房"=>RoomType.Penthouse,_=>RoomType.Standard};
        await gate.WaitAsync();
        try
        {
            await using var db=Open();await using var tx=await db.Database.BeginTransactionAsync();
            var archived=await db.BoardStates.IgnoreQueryFilters().Include(s=>s.Room)
                .Where(s=>s.IsDeleted&&numbers.Contains(s.Room.RoomNumber)).ToDictionaryAsync(s=>s.Room.RoomNumber);
            var existing=await db.Rooms.Where(r=>numbers.Contains(r.RoomNumber)).Select(r=>r.RoomNumber).ToListAsync();
            var conflicts=existing.Where(n=>!archived.ContainsKey(n)).OrderBy(n=>n).ToList();
            if(conflicts.Count>0)throw new BoardException("以下房号已存在："+string.Join("、",conflicts)+"。本批未添加任何房间。");
            var archivedIds=archived.Values.Select(s=>s.RoomId).ToArray();
            if(await db.GuestRegistrations.AnyAsync(g=>archivedIds.Contains(g.RoomId)&&g.CheckedOutAtUtc==null))
                throw new BoardException("已删除房间仍有关联的在住记录，请先核对记录。本批未添加任何房间。");
            foreach(var number in numbers)
            {
                if(archived.TryGetValue(number,out var state))
                {
                    // Reuse the identity so historical stays and bills retain their room link.
                    state.IsDeleted=false;state.Version++;state.Occupancy=Occupancy.Vacant;state.Service=ServiceState.Normal;state.IsClean=false;
                    state.TypeName=name;state.DefaultPriceCents=price;ClearReservation(state);
                    state.Room.FloorNumber=floor;state.Room.RoomType=roomType;state.Room.Status=RoomStatus.ScheduledCleaning;state.Room.LastCleaned=null;
                }
                else db.BoardStates.Add(new BoardState {TypeName=name,DefaultPriceCents=price,Room=new Room {RoomNumber=number,FloorNumber=floor,RoomType=roomType,Status=RoomStatus.ScheduledCleaning,LastCleaned=null},IsClean=false});
                db.Activities.Add(new RoomActivity {RoomNumber=number,Action=state is not null?"重新添加已删除房间":numbers.Count==1?"新增房间":"批量新增房间",Before=state is not null?"已删除":"—",After="空房 / 待清扫 / 正常",AtUtc=DateTime.UtcNow});
            }
            await db.SaveChangesAsync();await tx.CommitAsync();return numbers;
        }
        finally{gate.Release();}
    }
}
