using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RoomDesk.Core;
namespace RoomDesk.Windows;
public sealed class RoomEditWindow:Window
{
    public RoomEditWindow(BoardStore store,RoomCard room){
        Title=$"编辑 {room.Number} 号房";Width=500;Height=620;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel{Margin=new Thickness(26)};Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(new TextBlock{Text=Title,FontSize=25,FontWeight=FontWeights.Bold});
        var floor=new TextBox{Text=room.Floor.ToString()};var type=new RoomTypeField(store,room.Type,false);var price=new TextBox{Text=room.DefaultPrice?.ToString("0.00",CultureInfo.InvariantCulture)??""};
        var options=room.Occupancy=="Vacant"?new[]{new ActionOption("ready","可入住"),new ActionOption("dirty","待清扫"),new ActionOption("maintenance","维修中"),new ActionOption("disabled","停用")}:new[]{new ActionOption(room.StatusKey,room.Status)};
        var status=new ComboBox{ItemsSource=options,DisplayMemberPath="Label",SelectedValuePath="Key",SelectedValue=room.StatusKey};
        foreach(var field in new(string,Control)[]{("楼层",floor),("房型",type),("默认价格（元，留空表示未设置）",price),("房间状态",status)}){panel.Children.Add(new TextBlock{Text=field.Item1,Margin=new Thickness(0,16,0,6)});panel.Children.Add(field.Item2);}
        var clean=new CheckBox{Content="已清洁",Visibility=room.Occupancy=="Vacant"?Visibility.Collapsed:Visibility.Visible,IsChecked=room.IsClean,IsEnabled=room.Occupancy is "Occupied" or "Reserved",Margin=new Thickness(0,14,0,8)};panel.Children.Add(clean);
        panel.Children.Add(new TextBlock{Text="默认价只影响后续入住。入住、退房及取消预订请在房态页办理；不会通过编辑资料清除客人信息。",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray});
        var error=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Firebrick,Margin=new Thickness(0,10,0,10)};panel.Children.Add(error);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal};var save=new Button{Content="保存修改",IsDefault=true};var cancel=new Button{Content="取消",IsCancel=true};buttons.Children.Add(save);buttons.Children.Add(cancel);panel.Children.Add(buttons);bool saving=false;
        Closing+=(_,e)=>{if(saving)e.Cancel=true;};
        save.Click+=async(_,_)=>{if(saving)return;try{
            if(!int.TryParse(floor.Text,out var f))throw new BoardException("请输入整数楼层。");
            decimal? p=null;if(!string.IsNullOrWhiteSpace(price.Text)){if(!decimal.TryParse(price.Text,NumberStyles.Number & ~NumberStyles.AllowThousands,CultureInfo.InvariantCulture,out var value))throw new BoardException("请输入有效价格。");p=value;}
            var input=new RoomEdit(f,type.Text,p,status.SelectedValue?.ToString()??"",room.Occupancy is "Occupied" or "Reserved"?clean.IsChecked==true:room.IsClean);
            saving=true;save.IsEnabled=cancel.IsEnabled=false;await Task.Run(()=>store.EditRoomAsync(room.Id,room.Version,input));saving=false;DialogResult=true;
        }catch(Exception ex){error.Text=ex.Message;}finally{saving=false;save.IsEnabled=cancel.IsEnabled=true;}};
    }
}
