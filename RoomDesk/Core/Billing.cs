using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
namespace RoomDesk.Core;
public sealed record BillSummary(IReadOnlyList<StayRecord> Records,int Count,int UnpricedCount,long TotalCents)
{
    public string TotalText => BoardStore.PriceText(TotalCents);
}
public sealed partial class BoardStore
{
    private static (DateTime Start,DateTime EndExclusive) BillRange(string start,string end)
    {
        if(!DateTime.TryParseExact(start,"yyyy-MM-ddTHH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var a)
            || !DateTime.TryParseExact(end,"yyyy-MM-ddTHH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var b)
            || a.Year<1900 || b.Year>2100 || a>b)throw new BoardException("请选择有效的起止时间（精确到分钟），结束时间不能早于开始时间。");
        return (DateTime.SpecifyKind(a.AddHours(-8),DateTimeKind.Utc),DateTime.SpecifyKind(b.AddMinutes(1).AddHours(-8),DateTimeKind.Utc));
    }
    private async Task<List<StayRecord>> BillRowsAsync(string start,string end)
    {
        var range=BillRange(start,end);await using var db=Open();
        return await StayQuery(db,null,"all").Where(g=>g.CheckedInAtUtc>=range.Start && g.CheckedInAtUtc<range.EndExclusive).OrderBy(g=>g.CheckedInAtUtc).ThenBy(g=>g.Id).ToListAsync();
    }
    public async Task<BillSummary> ReadBillAsync(string start,string end)
    {
        var rows=await BillRowsAsync(start,end);
        return new(rows.Take(100).ToList(),rows.Count,rows.Count(r=>!r.SalePriceCents.HasValue),rows.Sum(r=>r.SalePriceCents??0));
    }
    public async Task<byte[]> ExportBillAsync(string start,string end,string? password)
    {
        await RequirePasswordAsync(password);var rows=await BillRowsAsync(start,end);
        var csv=new StringBuilder("记录编号,房号,入住人姓名,入住时间（北京时间）,退房时间（北京时间）,状态,售出总价（元）,金额登记状态,筛选开始（含）,筛选结束分钟（含）\r\n");
        foreach(var row in rows)
            csv.AppendJoin(',',new[]{row.Id.ToString(CultureInfo.InvariantCulture),row.RoomNumber.ToString(CultureInfo.InvariantCulture),row.Name,row.CheckInText,row.CheckedOutAtUtc.HasValue?row.CheckOutText:"",row.Status,row.SalePriceCents.HasValue?row.SalePriceText:"",row.SalePriceCents.HasValue?"已登记":"未登记",start.Replace('T',' '),end.Replace('T',' ')}.Select(CsvCell)).Append("\r\n");
        csv.AppendJoin(',',new[]{"合计","",$"{rows.Count} 笔","","","",PriceText(rows.Sum(r=>r.SalePriceCents??0)),$"{rows.Count(r=>r.SalePriceCents==null)} 笔未登记价格",start.Replace('T',' '),end.Replace('T',' ')}.Select(CsvCell)).Append("\r\n");
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }
}
