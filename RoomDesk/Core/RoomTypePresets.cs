using Microsoft.EntityFrameworkCore;
namespace RoomDesk.Core;

public sealed class RoomTypePreset
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string NameKey { get; set; } = "";
    public bool IsHidden { get; set; }
}
public sealed record RoomTypeWord(long Id, string Name);

public sealed partial class BoardStore
{
    public async Task<IReadOnlyList<RoomTypeWord>> ReadRoomTypePresetsAsync()
    {
        await using var db=Open();
        return await db.RoomTypePresets.AsNoTracking().Where(p=>!p.IsHidden).OrderBy(p=>p.Id).Select(p=>new RoomTypeWord(p.Id,p.Name)).ToListAsync();
    }
    public async Task AddRoomTypePresetAsync(string? name)
    {
        var value=ValidateRoomType(name ?? "");var key=value.ToUpperInvariant();
        await gate.WaitAsync();
        try
        {
            await using var db=Open();
            var found=await db.RoomTypePresets.SingleOrDefaultAsync(p=>p.NameKey==key);
            if(found is { IsHidden:false })throw new BoardException("这个常用房型已经存在。");
            if(await db.RoomTypePresets.CountAsync(p=>!p.IsHidden)>=30)throw new BoardException("最多保存 30 个常用房型，请先移除不常用的气泡。");
            if(found!=null){found.IsHidden=false;found.Name=value;}
            else db.RoomTypePresets.Add(new(){Name=value,NameKey=key});
            await db.SaveChangesAsync();
        }
        finally{gate.Release();}
    }
    public async Task RemoveRoomTypePresetAsync(long id,string? password)
    {
        await RequirePasswordAsync(password);
        await gate.WaitAsync();
        try
        {
            await using var db=Open();
            var word=await db.RoomTypePresets.SingleOrDefaultAsync(p=>p.Id==id)??throw new BoardException("常用房型不存在，请刷新。");
            word.IsHidden=true;await db.SaveChangesAsync();
        }
        finally{gate.Release();}
    }
}
