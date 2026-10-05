using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RoomDesk.Core;

namespace RoomDesk.Windows;

public sealed class IdentityCardWindow : Window
{
    public IdentityCardData? Result { get; private set; }
    public IdentityCardWindow()
    {
        Title = "接收身份证信息"; Width = 540; Height = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "通用键盘输入模式", FontSize = 22 });
        panel.Children.Add(new TextBlock { Text = "仅适用于能模拟键盘输入文本的设备，尚未验证具体读卡器。\n点击下方输入框后刷卡，或粘贴设备软件导出的文本。\n格式：姓名 + Tab / 换行 + 18 位身份证号。\n也支持“姓名：… / 身份证号：…”两行文本。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,14,0,14) });
        var input = new TextBox { Height = 115, AcceptsReturn = true, AcceptsTab = true, MaxLength = 4096, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(input);
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,12) }; panel.Children.Add(error);
        var fill = new Button { Content = "识别并填入（Ctrl+Enter）" }; panel.Children.Add(fill);
        void Parse() { try { Result = IdentityCardInput.Parse(input.Text); input.Clear(); DialogResult = true; } catch (BoardException ex) { error.Text = ex.Message; input.Focus(); } }
        fill.Click += (_, _) => Parse();
        input.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { e.Handled = true; Parse(); } };
        panel.Children.Add(new TextBlock { Text = "填入后请核对，再保存入住。此功能不核验身份证真伪。", Foreground = Brushes.Gray, Margin = new Thickness(0,12,0,0), TextWrapping = TextWrapping.Wrap });
        Loaded += (_, _) => input.Focus();
        Closed += (_, _) => input.Clear();
    }
}
