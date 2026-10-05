using Microsoft.EntityFrameworkCore;
using System.Text;

namespace RoomDesk.Core;

public sealed record GuestSuggestion(long RegistrationId, string Name, string Phone,
    string DocumentType, string DocumentNumber, DateTime LastCheckInAtUtc)
{
    public string MaskedDocument => DocumentNumber.Length > 7
        ? DocumentNumber[..3] + "••••" + DocumentNumber[^4..] : DocumentNumber;
    public string LastCheckInDate => StayRecord.ChinaTime(LastCheckInAtUtc)[..10];
    public string Display => $"{Name}  ·  {(DocumentNumber.Length == 0 ? "未登记证件" : DocumentType + " " + MaskedDocument)}  ·  {LastCheckInDate}";
}

public sealed partial class BoardStore
{
    public async Task<IReadOnlyList<GuestSuggestion>> SuggestGuestsAsync(string? query, string field = "name")
    {
        if (field is not ("name" or "document")) throw new BoardException("历史匹配字段无效。");
        var term = query?.Trim() ?? "";
        if (term.Length == 0) return [];
        if (term.Length > 100) throw new BoardException("查询内容最多 100 字。");
        await using var db = Open();
        // Indexed anti-join finds the latest identity without grouping all history in memory or SQL temp tables.
        var rows=db.GuestRegistrations.FromSqlRaw("""
            SELECT g.* FROM GuestRegistrations g INDEXED BY IX_Stays_Suggest WHERE g.DeletedAtUtc IS NULL AND NOT EXISTS(
              SELECT 1 FROM GuestRegistrations n WHERE n.DeletedAtUtc IS NULL AND n.Id>g.Id
                AND upper(n.DocumentNumber)=upper(g.DocumentNumber)
                AND CASE WHEN n.DocumentNumber='' THEN '' ELSE n.DocumentType END=CASE WHEN g.DocumentNumber='' THEN '' ELSE g.DocumentType END
                AND CASE WHEN n.DocumentNumber='' THEN n.Name ELSE '' END=CASE WHEN g.DocumentNumber='' THEN g.Name ELSE '' END
                AND CASE WHEN n.DocumentNumber='' THEN n.Phone ELSE '' END=CASE WHEN g.DocumentNumber='' THEN g.Phone ELSE '' END)
            """).AsNoTracking();
        if(term.EnumerateRunes().Count()>=3){var candidates=SearchCandidates(db,term,false).Select(g=>g.Id);rows=rows.Where(g=>candidates.Contains(g.Id));}
        var lower = term.ToLowerInvariant();
        rows = field == "name" ? rows.Where(g => g.Name.ToLower().Contains(lower))
            : rows.Where(g => g.DocumentNumber.ToLower().Contains(lower));
        return await rows.OrderByDescending(g => g.Id).Take(8)
            .Select(g => new GuestSuggestion(g.Id, g.Name, g.Phone, g.DocumentType, g.DocumentNumber, g.CheckedInAtUtc)).ToListAsync();
    }
}
