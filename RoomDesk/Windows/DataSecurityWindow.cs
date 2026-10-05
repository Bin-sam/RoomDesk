using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RoomDesk.Core;
namespace RoomDesk.Windows;
public sealed class DataSecurityWindow:Window
{
    public DataSecurityWindow(BoardStore store)
    {
        Title="数据与安全";Width=540;Height=660;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel{Margin=new Thickness(26)};Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(new TextBlock{Text="操作密码",FontSize=25,FontWeight=FontWeights.Bold});
        var state=new TextBlock{Margin=new Thickness(0,14,0,14),TextWrapping=TextWrapping.Wrap};panel.Children.Add(state);
        var current=new PasswordBox{MaxLength=128,Padding=new Thickness(10)};var next=new PasswordBox{MaxLength=128,Padding=new Thickness(10)};var confirm=new PasswordBox{MaxLength=128,Padding=new Thickness(10)};
        foreach(var field in new[]{("当前密码（首次设置可留空）",current),("新密码（8–128 位）",next),("确认新密码",confirm)}){panel.Children.Add(new TextBlock{Text=field.Item1,Margin=new Thickness(0,12,0,6)});panel.Children.Add(field.Item2);}
        var error=new TextBlock{Foreground=Brushes.Firebrick,Margin=new Thickness(0,12,0,12),TextWrapping=TextWrapping.Wrap};panel.Children.Add(error);
        var save=new Button{Content="保存密码",IsEnabled=false};panel.Children.Add(save);
        panel.Children.Add(new TextBlock{Text="每次导出、备份和删除已退房记录都需要验证密码。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,20,0,12)});
        var backup=new Button{Content="验证密码并备份"};panel.Children.Add(backup);var message=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)};panel.Children.Add(message);bool busy=false;
        async Task Read(){var configured=await Task.Run(store.HasPasswordAsync);current.IsEnabled=configured;state.Text=configured?"已设置操作密码，修改时请填写当前密码。":"尚未设置，请先创建操作密码。";save.IsEnabled=true;}
        Loaded+=async(_,_)=>{try{await Read();}catch(Exception ex){error.Text=ex.Message;}};
        save.Click+=async(_,_)=>{if(busy)return;if(next.Password!=confirm.Password){error.Text="两次新密码不一致。";return;}var replacement=next.Password;var old=current.Password;busy=true;save.IsEnabled=false;error.Text="";try{await Task.Run(()=>store.SetPasswordAsync(replacement,old));current.Clear();next.Clear();confirm.Clear();await Read();state.Text="操作密码已保存。";}catch(Exception ex){error.Text=ex.Message;}finally{busy=false;save.IsEnabled=true;}};
        backup.Click+=async(_,_)=>{await PasswordPrompt.RunAsync(this,store,"备份本机数据",async password=>{message.Text=await Task.Run(()=>store.BackupAsync(password));});};
        Closing+=(_,e)=>{if(busy)e.Cancel=true;};Closed+=(_,_)=>{current.Clear();next.Clear();confirm.Clear();};
    }
}
