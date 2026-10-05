using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RoomDesk.Core;
namespace RoomDesk.Windows;
public static class PasswordPrompt
{
    public static async Task<bool> RunAsync(Window owner,BoardStore store,string title,Func<string,Task> action)
    {
        if(!await Task.Run(store.HasPasswordAsync)){MessageBox.Show(owner,"请先在首页的“数据与安全”中设置操作密码。","尚未设置密码");return false;}
        var panel=new StackPanel{Margin=new Thickness(24)};
        var dialog=new Window{Owner=owner,Title=title,Width=460,SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=panel};
        panel.Children.Add(new TextBlock{Text=title,FontSize=20,TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(new TextBlock{Text="请输入操作密码，验证通过后继续。",Margin=new Thickness(0,15,0,8)});
        var password=new PasswordBox{MaxLength=128,Padding=new Thickness(10)};panel.Children.Add(password);
        var error=new TextBlock{Foreground=Brushes.Firebrick,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)};panel.Children.Add(error);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal};var cancel=new Button{Content="取消",IsCancel=true};var submit=new Button{Content="验证并继续",IsDefault=true};buttons.Children.Add(cancel);buttons.Children.Add(submit);panel.Children.Add(buttons);bool busy=false;
        dialog.Closing+=(_,e)=>{if(busy)e.Cancel=true;};
        submit.Click+=async(_,_)=>{if(busy)return;busy=true;submit.IsEnabled=cancel.IsEnabled=false;error.Text="";try{await action(password.Password);busy=false;password.Clear();dialog.DialogResult=true;}catch(Exception ex){error.Text=ex.Message;password.Clear();password.Focus();}finally{busy=false;submit.IsEnabled=cancel.IsEnabled=true;}};
        dialog.Loaded+=(_,_)=>password.Focus();dialog.Closed+=(_,_)=>password.Clear();return dialog.ShowDialog()==true;
    }
}
