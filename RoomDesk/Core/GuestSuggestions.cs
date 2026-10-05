using Microsoft.EntityFrameworkCore;

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
        // Same document identifies one candidate. Without a document, keep same-name people with different phones separate.
        var latestIds = db.GuestRegistrations.Where(g=>g.DeletedAtUtc==null).GroupBy(g => new {
            Document = g.DocumentNumber.ToUpper(),
            Type = g.DocumentNumber == "" ? "" : g.DocumentType,
            Name = g.DocumentNumber == "" ? g.Name : "",
            Phone = g.DocumentNumber == "" ? g.Phone : ""
        }).Select(group => group.Max(g => g.Id));
        var rows = db.GuestRegistrations.AsNoTracking().Where(g => latestIds.Contains(g.Id));
        var lower = term.ToLowerInvariant();
        rows = field == "name" ? rows.Where(g => g.Name.ToLower().Contains(lower))
            : rows.Where(g => g.DocumentNumber.ToLower().Contains(lower));
        return await rows.OrderByDescending(g => g.Id).Take(8)
            .Select(g => new GuestSuggestion(g.Id, g.Name, g.Phone, g.DocumentType, g.DocumentNumber, g.CheckedInAtUtc)).ToListAsync();
    }
}
