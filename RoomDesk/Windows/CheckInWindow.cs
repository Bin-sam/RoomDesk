using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RoomDesk.Core;

namespace RoomDesk.Windows;

public sealed class CheckInWindow : Window
{
    private bool saving;
    public CheckInWindow(BoardStore store, RoomCard room, bool readOnly = false, GuestRegistration? guest = null)
    {
        Title = readOnly ? "入住信息" : "办理入住";
        Width = 520; Height = 700; MinWidth = 420; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");
        FontSize = 14; Background = Brushes.White;
        var panel = new StackPanel { Margin = new Thickness(28) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = Title, FontSize = 25, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = $"{room.Number} 号房 · {room.Type} · {room.Floor} 楼", Margin = new Thickness(0, 15, 0, 12), Foreground = Brushes.SeaGreen });
        panel.Children.Add(new TextBlock { Text = readOnly ? (guest == null ? "此房间尚无入住登记信息。" : "当前在住客人的登记信息。") : "填写入住人信息，保存后房间将转为在住。", Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap });
        var name = new TextBox { Text = guest?.Name ?? "", MaxLength = 80 };
        var phone = new TextBox { Text = guest?.Phone ?? "", MaxLength = 40 };
        var types = new[] { "未填写", "身份证", "护照", "其他" };
        var type = new ComboBox { ItemsSource = types, SelectedIndex = 0 };
        if (!string.IsNullOrEmpty(guest?.DocumentType)) type.SelectedItem = guest.DocumentType;
        var document = new TextBox { Text = guest?.DocumentNumber ?? "", MaxLength = 50 };
        var salePrice = new TextBox { Text = readOnly ? (guest?.SalePrice?.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture) ?? "未登记") : (room.DefaultPrice?.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture) ?? "") };
        var notes = new TextBox { Text = guest?.Notes ?? "", MaxLength = 500, Height = 75, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        foreach (var field in new (string, Control)[] { ("入住人姓名 *", name), ("联系电话（选填）", phone), ("证件类型（选填）", type), ("证件号码（选填）", document), ("本次入住售出总价（元） *", salePrice), ("备注（选填）", notes) })
        {
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
                type.SelectedIndex = Array.IndexOf(types, candidate.DocumentType) is var index && index > 0 ? index : 0;
                document.Text = candidate.DocumentNumber;
                names?.Reset(); documents?.Reset();
                filled.Text = "已填入历史住客信息，请核对后保存。";
            }
            names = new GuestSuggestionPopup(this, name, store, "name", Fill);
            documents = new GuestSuggestionPopup(this, document, store, "document", Fill);
            name.ToolTip = "输入姓名匹配历史住客，上下键选择、回车填入";
            document.ToolTip = "输入证件号码匹配历史住客，上下键选择、回车填入";
            panel.Children.Add(filled);
            var reader = new Button { Content = "从读卡器填入 · 通用键盘模式", Margin = new Thickness(0,12,0,0) };
            reader.Click += (_, _) =>
            {
                if (saving) return;
                names.Reset(); documents.Reset();
                var dialog = new IdentityCardWindow { Owner = this };
                if (dialog.ShowDialog() == true && dialog.Result is { } card)
                {
                    phone.Focus(); name.Text = card.Name; document.Text = card.DocumentNumber;
                    type.SelectedItem = "身份证"; phone.Clear();
                    names.Reset(); documents.Reset();
                    filled.Text = "已填入姓名和身份证号，请核对并补充联系电话后保存。";
                }
            };
            panel.Children.Add(reader);
        }
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = readOnly ? "关闭" : "取消", IsCancel = true };
        var save = new Button { Content = "保存并入住", IsDefault = true, Background = new SolidColorBrush(Color.FromRgb(36,79,61)), Foreground = Brushes.White };
        buttons.Children.Add(cancel); if (!readOnly) buttons.Children.Add(save); panel.Children.Add(buttons);
        Closing += (_, e) => { if (saving) e.Cancel = true; };
        save.Click += async (_, _) =>
        {
            if (saving) return;
            if(!decimal.TryParse(salePrice.Text,System.Globalization.NumberStyles.AllowDecimalPoint | System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowLeadingWhite | System.Globalization.NumberStyles.AllowTrailingWhite,System.Globalization.CultureInfo.InvariantCulture,out var price)){error.Text="请填写正确的本次入住售出总价。";return;}
            var input = new GuestInput(name.Text, phone.Text, type.SelectedIndex == 0 ? "" : type.SelectedItem?.ToString(), document.Text, notes.Text, price);
            try { input = BoardStore.ValidateGuest(input); }
            catch (BoardException ex) { error.Text = ex.Message; name.Focus(); return; }
            saving = true; save.IsEnabled = false; cancel.IsEnabled = false; error.Text = "";
            try
            {
                await Task.Run(() => store.ChangeAsync(room.Id, room.Version, "CheckIn", input));
                saving = false; DialogResult = true;
            }
            catch (Exception ex) { error.Text = "未保存：" + ex.Message; }
            finally { saving = false; save.IsEnabled = true; cancel.IsEnabled = true; }
        };
        Loaded += (_, _) => { if (!readOnly) name.Focus(); };
    }
}
