using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using RoomDesk.Core;

namespace RoomDesk.Windows;

public sealed class RoomManagementWindow : Window
{
    private readonly BoardStore store;
    private IReadOnlyList<RoomCard> rooms = [];
    private readonly TextBlock summary = new() { FontSize = 21, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,16) };
    private readonly TextBlock feedback = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,14,0,0) };
    private readonly TextBlock floorSummary = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,10,0,0) };
    private readonly TextBox query = new() { Width = 180, ToolTip = "搜索房号或房型" };
    private readonly ComboBox floorFilter = new() { Width = 115, Margin = new Thickness(10,0,0,0) };
    private readonly DataGrid table = new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, RowHeight = 40, FontSize = 14 };
    private bool saving;

    public RoomManagementWindow(BoardStore store)
    {
        this.store = store;
        Title = "房间管理 · 栖间"; Width = 1000; Height = 740; MinWidth = 850; MinHeight = 550;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = new SolidColorBrush(Color.FromRgb(243,246,245));
        FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");
        var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
        var heading = new DockPanel { Margin = new Thickness(0,0,0,18) };
        var back = new Button { Content = "返回房态总览", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(back, Dock.Right); heading.Children.Add(back);
        heading.Children.Add(new TextBlock { Text = "房间管理", FontSize = 27, FontWeight = FontWeights.Bold }); DockPanel.SetDock(heading,Dock.Top); root.Children.Add(heading);
        DockPanel.SetDock(summary,Dock.Top); root.Children.Add(summary);
        DockPanel.SetDock(feedback,Dock.Bottom); root.Children.Add(feedback);
        var layout = new Grid(); layout.ColumnDefinitions.Add(new ColumnDefinition()); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) }); root.Children.Add(layout);
        var list = new DockPanel { Margin = new Thickness(0,0,20,0) }; layout.Children.Add(list);
        var filters = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,0,0,12) }; filters.Children.Add(query); filters.Children.Add(floorFilter);
        var refresh = new Button { Content = "刷新", Margin = new Thickness(10,0,0,0) }; filters.Children.Add(refresh); DockPanel.SetDock(filters,Dock.Top); list.Children.Add(filters);
        DockPanel.SetDock(floorSummary,Dock.Bottom); list.Children.Add(floorSummary);
        foreach (var column in new[]{("房号","Number"),("楼层","Floor"),("房型","Type"),("当前房态","Status"),("默认价格（元）","DefaultPriceText")})
            table.Columns.Add(new DataGridTextColumn { Header = column.Item1, Binding = new Binding(column.Item2), Width = new DataGridLength(1,DataGridLengthUnitType.Star) });
        list.Children.Add(table);
        var form = new StackPanel { Margin = new Thickness(18), Background = Brushes.White };
        var formHost = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brushes.White }; Grid.SetColumn(formHost,1); layout.Children.Add(formHost);
        form.Children.Add(new TextBlock { Text = "新增房间", FontSize = 21, FontWeight = FontWeights.Bold });
        form.Children.Add(new TextBlock { Text = "房间总数按实际清单自动统计。添加后会同步到房态总览。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,0) });
        var editPrice = new Button { Content = "设置选中房间默认价", Margin = new Thickness(0,0,0,10) }; DockPanel.SetDock(editPrice,Dock.Top); list.Children.Insert(0,editPrice);
        editPrice.Click += async (_,_) =>
        {
            if(saving || table.SelectedItem is not RoomCard room){feedback.Text="请先选择一个房间。";return;}
            var box=new TextBox{Text=room.DefaultPrice?.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)??""};var stack=new StackPanel{Margin=new Thickness(24)};stack.Children.Add(new TextBlock{Text=$"{room.Number} 号房默认价格（元）",Margin=new Thickness(0,0,0,12)});stack.Children.Add(box);var ok=new Button{Content="保存",Margin=new Thickness(0,14,0,0),IsDefault=true};stack.Children.Add(ok);var err=new TextBlock{TextWrapping=TextWrapping.Wrap};stack.Children.Add(err);
            var dialog=new Window{Title="设置默认价格",Owner=this,Width=350,SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=stack};bool updating=false;
            ok.Click+=async(_,_)=>{if(updating)return;if(!decimal.TryParse(box.Text,System.Globalization.NumberStyles.AllowDecimalPoint | System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowLeadingWhite | System.Globalization.NumberStyles.AllowTrailingWhite,System.Globalization.CultureInfo.InvariantCulture,out var price)){err.Text="价格格式不正确。";return;}updating=true;ok.IsEnabled=false;try{await Task.Run(()=>store.SetRoomPriceAsync(room.Id,room.Version,price));updating=false;dialog.DialogResult=true;}catch(Exception ex){err.Text=ex.Message;}finally{updating=false;ok.IsEnabled=true;}};dialog.Closing+=(_,e)=>{if(updating)e.Cancel=true;};
            if(dialog.ShowDialog()==true){try{await LoadAsync();feedback.Text="默认价格已更新，历史售出价保持原值。";}catch(Exception ex){feedback.Text=ex.Message;}}
        };
        var defaultPrice = new TextBox();
        var number = new TextBox(); var floor = new TextBox { Text = "1" };
        var type = new ComboBox { ItemsSource = new[]{"标准间","豪华房","行政套房","贵宾套房","顶层套房"}, SelectedIndex = 0 };
        foreach (var field in new (string,Control)[]{("房号",number),("楼层",floor),("房型",type),("默认价格（元，选填）",defaultPrice)})
        { form.Children.Add(new TextBlock { Text = field.Item1, Margin = new Thickness(0,16,0,6) }); form.Children.Add(field.Item2); }
        form.Children.Add(new TextBlock { Text = "新房间初始为待清扫，清扫完成后可办理入住。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Sienna, Margin = new Thickness(0,16,0,0) });
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,10,0,10) }; form.Children.Add(error);
        var save = new Button { Content = "添加房间", Margin = new Thickness(0), IsEnabled = false }; form.Children.Add(save);
        query.TextChanged += (_, _) => Filter(); floorFilter.SelectionChanged += (_, _) => Filter();
        refresh.Click += async (_, _) => { if (saving) return; try { await LoadAsync(); feedback.Text = "房间清单已刷新"; save.IsEnabled = true; } catch (Exception ex) { feedback.Text = "读取失败：" + ex.Message; } };
        save.Click += async (_, _) =>
        {
            if (saving) return;
            if (!int.TryParse(number.Text,out var newNumber) || !int.TryParse(floor.Text,out var newFloor)) { error.Text = "请输入有效的整数房号和楼层。"; return; }
            decimal? initialPrice=null;if(!string.IsNullOrWhiteSpace(defaultPrice.Text)){if(!decimal.TryParse(defaultPrice.Text,System.Globalization.NumberStyles.AllowDecimalPoint | System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowLeadingWhite | System.Globalization.NumberStyles.AllowTrailingWhite,System.Globalization.CultureInfo.InvariantCulture,out var parsed)){error.Text="默认价格格式不正确。";return;}initialPrice=parsed;}
            var types = new[]{"Standard","Deluxe","ExecutiveSuite","VipSuite","Penthouse"}; var newType = types[type.SelectedIndex];
            saving = true; form.IsEnabled = false; refresh.IsEnabled = false; back.IsEnabled = false; error.Text = "";
            try
            {
                await Task.Run(() => store.AddRoomAsync(newNumber,newFloor,newType,initialPrice)); number.Clear(); feedback.Text = $"{newNumber} 号房已添加，当前为待清扫";
                try { await LoadAsync(); } catch { feedback.Text = "房间已添加，清单刷新失败，请点击刷新。"; }
            }
            catch (Exception ex) { error.Text = ex.Message; }
            finally { saving = false; form.IsEnabled = true; refresh.IsEnabled = true; back.IsEnabled = true; }
        };
        Closing += (_,e) => { if (saving) e.Cancel = true; };
        Loaded += async (_, _) => { try { await LoadAsync(); save.IsEnabled = true; feedback.Text = "已载入本机房间清单"; } catch (Exception ex) { feedback.Text = "读取失败：" + ex.Message; } };
    }
    private async Task LoadAsync()
    {
        rooms = (await Task.Run(store.ReadAsync)).Rooms;
        var floors = rooms.Select(r=>r.Floor).Distinct().ToList();
        summary.Text = $"房间总数  {rooms.Count} 间     ·     {floors.Count} 个楼层     ·     {rooms.Select(r=>r.Type).Distinct().Count()} 种房型";
        var previous = floorFilter.SelectedItem?.ToString(); floorFilter.ItemsSource = new[]{"全部楼层"}.Concat(floors.Select(f=>$"{f} 楼")).ToList(); floorFilter.SelectedItem = previous; if(floorFilter.SelectedIndex < 0) floorFilter.SelectedIndex = 0;
        floorSummary.Text = string.Join("     ",rooms.GroupBy(r=>r.Floor).Select(g=>$"{g.Key} 楼：{g.Count()} 间")); Filter();
    }
    private void Filter()
    {
        var term=query.Text.Trim(); var floor=floorFilter.SelectedItem?.ToString() ?? "全部楼层";
        table.ItemsSource = rooms.Where(r=>(r.Number.ToString().Contains(term)||r.Type.Contains(term,StringComparison.OrdinalIgnoreCase))&&(floor=="全部楼层"||floor==$"{r.Floor} 楼")).ToList();
    }
}
