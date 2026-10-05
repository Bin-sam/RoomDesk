using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace RoomDesk.Core;

public sealed class StayRecord
{
    public long Id { get; set; }
    public int RoomNumber { get; set; }
    public string Name { get; set; } = "";
    public string Platform { get; set; } = "";
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
        var term = search?.Trim().ToLowerInvariant() ?? "";
        if (term.Length > 100 || term.Any(char.IsControl)) throw new BoardException("搜索关键词最多 100 字，不能包含控制字符。");
        var guests=SearchCandidates(db,term);
        if(term.EnumerateRunes().Count()>=3){
            var roomIds=db.Rooms.Where(r=>r.RoomNumber.ToString().Contains(term)).Select(r=>r.Id);
            guests=guests.Where(g=>g.Name.ToLower().Contains(term)||g.Phone.ToLower().Contains(term)||g.Platform.ToLower().Contains(term)||g.DocumentNumber.ToLower().Contains(term)||g.Notes.ToLower().Contains(term)||roomIds.Contains(g.RoomId));
        }
        if(status=="current")guests=guests.Where(g=>g.CheckedOutAtUtc==null);
        if(status=="checkedout")guests=guests.Where(g=>g.CheckedOutAtUtc!=null);
        // Correlated room lookup runs only for the selected page; a join could sort the entire history before LIMIT.
        return guests.OrderByDescending(g=>g.Id).Select(g=>new StayRecord{
            Id=g.Id,RoomNumber=db.Rooms.Where(r=>r.Id==g.RoomId).Select(r=>r.RoomNumber).First(),
            Name=g.Name,Platform=g.Platform,Phone=g.Phone,DocumentType=g.DocumentType,DocumentNumber=g.DocumentNumber,
            Notes=g.Notes,SalePriceCents=g.SalePriceCents,CheckedInAtUtc=g.CheckedInAtUtc,CheckedOutAtUtc=g.CheckedOutAtUtc});
    }

    public async Task<StayPage> SearchStaysAsync(string? search = null, string? status = "all", int page = 1, int pageSize = 30)
    {
        if (page < 1 || pageSize is < 1 or > 100) throw new BoardException("分页参数无效。");
        await using var db = Open();
        await using var tx = ((Microsoft.Data.Sqlite.SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred:true);
        await db.Database.UseTransactionAsync(tx);
        var query = StayQuery(db, search, status);
        var unfiltered=string.IsNullOrWhiteSpace(search);
        var guests=db.GuestRegistrations.FromSqlRaw("SELECT * FROM GuestRegistrations INDEXED BY IX_Stays_Page WHERE DeletedAtUtc IS NULL").AsNoTracking();
        if(status=="current")guests=guests.Where(g=>g.CheckedOutAtUtc==null);
        if(status=="checkedout")guests=guests.Where(g=>g.CheckedOutAtUtc!=null);
        int total = unfiltered?await guests.CountAsync():await query.CountAsync();
        page = Math.Min(page, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        var ids = unfiltered?await guests.OrderByDescending(g=>g.Id).Select(g=>g.Id).Skip((page-1)*pageSize).Take(pageSize).ToListAsync()
            :await query.Select(g=>g.Id).Skip((page-1)*pageSize).Take(pageSize).ToListAsync();
        var rows = await query.Where(g=>ids.Contains(g.Id)).ToListAsync();
        await tx.CommitAsync();
        return new(rows, total, page, pageSize);
    }

    // Compatibility for small callers/tests. Production UI uses the disk-backed stream below.
    public async Task<byte[]> ExportStaysCsvAsync(string? search=null,string? status="all",string? password=null)
    {
        await using var file=await OpenStaysCsvAsync(search,status,password);using var buffer=new MemoryStream();await file.CopyToAsync(buffer);return buffer.ToArray();
    }
    public Task<FileStream> OpenStaysCsvAsync(string? search=null,string? status="all",string? password=null)
        => CreateCsvAsync(password,async writer=>{
            await using var db=Open();
            await writer.WriteLineAsync("记录编号,房号,入住人姓名,联系电话,证件类型,证件号码,入住时间（北京时间）,退房时间（北京时间）,状态,备注,售出总价（元）,预订平台");
            await foreach(var row in StayQuery(db,search,status).AsAsyncEnumerable())
                await WriteCsvLine(writer,new[]{row.Id.ToString(CultureInfo.InvariantCulture),row.RoomNumber.ToString(CultureInfo.InvariantCulture),row.Name,row.Phone,row.DocumentType,row.DocumentNumber,row.CheckInText,row.CheckedOutAtUtc.HasValue?row.CheckOutText:"",row.Status,row.Notes,row.SalePriceText,row.Platform});
        });

    private static string CsvCell(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0]) || text.StartsWith('\t') || text.StartsWith('\r') || text.StartsWith('\n'))
            text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
