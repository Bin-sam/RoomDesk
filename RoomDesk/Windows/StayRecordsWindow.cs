using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using RoomDesk.Core;

namespace RoomDesk.Windows;

public sealed class StayRecordsWindow : Window
{
    private readonly BoardStore store;
    private readonly TextBox search = new() { Width = 260, ToolTip = "搜索姓名、房号、电话、证件号码或备注", MaxLength = 100 };
    private readonly ComboBox status = new() { Width = 100, ItemsSource = new[] { "全部记录", "在住", "已退房" }, SelectedIndex = 0, Margin = new Thickness(8,0,8,0) };
    private readonly DataGrid grid = new() { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, CanUserAddRows = false, EnableRowVirtualization = true, EnableColumnVirtualization = true };
    private readonly TextBlock feedback = new() { Margin = new Thickness(0,12,0,12), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock detail = new() { Margin = new Thickness(0,12,0,0), TextWrapping = TextWrapping.Wrap };
    private readonly Button previous = new() { Content = "上一页" }, next = new() { Content = "下一页" }, export = new() { Content = "导出 CSV" }, find = new() { Content = "搜索" }, reset = new() { Content = "重置" };
    private string activeSearch = "", activeStatus = "all";
    private StayPage page = new([],0,1,30);
    private bool busy;

    public StayRecordsWindow(BoardStore boardStore)
    {
        store = boardStore; Title = "入住记录"; Width = 1100; Height = 700; MinWidth = 800; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new DockPanel { Margin = new Thickness(24) }; Content = layout;
        var heading = new TextBlock { Text = "入住记录", FontSize = 26, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,18) };
        DockPanel.SetDock(heading,Dock.Top);layout.Children.Add(heading);
        var delete = new Button { Content = "删除选中记录" };
        delete.Click += async (_,_) => { if(busy || grid.SelectedItem is not StayRecord row)return;if(row.CheckedOutAtUtc==null){feedback.Text="在住记录不能删除，请先办理退房。";return;}if(await PasswordPrompt.RunAsync(this,store,$"删除 #{row.Id} 已退房记录（将移出账单）",password=>Task.Run(()=>store.DeleteStayAsync(row.Id,password))))await LoadAsync(page.Page); };
        var bar = new WrapPanel();bar.Children.Add(delete);foreach(var item in new Control[]{search,status,find,reset,export})bar.Children.Add(item);
        DockPanel.SetDock(bar,Dock.Top);layout.Children.Add(bar);
        DockPanel.SetDock(feedback,Dock.Top);layout.Children.Add(feedback);
        var bottom = new StackPanel();var pagination = new StackPanel { Orientation = Orientation.Horizontal };
        pagination.Children.Add(previous);pagination.Children.Add(next);bottom.Children.Add(pagination);
        bottom.Children.Add(new TextBlock { Text = "CSV 导出当前筛选的全部记录；时间均为北京时间。", Margin = new Thickness(0,12,0,0) });
        bottom.Children.Add(detail);DockPanel.SetDock(bottom,Dock.Bottom);layout.Children.Add(bottom);layout.Children.Add(grid);
        foreach(var column in new[]{("房号","RoomNumber",65), ("姓名","Name",110), ("联系电话","Phone",120), ("预订平台","Platform",85), ("入住时间","CheckInText",155), ("退房时间","CheckOutText",155), ("状态","Status",70), ("证件类型","DocumentType",80), ("证件号码","DocumentNumber",170), ("售出总价（元）","SalePriceText",120), ("备注","Notes",180)})
            grid.Columns.Add(new DataGridTextColumn { Header = column.Item1, Binding = new Binding(column.Item2), Width = column.Item3 });
        grid.SelectionChanged += (_,_) => { if(grid.SelectedItem is StayRecord r)detail.Text=$"记录 #{r.Id} · {r.RoomNumber} 号房 · {r.Name}\n预订平台：{(string.IsNullOrEmpty(r.Platform)?"未登记":r.Platform)}\n证件：{r.DocumentType} {r.DocumentNumber}\n备注：{r.Notes}";else detail.Text=""; };
        find.Click += async (_,_)=> await SearchAsync();
        search.KeyDown += async (_,e)=>{if(e.Key==Key.Enter)await SearchAsync();};
        reset.Click += async (_,_)=>{search.Text="";status.SelectedIndex=0;await SearchAsync();};
        previous.Click += async (_,_)=>await LoadAsync(page.Page-1);
        next.Click += async (_,_)=>await LoadAsync(page.Page+1);
        export.Click += async (_,_)=>await ExportAsync();
        Loaded += async (_,_)=>await LoadAsync(1);
    }
    private void SetBusy(bool value)
    {
        busy=value;find.IsEnabled=reset.IsEnabled=export.IsEnabled=search.IsEnabled=status.IsEnabled=!value;
        previous.IsEnabled=!value&&page.Page>1;next.IsEnabled=!value&&page.Page*page.PageSize<page.Total;
    }
    private async Task SearchAsync()
    {
        if(busy)return;activeSearch=search.Text.Trim();activeStatus=new[]{"all","current","checkedout"}[status.SelectedIndex];await LoadAsync(1);
    }
    private async Task LoadAsync(int number)
    {
        if(busy)return;SetBusy(true);feedback.Text="正在查询…";detail.Text="";
        try { page=await Task.Run(()=>store.SearchStaysAsync(activeSearch,activeStatus,number));grid.ItemsSource=page.Records;
            feedback.Text=$"共 {page.Total} 条 · 第 {page.Page} / {Math.Max(1,(int)Math.Ceiling(page.Total/(double)page.PageSize))} 页"+(page.Total==0?" · 没有符合条件的记录。":""); }
        catch(Exception ex){feedback.Text="查询失败："+ex.Message;grid.ItemsSource=null;}
        finally{SetBusy(false);}
    }
    private async Task ExportAsync()
    {
        if(busy)return;
        var dialog=new SaveFileDialog { Filter="CSV 文件 (*.csv)|*.csv", FileName=$"入住记录-{DateTime.Now:yyyyMMdd-HHmmss}.csv", DefaultExt=".csv" };
        if(dialog.ShowDialog(this)!=true)return;
        SetBusy(true);
        try { await PasswordPrompt.RunAsync(this,store,"导出入住记录",async password=>{await BoardStore.SaveExportAsync(await Task.Run(()=>store.OpenStaysCsvAsync(activeSearch,activeStatus,password)),dialog.FileName);feedback.Text="已导出："+dialog.FileName;}); }
        catch(Exception ex){feedback.Text=ex.Message;}
        finally{SetBusy(false);}
    }
}
