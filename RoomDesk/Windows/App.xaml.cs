using System.Windows;
using RoomDesk.Core;

namespace RoomDesk.Windows;
public partial class App : Application
{
    private Mutex? instance;
    protected override void OnExit(ExitEventArgs e){instance?.ReleaseMutex();instance?.Dispose();base.OnExit(e);}
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var index = Array.IndexOf(e.Args, "--data");
            var path = index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : BoardStore.DefaultDatabasePath;
            var key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(path).ToUpperInvariant())));
            var mutex=new Mutex(true,"Local\\RoomDesk-"+key,out var created);
            if(!created){mutex.Dispose();MessageBox.Show("此酒店数据库已在另一个窗口打开，请使用已有窗口。");Shutdown();return;}instance=mutex;
            new BoardWindow(new BoardStore(path)).Show();
        }
        catch (Exception ex) { MessageBox.Show("无法启动房态原型：" + ex.Message); Shutdown(1); }
    }
}
