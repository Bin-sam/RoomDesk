using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using RoomDesk.Core;
namespace RoomDesk.Windows;
public sealed class BillingWindow:Window
{
    public BillingWindow(BoardStore store)
    {
        Title="入住账单";Width=1120;Height=720;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var layout=new DockPanel{Margin=new Thickness(24)};Content=layout;
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);layout.Children.Add(top);
        top.Children.Add(new TextBlock{Text="入住账单",FontSize=26,FontWeight=FontWeights.Bold});top.Children.Add(new TextBlock{Text="按入住时间筛选 · 北京时间 · 售出总价不代表已收款金额",Margin=new Thickness(0,10,0,12)});
        var today=DateTime.UtcNow.AddHours(8).Date;var startDate=new DatePicker{SelectedDate=today,Width=140};var endDate=new DatePicker{SelectedDate=today,Width=140};var startTime=new TextBox{Text="00:00",Width=75};var endTime=new TextBox{Text="23:59",Width=75};
        var bar=new WrapPanel();foreach(var item in new FrameworkElement[]{new TextBlock{Text="开始（含）",VerticalAlignment=VerticalAlignment.Center},startDate,startTime,new TextBlock{Text=" 结束（含该分钟）",VerticalAlignment=VerticalAlignment.Center},endDate,endTime})bar.Children.Add(item);
        var find=new Button{Content="查询账单",Margin=new Thickness(10,0,8,0)};var export=new Button{Content="导出 CSV",IsEnabled=false};bar.Children.Add(find);bar.Children.Add(export);top.Children.Add(bar);
        var summary=new TextBlock{FontSize=20,FontWeight=FontWeights.Bold,Margin=new Thickness(0,20,0,12)};top.Children.Add(summary);var feedback=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};top.Children.Add(feedback);
        var note=new TextBlock{Text="结束时间包含该分钟的全部秒。预览最多 100 笔，导出包含全部记录与合计；未登记价格单独计数。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(note,Dock.Bottom);layout.Children.Add(note);
        var grid=new DataGrid{AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false};foreach(var c in new[]{("房号","RoomNumber"),("入住人","Name"),("入住时间","CheckInText"),("状态","Status"),("售出总价（元）","SalePriceText")})grid.Columns.Add(new DataGridTextColumn{Header=c.Item1,Binding=new Binding(c.Item2),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});layout.Children.Add(grid);
        (string Start,string End)? active=null;int generation=0;
        void Invalidate(){generation++;active=null;export.IsEnabled=false;feedback.Text="时间已修改，请重新查询后导出。";}
        startDate.SelectedDateChanged+=(_,_)=>Invalidate();endDate.SelectedDateChanged+=(_,_)=>Invalidate();startTime.TextChanged+=(_,_)=>Invalidate();endTime.TextChanged+=(_,_)=>Invalidate();
        async Task Load()
        {
            int id=++generation;active=null;export.IsEnabled=false;
            try
            {
                if(startDate.SelectedDate==null||endDate.SelectedDate==null)throw new BoardException("请选择起止日期。");
                var a=startDate.SelectedDate.Value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)+"T"+startTime.Text.Trim();var b=endDate.SelectedDate.Value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)+"T"+endTime.Text.Trim();
                var result=await Task.Run(()=>store.ReadBillAsync(a,b));if(id!=generation)return;active=(a,b);grid.ItemsSource=result.Records;summary.Text=$"{result.Count} 笔入住   ·   已登记售出总额 ¥{result.TotalText}   ·   {result.UnpricedCount} 笔未登记价格";feedback.Text=$"{a.Replace('T',' ')} 至 {b.Replace('T',' ')}（含结束分钟）";export.IsEnabled=true;
            }
            catch(Exception ex){if(id==generation){feedback.Text=ex.Message;summary.Text="";grid.ItemsSource=null;}}
        }
        find.Click+=async(_,_)=>await Load();Loaded+=async(_,_)=>await Load();
        export.Click+=async(_,_)=>{if(active is not {} range)return;var file=new SaveFileDialog{Filter="CSV 文件 (*.csv)|*.csv",FileName="入住账单.csv"};if(file.ShowDialog(this)!=true)return;await PasswordPrompt.RunAsync(this,store,"导出该时间段入住账单",async password=>{var bytes=await Task.Run(()=>store.ExportBillAsync(range.Start,range.End,password));await System.IO.File.WriteAllBytesAsync(file.FileName,bytes);feedback.Text="账单已导出："+file.FileName;});};
    }
}
