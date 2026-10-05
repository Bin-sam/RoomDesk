using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace RoomDesk.Core;

public sealed class StayRecord
{
    public long Id { get; set; }
    public int RoomNumber { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string DocumentType { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime CheckedInAtUtc { get; set; }
    public DateTime? CheckedOutAtUtc { get; set; }
    public long? SalePriceCents { get; set; }
    public decimal? SalePrice => SalePriceCents / 100m;
    public string SalePriceText => BoardStore.PriceText(SalePriceCents);
    public string Status => CheckedOutAtUtc.HasValue ? "已退房" : "在住";
    public string CheckInText => ChinaTime(CheckedInAtUtc);
    public string CheckOutText => CheckedOutAtUtc.HasValue ? ChinaTime(CheckedOutAtUtc.Value) : "—";
    public static string ChinaTime(DateTime utc) => utc.AddHours(8).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
public sealed record StayPage(IReadOnlyList<StayRecord> Records, int Total, int Page, int PageSize);

public sealed partial class BoardStore
{
    private static IQueryable<StayRecord> StayQuery(BoardDbContext db, string? search, string? status)
    {
        if (status is not (null or "" or "all" or "current" or "checkedout")) throw new BoardException("入住记录状态筛选无效。");
        var query = from g in db.GuestRegistrations.AsNoTracking().Where(g=>g.DeletedAtUtc==null)
                    join r in db.Rooms.AsNoTracking() on g.RoomId equals r.Id
                    select new StayRecord { Id=g.Id, RoomNumber=r.RoomNumber, Name=g.Name, Phone=g.Phone, DocumentType=g.DocumentType,
                        DocumentNumber=g.DocumentNumber, Notes=g.Notes, SalePriceCents=g.SalePriceCents, CheckedInAtUtc=g.CheckedInAtUtc, CheckedOutAtUtc=g.CheckedOutAtUtc };
        var term = search?.Trim().ToLowerInvariant() ?? "";
        if (term.Length > 100) throw new BoardException("搜索关键词最多 100 字。");
        if (term.Length > 0) query = query.Where(r => r.Name.ToLower().Contains(term)
            || r.RoomNumber.ToString().Contains(term) || r.Phone.ToLower().Contains(term)
            || r.DocumentNumber.ToLower().Contains(term) || r.Notes.ToLower().Contains(term));
        if (status == "current") query = query.Where(r => r.CheckedOutAtUtc == null);
        if (status == "checkedout") query = query.Where(r => r.CheckedOutAtUtc != null);
        return query.OrderByDescending(r => r.Id);
    }

    public async Task<StayPage> SearchStaysAsync(string? search = null, string? status = "all", int page = 1, int pageSize = 30)
    {
        if (page < 1 || pageSize is < 1 or > 100) throw new BoardException("分页参数无效。");
        await using var db = Open();
        await using var tx = await db.Database.BeginTransactionAsync();
        var query = StayQuery(db, search, status);
        int total = await query.CountAsync();
        page = Math.Min(page, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        await tx.CommitAsync();
        return new(rows, total, page, pageSize);
    }

    public async Task<byte[]> ExportStaysCsvAsync(string? search = null, string? status = "all", string? password = null)
    {
        await using var db = Open();
        await RequirePasswordAsync(password);
        var rows = await StayQuery(db, search, status).ToListAsync();
        var csv = new StringBuilder("记录编号,房号,入住人姓名,联系电话,证件类型,证件号码,入住时间（北京时间）,退房时间（北京时间）,状态,备注,售出总价（元）\r\n");
        foreach (var row in rows)
        {
            var values = new[] { row.Id.ToString(CultureInfo.InvariantCulture), row.RoomNumber.ToString(CultureInfo.InvariantCulture),
                row.Name, row.Phone, row.DocumentType, row.DocumentNumber, row.CheckInText,
                row.CheckedOutAtUtc.HasValue ? row.CheckOutText : "", row.Status, row.Notes, row.SalePriceText };
            csv.AppendJoin(',', values.Select(CsvCell)).Append("\r\n");
        }
        // BOM allows Excel on Windows to recognize Chinese UTF-8 headers.
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }

    private static string CsvCell(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0]) || text.StartsWith('\t') || text.StartsWith('\r') || text.StartsWith('\n'))
            text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
