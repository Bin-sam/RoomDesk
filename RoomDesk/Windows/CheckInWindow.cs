using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RoomDesk.Core;

namespace RoomDesk.Windows;

public sealed class CheckInWindow : Window
{
    private bool saving;
    public CheckInWindow(BoardStore store, RoomCard room, bool readOnly = false, GuestRegistration? guest = null, ReservationInput? reservation = null, bool edit = false)
    {
        Title = readOnly ? "入住信息" : edit ? "编辑入住信息" : "办理入住";
        Width = 520; Height = 700; MinWidth = 420; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");
        FontSize = 14; Background = Brushes.White;
        var panel = new StackPanel { Margin = new Thickness(28) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = Title, FontSize = 25, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = $"{room.Number} 号房 · {room.Type} · {room.Floor} 楼", Margin = new Thickness(0, 15, 0, 12), Foreground = Brushes.SeaGreen });
        panel.Children.Add(new TextBlock { Text = readOnly ? (guest == null ? "此房间尚无入住登记信息。" : "当前在住客人的登记信息。") : (edit ? (guest==null ? "补登入住信息：保存时间将作为入住时间。" : "修改本次入住信息，入住时间和记录编号保持不变。") : "填写入住人信息，保存后房间将转为在住。"), Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap });
        var name = new TextBox { Text = guest?.Name ?? reservation?.Name ?? "", MaxLength = 80 };
        var phone = new TextBox { Text = guest?.Phone ?? reservation?.Phone ?? "", MaxLength = 40 };
        var types = new[] { "未填写", "身份证", "护照", "其他" };
        var type = new ComboBox { ItemsSource = types, SelectedIndex = readOnly ? 0 : 1 };
        if (!string.IsNullOrEmpty(guest?.DocumentType)) type.SelectedItem = guest.DocumentType;
        var platform = new PlatformField(store, (readOnly || edit) ? (string.IsNullOrEmpty(guest?.Platform) ? "线下" : guest.Platform) : reservation?.Platform ?? "线下", readOnly);
        var document = new TextBox { Text = guest?.DocumentNumber ?? "", MaxLength = 50 };
        var salePrice = new TextBox { Text = (readOnly || edit) ? (guest?.SalePrice?.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture) ?? "") : (room.DefaultPrice?.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture) ?? "") };
        var notes = new TextBox { Text = guest?.Notes ?? "", MaxLength = 500, Height = 75, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        foreach (var field in new (string, Control)[] { ("入住人姓名 *", name), ("联系电话（选填）", phone), ("预订平台", platform), ("证件类型（选填）", type), ("证件号码（选填）", document), ("本次入住售出总价（元） *", salePrice), ("备注（选填）", notes) })
        {
            if (field.Item2 == salePrice) panel.Children.Add(new TextBlock { Text = $"房间默认价：{room.DefaultPriceText}" + (room.DefaultPrice.HasValue ? " 元" : ""), FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Brushes.SeaGreen, Margin = new Thickness(0,16,0,0) });
            panel.Children.Add(new TextBlock { Text = field.Item1, Margin = new Thickness(0, 13, 0, 6), FontSize = 12 });
            field.Item2.IsEnabled = !readOnly; panel.Children.Add(field.Item2);
        }
        if (!readOnly)
        {
            var filled = new TextBlock { Foreground = Brushes.SeaGreen, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,10,0,0) };
            GuestSuggestionPopup? names = null, documents = null;
            void Fill(GuestSuggestion candidate)
            {
                if (saving) return;
                phone.Focus();
                name.Text = candidate.Name; phone.Text = candidate.Phone;
                type.SelectedIndex = Array.IndexOf(types, candidate.DocumentType) is var index && index > 0 ? index : 1;
                document.Text = candidate.DocumentNumber;
                names?.Reset(); documents?.Reset();
                filled.Text = "已填入历史住客信息，请核对后保存。";
            }
            names = new GuestSuggestionPopup(this, name, store, "name", Fill);
            documents = new GuestSuggestionPopup(this, document, store, "document", Fill);
            name.ToolTip = "输入姓名匹配历史住客，上下键选择、回车填入";
            document.ToolTip = "输入证件号码匹配历史住客，上下键选择、回车填入";
            panel.Children.Add(filled);

        }
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = readOnly ? "关闭" : "取消", IsCancel = true };
        var save = new Button { Content = edit ? "保存修改" : "保存并入住", IsDefault = true, Background = new SolidColorBrush(Color.FromRgb(36,79,61)), Foreground = Brushes.White };
        buttons.Children.Add(cancel); if (!readOnly) buttons.Children.Add(save); panel.Children.Add(buttons);
        Closing += (_, e) => { if (saving) e.Cancel = true; };
        save.Click += async (_, _) =>
        {
            if (saving) return;
            if(!decimal.TryParse(salePrice.Text,System.Globalization.NumberStyles.AllowDecimalPoint | System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowLeadingWhite | System.Globalization.NumberStyles.AllowTrailingWhite,System.Globalization.CultureInfo.InvariantCulture,out var price)){error.Text="请填写正确的本次入住售出总价。";return;}
            var input = new GuestInput(name.Text, phone.Text, type.SelectedIndex == 0 ? "" : type.SelectedItem?.ToString(), document.Text, notes.Text, price, platform.Text);
            try { input = BoardStore.ValidateGuest(input); }
            catch (BoardException ex) { error.Text = ex.Message; name.Focus(); return; }
            saving = true; save.IsEnabled = false; cancel.IsEnabled = false; error.Text = "";
            try
            {
                await Task.Run(() => edit ? store.EditGuestAsync(room.Id,room.Version,input) : store.ChangeAsync(room.Id, room.Version, "CheckIn", input));
                saving = false; DialogResult = true;
            }
            catch (Exception ex) { error.Text = "未保存：" + ex.Message; }
            finally { saving = false; save.IsEnabled = true; cancel.IsEnabled = true; }
        };
        Loaded += (_, _) => { if (!readOnly) name.Focus(); };
    }
}
