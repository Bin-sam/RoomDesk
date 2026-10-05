using System.Windows;
using RoomDesk.Core;

namespace RoomDesk.Windows;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var index = Array.IndexOf(e.Args, "--data");
            var path = index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : BoardStore.DefaultDatabasePath;
            new BoardWindow(new BoardStore(path)).Show();
        }
        catch (Exception ex) { MessageBox.Show("无法启动房态原型：" + ex.Message); Shutdown(1); }
    }
}
