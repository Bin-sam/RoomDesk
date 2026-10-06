using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RoomDesk.Core;
namespace RoomDesk.Windows;

public sealed class ReservationWindow : Window
{
    private bool saving;
    public ReservationWindow(BoardStore store, RoomCard room, bool readOnly=false, ReservationInput? reservation=null,bool edit=false)
    {
        Title=readOnly?"预订信息":edit?"编辑预订信息":"办理预订"; Width=480; Height=500; MinHeight=350;
        WindowStartupLocation=WindowStartupLocation.CenterOwner; FontFamily=new FontFamily("Microsoft YaHei UI, Segoe UI");FontSize=14;
        var panel=new StackPanel { Margin=new Thickness(28) };
        Content=new ScrollViewer { Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text=Title,FontSize=25,FontWeight=FontWeights.Bold });
        panel.Children.Add(new TextBlock { Text=$"{room.Number} 号房 · {room.Type}",Margin=new Thickness(0,16,0,10),Foreground=Brushes.DarkGoldenrod });
        var name=new TextBox { Text=reservation?.Name??"",MaxLength=80 };
        var phone=new TextBox { Text=reservation?.Phone??"",MaxLength=40 };
        var platform=new PlatformField(store,reservation?.Platform??(readOnly?"未登记":"线下"),readOnly);
        foreach(var field in new (string,Control)[]{("预订人姓名 *",name),("预订平台",platform),("联系电话（选填）",phone)})
        {
            panel.Children.Add(new TextBlock { Text=field.Item1,Margin=new Thickness(0,12,0,6) });
            field.Item2.IsEnabled=!readOnly;panel.Children.Add(field.Item2);
        }
        var error=new TextBlock { Text=readOnly&&reservation==null?"此示例预订尚未登记预订人信息。":"办理入住时会自动带入以上信息。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,16,0,12) };
        panel.Children.Add(error);
        var buttons=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right };
        var cancel=new Button { Content=readOnly?"关闭":"取消",IsCancel=true };
        var save=new Button { Content="保存预订",IsDefault=true };
        buttons.Children.Add(cancel);if(!readOnly)buttons.Children.Add(save);panel.Children.Add(buttons);
        Closing+=(_,e)=>{if(saving)e.Cancel=true;};
        save.Click+=async(_,_)=>{
            if(saving)return;
            try{
                var input=BoardStore.ValidateReservation(new(name.Text,platform.Text,phone.Text));
                saving=true;save.IsEnabled=cancel.IsEnabled=false;
                await Task.Run(()=>edit?store.EditReservationAsync(room.Id,room.Version,input):store.ChangeAsync(room.Id,room.Version,"Reserve",reservation:input));
                saving=false;DialogResult=true;
            }catch(Exception ex){error.Text=ex.Message;error.Foreground=Brushes.Firebrick;}
            finally{saving=false;save.IsEnabled=cancel.IsEnabled=true;}
        };
    }
}
