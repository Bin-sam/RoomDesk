using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RoomDesk.Core;
namespace RoomDesk.Windows;
public static class PasswordPrompt
{
    private static readonly Dictionary<string,OperationSession> sessions=new();
    public static async Task LockAsync(BoardStore store){if(sessions.Remove(store.DatabasePath,out var session))await store.LockSessionAsync(session.Token);}

    public static async Task<bool> RunAsync(Window owner,BoardStore store,string title,Func<string,Task> action)
    {
        if(sessions.TryGetValue(store.DatabasePath,out var remembered)&&await store.SessionValidAsync(remembered.Token)){
            if(title.Contains("删除")&&MessageBox.Show(owner,title+"？","确认删除（5 分钟免密有效）",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return false;
            try{await action(remembered.Token);return true;}catch(Exception ex){MessageBox.Show(owner,ex.Message,"操作未完成");return false;}
        }
        sessions.Remove(store.DatabasePath);
        if(!await Task.Run(store.HasPasswordAsync)){MessageBox.Show(owner,"请先在首页的“数据与安全”中设置操作密码。","尚未设置密码");return false;}
        var panel=new StackPanel{Margin=new Thickness(24)};
        var dialog=new Window{Owner=owner,Title=title,Width=460,SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=panel};
        panel.Children.Add(new TextBlock{Text=title,FontSize=20,TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(new TextBlock{Text="请输入操作密码，验证通过后继续。",Margin=new Thickness(0,15,0,8)});
        var password=new PasswordBox{MaxLength=128,Padding=new Thickness(10)};panel.Children.Add(password);
        var remember=new CheckBox{Content="验证后 5 分钟内免重复输入（关闭窗口仍有效）",IsChecked=true,Margin=new Thickness(0,12,0,0)};panel.Children.Add(remember);
        var error=new TextBlock{Foreground=Brushes.Firebrick,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)};panel.Children.Add(error);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal};var cancel=new Button{Content="取消",IsCancel=true};var submit=new Button{Content="验证并继续",IsDefault=true};buttons.Children.Add(cancel);buttons.Children.Add(submit);panel.Children.Add(buttons);bool busy=false;
        dialog.Closing+=(_,e)=>{if(busy)e.Cancel=true;};
        submit.Click+=async(_,_)=>{if(busy)return;busy=true;submit.IsEnabled=cancel.IsEnabled=false;error.Text="";try{var value=password.Password;var session=await Task.Run(()=>store.UnlockAsync(value));password.Clear();if(remember.IsChecked==true)sessions[store.DatabasePath]=session;try{await action(session.Token);}finally{if(remember.IsChecked!=true)await store.LockSessionAsync(session.Token);}busy=false;password.Clear();dialog.DialogResult=true;}catch(Exception ex){error.Text=ex.Message;password.Clear();password.Focus();}finally{busy=false;submit.IsEnabled=cancel.IsEnabled=true;}};
        dialog.Loaded+=(_,_)=>password.Focus();dialog.Closed+=(_,_)=>password.Clear();return dialog.ShowDialog()==true;
    }
}
