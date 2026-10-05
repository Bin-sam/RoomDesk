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
    private void Board_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var columns = e.NewSize.Width >= 870 ? 8 : 4;
        if (columns != RoomColumns) { RoomColumns = columns; Notify(); }
    }
    private bool loaded;
    public BoardWindow(BoardStore boardStore)
    {
        store = boardStore;
        InitializeComponent(); DataContext = this;
        Loaded += async (_, _) => await RunAsync(async () => {
            await Task.Run(() => store.InitializeAsync()); loaded = true; await ReloadAsync();
        }, "已载入本地示例房态 · 选择房间进行操作");
    }
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    private async Task ReloadAsync()
    {
        snapshot = await Task.Run(store.ReadAsync);
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
            if (new CheckInWindow(store, room) { Owner = this }.ShowDialog() == true)
                await RunAsync(ReloadAsync, $"{room.Number} 入住信息已保存，房间已转为在住");
            return;
        }
        if (key is "CheckOut" or "Disable" && MessageBox.Show(this, $"确认对 {room.Number} 执行“{room.Actions.First(a => a.Key == key).Label}”？", "确认房态变更", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        await RunAsync(async () => { await Task.Run(() => store.ChangeAsync(room.Id, room.Version, key)); await ReloadAsync(); }, $"{room.Number} 房态已保存到本机");
    }
    private async void Guest_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { Occupancy: "Occupied" } room) { Feedback = "请选择在住房间查看入住信息。"; Notify(); return; }
        await RunAsync(async () => {
            var guest = await Task.Run(() => store.GetCurrentGuestAsync(room.Id));
            new CheckInWindow(store, room, true, guest) { Owner = this }.ShowDialog();
        }, "已读取入住信息");
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
