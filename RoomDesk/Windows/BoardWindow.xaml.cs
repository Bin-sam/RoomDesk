using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using RoomDesk.Core;

namespace RoomDesk.Windows;
public sealed record FloorGroup(string Title, IReadOnlyList<RoomCard> Rooms);
public partial class BoardWindow : Window, INotifyPropertyChanged
{
    private readonly BoardStore store;
    private BoardSnapshot snapshot = new([], []);
    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<FloorGroup> Floors { get; private set; } = [];
    public RoomCard? Selected { get; private set; }
    public string Feedback { get; private set; } = "正在加载本地房态…";
    public string ResultText { get; private set; } = "";
    public int ReadyCount => snapshot.Ready;
    public int OccupiedCount => snapshot.Occupied;
    public int ReservedCount => snapshot.Reserved;
    public int DirtyCount => snapshot.Dirty;
    public int UnavailableCount => snapshot.Unavailable;
    public bool CanOperate { get; private set; } = true;
    public int RoomColumns { get; private set; } = 8;
    public double RoomCardHeight { get; private set; } = 120;
    private double roomScale = .8;
    public double RoomNumberSize => Math.Max(17,30*roomScale);
    public double RoomTypeSize => Math.Max(11,20*roomScale);
    public double RoomCaptionSize => Math.Max(11,16*roomScale);
    public double RoomStatusSize => Math.Max(12,18*roomScale);
    public double RoomCleanSize => Math.Max(10,14*roomScale);
    public double RoomTypeLineHeight => RoomTypeSize*1.15;
    public double RoomTypeHeight => RoomTypeLineHeight*2;
    private void Board_SizeChanged(object sender, SizeChangedEventArgs e) => FitBoard();
    private void FitBoard()
    {
        if(BoardArea is null || Floors.Count==0 || BoardArea.ActualWidth<1 || BoardArea.ActualHeight<1)return;
        var width=BoardArea.ActualWidth-46;var height=BoardArea.ActualHeight-8;
        var bestScale=double.MinValue;var bestColumns=1;var bestHeight=120d;
        var minColumns=Math.Max(1,(int)Math.Ceiling(width/200));
        var limit=Math.Max(minColumns,Math.Min(Floors.Max(f=>f.Rooms.Count),Math.Max(1,(int)(width/54))));
        for(var columns=minColumns;columns<=limit;columns++){
            var rows=Floors.Sum(f=>(int)Math.Ceiling(f.Rooms.Count/(double)columns));
            var cardWidth=width/columns-7;
            var cardHeight=Math.Min(180,Math.Floor((height-Floors.Count*12)/rows));
            var scale=Math.Min(1.15,Math.Min((cardWidth-14)/100,(cardHeight-16)/126));
            if(scale>bestScale){bestScale=scale;bestColumns=columns;bestHeight=cardHeight;}
        }
        // Large inventories keep legible text and retain the virtualized scrolling board.
        RoomColumns=bestColumns;RoomCardHeight=Math.Max(88,bestHeight);roomScale=Math.Max(.55,bestScale);Notify();
    }
    private readonly CancellationTokenSource lifetime=new();
    private bool loaded;
    public string HotelName { get;private set; }="栖间";
    public BoardWindow(BoardStore boardStore)
    {
        store = boardStore;
        InitializeComponent(); DataContext = this;
        Closed+=(_,_)=>lifetime.Cancel();
        Loaded += async (_, _) => await RunAsync(async () => {
            await Task.Run(() => store.InitializeAsync()); _=Task.Run(()=>store.RunMaintenanceLoopAsync(lifetime.Token)); loaded = true; await ReloadAsync();
        }, "已载入本地示例房态 · 选择房间进行操作");
    }
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    private async Task ReloadAsync()
    {
        snapshot = await Task.Run(store.ReadAsync);
        HotelName=await Task.Run(store.ReadHotelNameAsync);Title=HotelName+" · 房态管理";
        int selectedId = Selected?.Id ?? snapshot.Rooms.FirstOrDefault()?.Id ?? 0;
        Selected = snapshot.Rooms.FirstOrDefault(r => r.Id == selectedId);
        var previous = FloorBox.SelectedItem?.ToString() ?? "全部楼层";
        FloorBox.ItemsSource = new[] { "全部楼层" }.Concat(snapshot.Rooms.Select(r => $"{r.Floor} 楼").Distinct()).ToList();
        FloorBox.SelectedItem = previous;
        if (FloorBox.SelectedIndex < 0) FloorBox.SelectedIndex = 0;
        ApplyFilter(); Notify();
    }
    private void Filter_Changed(object sender, RoutedEventArgs e) { if (loaded) ApplyFilter(); }
    private void ApplyFilter()
    {
        var status = (StatusBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
        var floor = FloorBox.SelectedItem?.ToString() ?? "全部楼层";
        var rooms = snapshot.Rooms.Where(r => r.Number.ToString().Contains(SearchBox.Text.Trim())
            && (floor == "全部楼层" || floor == $"{r.Floor} 楼")
            && (status == "all" || (status == "dirty" ? !r.IsClean : r.StatusKey == status))).ToList();
        Floors = rooms.GroupBy(r => r.Floor).Select(g => new FloorGroup($"{g.Key:00}", g.ToList())).ToList();
        FitBoard();
        ResultText = rooms.Count == 0 ? "没有符合条件的房间，请调整筛选。" : $"显示 {rooms.Count} / {snapshot.Rooms.Count} 间 · 点击房间查看详情";
        Notify();
    }
    private void Room_Click(object sender, RoutedEventArgs e) { if (!CanOperate) return; Selected = (sender as Button)?.Tag as RoomCard; Notify(); }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RunAsync(ReloadAsync, "房态已刷新");
    private async void Action_Click(object sender, RoutedEventArgs e)
    {
        var room = Selected; var key = (sender as Button)?.Tag?.ToString();
        if (room is null || key is null) return;
        if (key == "CheckIn")
        {
            await RunAsync(async () => {
                var reservation = await Task.Run(() => store.GetReservationAsync(room.Id));
                if (new CheckInWindow(store, room, reservation: reservation) { Owner = this }.ShowDialog() == true) await ReloadAsync();
            }, "已同步房态");
            return;
        }
        if (key == "Reserve")
        {
            if (new ReservationWindow(store, room) { Owner = this }.ShowDialog() == true)
                await RunAsync(ReloadAsync, $"{room.Number} 预订信息已保存");
            return;
        }
        if (key is "CheckOut" or "Disable" && MessageBox.Show(this, $"确认对 {room.Number} 执行“{room.Actions.First(a => a.Key == key).Label}”？", "确认房态变更", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        await RunAsync(async () => { await Task.Run(() => store.ChangeAsync(room.Id, room.Version, key)); await ReloadAsync(); }, $"{room.Number} 房态已保存到本机");
    }
    private async void Guest_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { Occupancy: "Reserved" } reserved)
        {
            await RunAsync(async () => {
                var reservation = await Task.Run(() => store.GetReservationAsync(reserved.Id));
                new ReservationWindow(store, reserved, true, reservation) { Owner = this }.ShowDialog();
            }, "已读取预订信息");
            return;
        }
        if (Selected is not { Occupancy: "Occupied" } room) { Feedback = "请选择在住房间查看入住信息。"; Notify(); return; }
        await RunAsync(async () => {
            var guest = await Task.Run(() => store.GetCurrentGuestAsync(room.Id));
            new CheckInWindow(store, room, true, guest) { Owner = this }.ShowDialog();
        }, "已读取入住信息");
    }
    private async void EditRoom_Click(object sender,RoutedEventArgs e){if(!CanOperate||Selected is not {} room)return;if(new RoomEditWindow(store,room){Owner=this}.ShowDialog()==true)await RunAsync(ReloadAsync,"房间修改已保存");}
    private async void EditGuest_Click(object sender,RoutedEventArgs e){
        if(!CanOperate||Selected is not {} room)return;
        await RunAsync(async()=>{
            bool saved=false;
            if(room.Occupancy=="Occupied"){var guest=await Task.Run(()=>store.GetCurrentGuestAsync(room.Id));saved=new CheckInWindow(store,room,guest:guest,edit:true){Owner=this}.ShowDialog()==true;}
            else if(room.Occupancy=="Reserved"){var booking=await Task.Run(()=>store.GetReservationAsync(room.Id));saved=new ReservationWindow(store,room,reservation:booking,edit:true){Owner=this}.ShowDialog()==true;}
            else {MessageBox.Show(this,"请先选择在住或已预订房间。");return;}
            if(saved)await ReloadAsync();
        },"已同步房态");
    }
    private void Security_Click(object sender,RoutedEventArgs e)=>new DataSecurityWindow(store){Owner=this}.ShowDialog();
    private void Bills_Click(object sender,RoutedEventArgs e)=>new BillingWindow(store){Owner=this}.ShowDialog();
    private void History_Click(object sender, RoutedEventArgs e)
    {
        var text = snapshot.Activities.Count == 0 ? "尚无操作记录。" : string.Join("\n\n", snapshot.Activities.Select(a => $"{a.AtUtc.ToLocalTime():MM-dd HH:mm:ss}   {a.RoomNumber}   {a.Action}\n{a.Before} → {a.After}"));
        new Window { Owner = this, Title = "最近 100 条操作记录", Width = 650, Height = 550, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(20) } }.ShowDialog();
    }
    private void StayRecords_Click(object sender, RoutedEventArgs e) => new StayRecordsWindow(store) { Owner = this }.ShowDialog();
    private async void Rooms_Click(object sender, RoutedEventArgs e)
    {
        if (!CanOperate) return;
        new RoomManagementWindow(store) { Owner = this }.ShowDialog();
        await RunAsync(ReloadAsync, "已同步房间清单");
    }
    private async Task RunAsync(Func<Task> work, string success)
    {
        if (!CanOperate) return;
        CanOperate = false; Feedback = "正在处理…"; Notify();
        try { await work(); Feedback = success; }
        catch (Exception ex) { Feedback = "操作未完成：" + ex.Message; }
        finally { CanOperate = true; Notify(); }
    }
}
