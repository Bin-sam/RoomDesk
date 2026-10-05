using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RoomDesk.Core;
namespace RoomDesk.Windows;

public sealed class PlatformField : UserControl
{
    private readonly BoardStore store;
    private readonly TextBox input=new(){MaxLength=40};
    private readonly WrapPanel chips=new();
    private readonly TextBlock status=new(){FontSize=11,Foreground=Brushes.Gray,TextWrapping=TextWrapping.Wrap};
    public string Text { get=>input.Text;set=>input.Text=value; }
    public PlatformField(BoardStore store,string value,bool readOnly)
    {
        this.store=store;input.Text=value;
        var panel=new StackPanel();Content=panel;panel.Children.Add(input);
        if(readOnly)return;
        input.ToolTip="自由输入平台名称，或点选常用气泡";
        panel.Children.Add(new ScrollViewer{Content=chips,MaxHeight=100,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var manage=new Button{Content="管理常用词",HorizontalAlignment=HorizontalAlignment.Left,FontSize=11,Margin=new Thickness(0,5,0,0)};
        manage.Click+=async(_,_)=>{new PlatformManagerWindow(store){Owner=Window.GetWindow(this)}.ShowDialog();await RefreshAsync();};
        panel.Children.Add(manage);panel.Children.Add(status);
        Loaded+=async(_,_)=>await RefreshAsync();
    }
    private async Task RefreshAsync()
    {
        try{
            var words=await Task.Run(store.ReadPlatformPresetsAsync);chips.Children.Clear();status.Text=words.Count==0?"暂无常用词，仍可自由输入。":"";
            foreach(var word in words){
                var button=new Button{Content=word.Name,FontSize=12,Padding=new Thickness(12,4,12,4),Margin=new Thickness(0,7,6,0),Background=new SolidColorBrush(Color.FromRgb(237,245,239))};
                button.Template=ChipTemplate();
                button.Click+=(_,_)=>{input.Text=word.Name;input.Focus();input.CaretIndex=input.Text.Length;};
                chips.Children.Add(button);
            }
        }catch(Exception ex){status.Text="常用词读取失败，仍可手动输入："+ex.Message;}
    }
    private static ControlTemplate ChipTemplate()
    {
        var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(16));
        border.SetValue(Border.BackgroundProperty,new SolidColorBrush(Color.FromRgb(237,245,239)));
        border.SetValue(Border.PaddingProperty,new Thickness(12,5,12,5));border.SetValue(Border.BorderBrushProperty,Brushes.LightGray);border.SetValue(Border.BorderThicknessProperty,new Thickness(1));
        var content=new FrameworkElementFactory(typeof(ContentPresenter));border.AppendChild(content);
        return new ControlTemplate(typeof(Button)){VisualTree=border};
    }
}
public sealed class PlatformManagerWindow : Window
{
    private readonly BoardStore store;
    private readonly TextBox name=new(){MaxLength=40};
    private readonly WrapPanel words=new();
    private readonly TextBlock message=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,10)};
    private readonly Button add=new(){Content="添加"};
    private bool busy;
    public PlatformManagerWindow(BoardStore store)
    {
        this.store=store;Title="管理常用平台";Width=520;Height=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel{Margin=new Thickness(24)};Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(new TextBlock{Text=Title,FontSize=24,FontWeight=FontWeights.Bold});
        panel.Children.Add(new TextBlock{Text="预订与入住共用。删除气泡需操作密码，不影响历史记录。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)});
        panel.Children.Add(name);panel.Children.Add(add);panel.Children.Add(message);panel.Children.Add(words);
        var close=new Button{Content="完成",IsCancel=true,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,20,0,0)};panel.Children.Add(close);
        add.Click+=async(_,_)=>await AddAsync();
        name.KeyDown+=async(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){e.Handled=true;await AddAsync();}};
        Loaded+=async(_,_)=>await RefreshAsync();
        Closing+=(_,e)=>{if(busy)e.Cancel=true;};
    }
    private async Task AddAsync()
    {
        if(busy)return;busy=true;add.IsEnabled=false;
        try{var value=name.Text;await Task.Run(()=>store.AddPlatformPresetAsync(value));name.Clear();await RefreshAsync();message.Text="已添加，预订与入住均可使用。";}
        catch(Exception ex){message.Text=ex.Message;}
        finally{busy=false;add.IsEnabled=true;}
    }
    private async Task RefreshAsync()
    {
        try{
            var list=await Task.Run(store.ReadPlatformPresetsAsync);words.Children.Clear();
            foreach(var word in list){
                var line=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,6,8,0)};
                line.Children.Add(new TextBlock{Text=word.Name,VerticalAlignment=VerticalAlignment.Center,MaxWidth=260,TextWrapping=TextWrapping.Wrap});
                var remove=new Button{Content="删除 🔒",FontSize=11};line.Children.Add(remove);
                remove.Click+=async(_,_)=>{
                    if(busy)return;busy=true;
                    try{if(await PasswordPrompt.RunAsync(this,store,"删除常用平台“"+word.Name+"”",password=>Task.Run(()=>store.RemovePlatformPresetAsync(word.Id,password)))){await RefreshAsync();message.Text="气泡已删除，历史记录保持不变。";}}
                    catch(Exception ex){message.Text=ex.Message;}
                    finally{busy=false;}
                };
                words.Children.Add(new Border{Child=line,CornerRadius=new CornerRadius(15),Background=new SolidColorBrush(Color.FromRgb(237,245,239)),Padding=new Thickness(10,3,5,3),Margin=new Thickness(0,4,6,0)});
            }
        }catch(Exception ex){message.Text=ex.Message;}
    }
}
