using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
namespace RoomDesk.Core;
public sealed record BillSummary(IReadOnlyList<StayRecord> Records,int Count,int UnpricedCount,long TotalCents)
{
    public string TotalText => BoardStore.PriceText(TotalCents);
}
public sealed class BillTotals {public int Count{get;set;} public int Missing{get;set;} public long Total{get;set;}}
public sealed partial class BoardStore
{
    private static (DateTime Start,DateTime EndExclusive) BillRange(string start,string end)
    {
        if(!DateTime.TryParseExact(start,"yyyy-MM-ddTHH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var a)
            || !DateTime.TryParseExact(end,"yyyy-MM-ddTHH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var b)
            || a.Year<1900 || b.Year>2100 || a>b)throw new BoardException("请选择有效的起止时间（精确到分钟），结束时间不能早于开始时间。");
        return (DateTime.SpecifyKind(a.AddHours(-8),DateTimeKind.Utc),DateTime.SpecifyKind(b.AddMinutes(1).AddHours(-8),DateTimeKind.Utc));
    }
    private static IQueryable<StayRecord> BillQuery(BoardDbContext db,string start,string end)
    {
        var range=BillRange(start,end);
        return StayQuery(db,null,"all").Where(g=>g.CheckedInAtUtc>=range.Start && g.CheckedInAtUtc<range.EndExclusive).OrderBy(g=>g.CheckedInAtUtc).ThenBy(g=>g.Id);
    }
    public async Task<BillSummary> ReadBillAsync(string start,string end)
    {
        await using var db=Open();
        await using var tx=((Microsoft.Data.Sqlite.SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred:true);await db.Database.UseTransactionAsync(tx);
        var query=BillQuery(db,start,end);
        var range=BillRange(start,end);
        var totals=await db.Database.SqlQuery<BillTotals>($"SELECT count(*) AS Count,coalesce(sum(CASE WHEN SalePriceCents IS NULL THEN 1 ELSE 0 END),0) AS Missing,coalesce(sum(SalePriceCents),0) AS Total FROM GuestRegistrations WHERE DeletedAtUtc IS NULL AND CheckedInAtUtc>={range.Start} AND CheckedInAtUtc<{range.EndExclusive}").SingleAsync();
        var rows=await query.Take(100).ToListAsync();await tx.CommitAsync();
        return new(rows,totals?.Count??0,totals?.Missing??0,totals?.Total??0);
    }
    public async Task<byte[]> ExportBillAsync(string start,string end,string? password)
    {
        await using var file=await OpenBillCsvAsync(start,end,password);using var buffer=new MemoryStream();await file.CopyToAsync(buffer);return buffer.ToArray();
    }
    public Task<FileStream> OpenBillCsvAsync(string start,string end,string? password)
        => CreateCsvAsync(password,async writer=>{
            await using var db=Open();int count=0,missing=0;long total=0;
            await writer.WriteLineAsync("记录编号,房号,入住人姓名,入住时间（北京时间）,退房时间（北京时间）,状态,售出总价（元）,金额登记状态,筛选开始（含）,筛选结束分钟（含）,预订平台");
            await foreach(var row in BillQuery(db,start,end).AsAsyncEnumerable()){
                count++;if(row.SalePriceCents==null)missing++;total+=row.SalePriceCents??0;
                await WriteCsvLine(writer,new[]{row.Id.ToString(CultureInfo.InvariantCulture),row.RoomNumber.ToString(CultureInfo.InvariantCulture),row.Name,row.CheckInText,row.CheckedOutAtUtc.HasValue?row.CheckOutText:"",row.Status,row.SalePriceCents.HasValue?row.SalePriceText:"",row.SalePriceCents.HasValue?"已登记":"未登记",start.Replace('T',' '),end.Replace('T',' '),row.Platform});
            }
            await WriteCsvLine(writer,new[]{"合计","",$"{count} 笔","","","",PriceText(total),$"{missing} 笔未登记价格",start.Replace('T',' '),end.Replace('T',' '),""});
        });
}
