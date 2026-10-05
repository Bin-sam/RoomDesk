using Microsoft.EntityFrameworkCore;
namespace RoomDesk.Core;

public sealed class PlatformPreset
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string NameKey { get; set; } = "";
    public bool IsHidden { get; set; }
}
public sealed record PlatformWord(long Id, string Name);

public sealed partial class BoardStore
{
    public async Task<IReadOnlyList<PlatformWord>> ReadPlatformPresetsAsync()
    {
        await using var db=Open();
        return await db.PlatformPresets.AsNoTracking().Where(p=>!p.IsHidden).OrderBy(p=>p.Id).Select(p=>new PlatformWord(p.Id,p.Name)).ToListAsync();
    }
    public async Task AddPlatformPresetAsync(string? name)
    {
        var value=ValidatePlatform(name ?? "");var key=value.ToUpperInvariant();
        await gate.WaitAsync();
        try
        {
            await using var db=Open();
            var found=await db.PlatformPresets.SingleOrDefaultAsync(p=>p.NameKey==key);
            if(found is { IsHidden:false })throw new BoardException("这个常用平台已经存在。");
            if(await db.PlatformPresets.CountAsync(p=>!p.IsHidden)>=30)throw new BoardException("最多保存 30 个常用平台，请先移除不常用的气泡。");
            if(found!=null){found.IsHidden=false;found.Name=value;}
            else db.PlatformPresets.Add(new(){Name=value,NameKey=key});
            await db.SaveChangesAsync();
        }
        finally{gate.Release();}
    }
    public async Task RemovePlatformPresetAsync(long id,string? password)
    {
        await RequirePasswordAsync(password);
        await gate.WaitAsync();
        try
        {
            await using var db=Open();
            var word=await db.PlatformPresets.SingleOrDefaultAsync(p=>p.Id==id)??throw new BoardException("常用平台不存在，请刷新。");
            word.IsHidden=true;await db.SaveChangesAsync();
        }
        finally{gate.Release();}
    }
}
