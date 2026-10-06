using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using RoomDesk.Core;
namespace RoomDesk.Windows;
public sealed class UpdateWindow:Window
{
    private static void PruneDownloads(){
        var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RoomDeskPrototype","updates");if(!Directory.Exists(root))return;
        foreach(var folder in new DirectoryInfo(root).GetDirectories().Where(d=>Guid.TryParseExact(d.Name,"N",out _)).OrderByDescending(d=>d.CreationTimeUtc).Skip(3)){
            if(File.Exists(Path.Combine(folder.FullName,"ready"))&&!File.Exists(Path.Combine(folder.FullName,"result.json")))continue;
            try{folder.Delete(true);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    public UpdateWindow(BoardStore store){
        _=Task.Run(()=>{try{PruneDownloads();}catch{}});
        Title="软件更新";Width=560;Height=580;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel{Margin=new Thickness(26)};Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(new TextBlock{Text="软件更新",FontSize=25,FontWeight=FontWeights.Bold});
        panel.Children.Add(new TextBlock{Text="当前版本："+SoftwareUpdates.CurrentTag,Margin=new Thickness(0,15,0,15)});
        var info=new TextBlock{Text="检测 GitHub 上已发布且可校验的安装包。更新前会备份数据库，请先完成正在办理的入住或退房。",TextWrapping=TextWrapping.Wrap};panel.Children.Add(info);
        var notes=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Height=210,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,12,0,12)};panel.Children.Add(notes);
        var progress=new ProgressBar{Height=8,Minimum=0,Maximum=100};panel.Children.Add(progress);
        var check=new Button{Content="检查更新",Margin=new Thickness(0,16,0,10)};var download=new Button{Content="下载新版本",IsEnabled=false};var install=new Button{Content="备份并安装更新",IsEnabled=false,Margin=new Thickness(0,10,0,10)};
        panel.Children.Add(check);panel.Children.Add(download);panel.Children.Add(install);
        var cancel=new Button{Content="取消下载",IsEnabled=false};panel.Children.Add(cancel);
        var manual=new Button{Content="打开 GitHub 下载页",Margin=new Thickness(0,10,0,0)};panel.Children.Add(manual);manual.Click+=(_,_)=>{try{Process.Start(new ProcessStartInfo("https://github.com/"+SoftwareUpdates.Repository+"/releases"){UseShellExecute=true});}catch(Exception ex){info.Text=ex.Message;}};
        var service=new SoftwareUpdates();SoftwareRelease? release=null;string? installer=null;string? folder=null;bool busy=false;CancellationTokenSource? cancellation=null;
        Closing+=(_,e)=>{if(busy){e.Cancel=true;info.Text="正在处理，请等待或点击取消下载。";}};cancel.Click+=(_,_)=>cancellation?.Cancel();
        check.Click+=async(_,_)=>{busy=true;check.IsEnabled=download.IsEnabled=install.IsEnabled=false;info.Text="正在检测新版本…";try{release=await service.CheckAsync();installer=null;info.Text=release==null?"当前已是最新可用版本。":"发现新版本："+release.Tag;notes.Text=release?.Notes??"";download.IsEnabled=release!=null;}catch(Exception ex){info.Text="检查失败："+ex.Message;}finally{busy=false;check.IsEnabled=true;}};
        download.Click+=async(_,_)=>{if(release==null)return;busy=true;check.IsEnabled=download.IsEnabled=install.IsEnabled=false;cancel.IsEnabled=true;cancellation=new();try{
            folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RoomDeskPrototype","updates",Guid.NewGuid().ToString("N"));
            installer=await service.DownloadAsync(release,folder,new Progress<int>(p=>{progress.Value=p;info.Text=$"正在下载：{p}%";}),cancellation.Token);info.Text="下载完成，SHA-256 校验通过。点击安装后将关闭并重启程序。";install.IsEnabled=true;
        }catch(OperationCanceledException){info.Text="已取消下载，当前版本可继续使用。";}catch(Exception ex){info.Text="下载失败："+ex.Message;}finally{busy=false;check.IsEnabled=true;download.IsEnabled=true;cancel.IsEnabled=false;cancellation.Dispose();cancellation=null;}};
        install.Click+=async(_,_)=>{
            if(installer==null||release==null||folder==null)return;
            if(MessageBox.Show(this,"确认已完成当前业务？程序将先备份，然后退出安装并自动重启。","安装更新",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
            busy=true;install.IsEnabled=check.IsEnabled=download.IsEnabled=false;
            bool readyToExit=false;
            try{await PasswordPrompt.RunAsync(this,store,"授权更新并备份酒店数据",async credential=>{
                info.Text="正在备份数据库…";var backup=await Task.Run(()=>store.BackupAsync(credential));
                var application=Environment.ProcessPath??throw new BoardException("无法确定安装位置。");
                var script=Path.Combine(folder,"update.ps1");using(var resource=typeof(UpdateWindow).Assembly.GetManifestResourceStream("RoomDesk.Windows.Resources.update.ps1")??throw new BoardException("缺少更新助手。"))using(var file=File.Create(script))await resource.CopyToAsync(file);
                File.Delete(Path.Combine(folder,"ready"));File.Delete(Path.Combine(folder,"result.json"));
                var plan=Path.Combine(folder,"plan.json");await File.WriteAllTextAsync(plan,JsonSerializer.Serialize(new{ProcessId=Environment.ProcessId,Application=application,Database=store.DatabasePath,Installer=installer,InstallDirectory=Path.GetDirectoryName(application),Backup=backup,Sha256=release.Sha256,Version=release.Tag}));
                var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe")){UseShellExecute=false,CreateNoWindow=true};
                foreach(var arg in new[]{"-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File",script,"-PlanPath",plan})start.ArgumentList.Add(arg);
                using var helper=Process.Start(start)??throw new BoardException("无法启动更新助手。");
                var ready=Path.Combine(folder,"ready");var deadline=DateTime.UtcNow.AddSeconds(20);while(!File.Exists(ready)){if(helper.HasExited||DateTime.UtcNow>=deadline){if(!helper.HasExited)helper.Kill();throw new BoardException("更新助手未就绪，当前程序未退出。请检查更新目录日志。");}await Task.Delay(150);}
                readyToExit=true;
            });if(readyToExit){busy=false;Application.Current.Shutdown();}}catch(Exception ex){info.Text=ex.Message;}finally{busy=false;install.IsEnabled=check.IsEnabled=download.IsEnabled=true;}
        };
    }
}
