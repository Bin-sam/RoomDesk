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
    private readonly CheckBox showDeleted=new(){Content="查看已删除房间",Margin=new Thickness(0,0,0,10)};
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
        var hotelBar=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,15)};
        var hotelName=new TextBox{Width=250,MaxLength=60};var saveName=new Button{Content="保存酒店名称"};hotelBar.Children.Add(new TextBlock{Text="酒店名称  ",VerticalAlignment=VerticalAlignment.Center});hotelBar.Children.Add(hotelName);hotelBar.Children.Add(saveName);DockPanel.SetDock(hotelBar,Dock.Top);root.Children.Add(hotelBar);
        saveName.Click+=async(_,_)=>{if(saving)return;saving=true;saveName.IsEnabled=false;try{var value=hotelName.Text;await Task.Run(()=>store.SetHotelNameAsync(value));feedback.Text="酒店名称已保存";Title=value+" · 房间管理";}catch(Exception ex){feedback.Text=ex.Message;}finally{saving=false;saveName.IsEnabled=true;}};
        Loaded+=async(_,_)=>{try{hotelName.Text=await Task.Run(store.ReadHotelNameAsync);Title=hotelName.Text+" · 房间管理";}catch(Exception ex){feedback.Text=ex.Message;}};
        DockPanel.SetDock(summary,Dock.Top); root.Children.Add(summary);
        DockPanel.SetDock(feedback,Dock.Bottom); root.Children.Add(feedback);
        var layout = new Grid(); layout.ColumnDefinitions.Add(new ColumnDefinition()); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) }); root.Children.Add(layout);
        var list = new DockPanel { Margin = new Thickness(0,0,20,0) }; layout.Children.Add(list);
        var filters = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,0,0,12) }; filters.Children.Add(query); filters.Children.Add(floorFilter);
        var refresh = new Button { Content = "刷新", Margin = new Thickness(10,0,0,0) }; filters.Children.Add(refresh); DockPanel.SetDock(filters,Dock.Top); list.Children.Add(filters);
        DockPanel.SetDock(showDeleted,Dock.Top);list.Children.Add(showDeleted);showDeleted.Checked+=(_,_)=>Filter();showDeleted.Unchecked+=(_,_)=>Filter();
        DockPanel.SetDock(floorSummary,Dock.Bottom); list.Children.Add(floorSummary);
        foreach (var column in new[]{("房号","Number"),("楼层","Floor"),("房型","Type"),("当前房态","Status"),("默认价格（元）","DefaultPriceText")})
            table.Columns.Add(new DataGridTextColumn { Header = column.Item1, Binding = new Binding(column.Item2), Width = new DataGridLength(1,DataGridLengthUnitType.Star) });
        var editButton=new FrameworkElementFactory(typeof(Button));editButton.SetValue(Button.ContentProperty,"编辑");editButton.AddHandler(Button.ClickEvent,new RoutedEventHandler(async(sender,_)=>{if(saving||(sender as Button)?.DataContext is not RoomCard room||room.IsDeleted)return;if(new RoomEditWindow(store,room){Owner=this}.ShowDialog()==true){try{await LoadAsync();feedback.Text="房间资料与状态已保存";}catch(Exception ex){feedback.Text=ex.Message;}}}));
        table.Columns.Add(new DataGridTemplateColumn{Header="编辑",CellTemplate=new DataTemplate{VisualTree=editButton}});
        list.Children.Add(table);
        var form = new StackPanel { Margin = new Thickness(18), Background = Brushes.White };
        var formHost = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brushes.White }; Grid.SetColumn(formHost,1); layout.Children.Add(formHost);
        form.Children.Add(new TextBlock { Text = "新增房间", FontSize = 21, FontWeight = FontWeights.Bold });
        form.Children.Add(new TextBlock { Text = "房间总数按实际清单自动统计。添加后会同步到房态总览。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,0) });
        var deleteRoom=new Button{Content="删除选中房间 🔒 / 恢复",Margin=new Thickness(0,0,0,10)};DockPanel.SetDock(deleteRoom,Dock.Top);list.Children.Insert(0,deleteRoom);
        deleteRoom.Click+=async(_,_)=>{if(saving||table.SelectedItem is not RoomCard room){feedback.Text="请先选择房间。";return;}saving=true;try{if(room.IsDeleted){await Task.Run(()=>store.RestoreRoomAsync(room.Id,room.Version));}else{await PasswordPrompt.RunAsync(this,store,$"删除 {room.Number} 号房（历史保留，可恢复）",password=>Task.Run(()=>store.DeleteRoomAsync(room.Id,room.Version,password)));}await LoadAsync();}catch(Exception ex){feedback.Text=ex.Message;}finally{saving=false;}};
        var defaultPrice = new TextBox();
        var number = new TextBox(); var floor = new TextBox { Text = "1" };
        var type = new RoomTypeField(store,"标准双人间",false);
        var batch = new CheckBox {Content="批量新增",Margin=new Thickness(0,14,0,0)};form.Children.Add(batch);
        number.ToolTip="单间：401；批量：401-408、410。每批最多 200 间。";
        number.AcceptsReturn=true;number.MaxHeight=100;number.MaxLength=5000;
        var batchPreview=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0),Foreground=Brushes.DarkGreen};form.Children.Add(batchPreview);
        void PreviewBatch(){if(batch.IsChecked!=true){batchPreview.Text="";return;}try{batchPreview.Text=$"将新增 {BoardStore.ParseRoomNumbers(number.Text).Count} 间，同一楼层、房型及默认价格。";}catch(Exception ex){batchPreview.Text=ex.Message;}}
        number.TextChanged+=(_,_)=>PreviewBatch();batch.Checked+=(_,_)=>PreviewBatch();batch.Unchecked+=(_,_)=>PreviewBatch();
        foreach (var field in new (string,Control)[]{("房号",number),("楼层",floor),("房型",type),("默认价格（元，选填）",defaultPrice)})
        { form.Children.Add(new TextBlock { Text = field.Item1, Margin = new Thickness(0,16,0,6) }); form.Children.Add(field.Item2); }
        form.Children.Add(new TextBlock { Text = "房号示例：401-408、410（每批最多 200 间）。新房间为待清扫。已删除房号可重加，使用新资料且保留历史；当前房号冲突时整批不添加。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Sienna, Margin = new Thickness(0,16,0,0) });
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,10,0,10) }; form.Children.Add(error);
        var save = new Button { Content = "添加房间", Margin = new Thickness(0), IsEnabled = false }; form.Children.Add(save);
        query.TextChanged += (_, _) => Filter(); floorFilter.SelectionChanged += (_, _) => Filter();
        refresh.Click += async (_, _) => { if (saving) return; try { await LoadAsync(); feedback.Text = "房间清单已刷新"; save.IsEnabled = true; } catch (Exception ex) { feedback.Text = "读取失败：" + ex.Message; } };
        save.Click += async (_, _) =>
        {
            if (saving) return;
            if (!int.TryParse(floor.Text,out var newFloor)) { error.Text = "请输入有效的整数楼层。"; return; }
            IReadOnlyList<int> newNumbers;
            try{newNumbers=BoardStore.ParseRoomNumbers(number.Text);if(batch.IsChecked!=true && newNumbers.Count!=1)throw new BoardException("多个房号请勾选批量新增。");}catch(Exception ex){error.Text=ex.Message;return;}
            var numberText=number.Text;
            decimal? initialPrice=null;if(!string.IsNullOrWhiteSpace(defaultPrice.Text)){if(!decimal.TryParse(defaultPrice.Text,System.Globalization.NumberStyles.AllowDecimalPoint | System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowLeadingWhite | System.Globalization.NumberStyles.AllowTrailingWhite,System.Globalization.CultureInfo.InvariantCulture,out var parsed)){error.Text="默认价格格式不正确。";return;}initialPrice=parsed;}
            var newType = type.Text;
            saving = true; form.IsEnabled = false; refresh.IsEnabled = false; back.IsEnabled = false; error.Text = "";
            try
            {
                await Task.Run(() => store.AddRoomsAsync(numberText,newFloor,newType,initialPrice)); number.Clear(); feedback.Text = $"已添加 {newNumbers.Count} 间房间，当前为待清扫";
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
        rooms = (await Task.Run(()=>store.ReadAsync(true))).Rooms;
        var activeRooms=rooms.Where(r=>!r.IsDeleted).ToList();var floors = rooms.Select(r=>r.Floor).Distinct().ToList();
        summary.Text = $"房间总数  {rooms.Count(r=>!r.IsDeleted)} 间     ·     {activeRooms.Select(r=>r.Floor).Distinct().Count()} 个楼层     ·     {activeRooms.Select(r=>r.Type).Distinct().Count()} 种房型";
        var previous = floorFilter.SelectedItem?.ToString(); floorFilter.ItemsSource = new[]{"全部楼层"}.Concat(floors.Select(f=>$"{f} 楼")).ToList(); floorFilter.SelectedItem = previous; if(floorFilter.SelectedIndex < 0) floorFilter.SelectedIndex = 0;
        floorSummary.Text = string.Join("     ",activeRooms.GroupBy(r=>r.Floor).Select(g=>$"{g.Key} 楼：{g.Count()} 间")); Filter();
    }
    private void Filter()
    {
        var term=query.Text.Trim(); var floor=floorFilter.SelectedItem?.ToString() ?? "全部楼层";
        table.ItemsSource = rooms.Where(r=>r.IsDeleted==(showDeleted.IsChecked==true)&&(r.Number.ToString().Contains(term)||r.Type.Contains(term,StringComparison.OrdinalIgnoreCase))&&(floor=="全部楼层"||floor==$"{r.Floor} 楼")).ToList();
    }
}
