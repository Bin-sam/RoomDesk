using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace RoomDesk.Core;

public sealed record SoftwareRelease(string Tag,string Version,string Notes,string Url,string DownloadUrl,string Sha256,long Size);
public sealed class SoftwareUpdates
{
    public const string CurrentTag="roomdesk-v0.8.3-preview.1";
    public const string CurrentVersion="0.8.3";
    public const string Repository="Bin-sam/RoomDesk";
    private static readonly HttpClient client=CreateClient();
    private readonly HttpClient http;
    public SoftwareUpdates(HttpClient? clientOverride=null){http=clientOverride??client;}
    private static HttpClient CreateClient(){var c=new HttpClient{Timeout=TimeSpan.FromMinutes(15)};c.DefaultRequestHeaders.UserAgent.ParseAdd("RoomDesk/0.8.3");return c;}
    public static (Version Version,int Revision)? ParseTag(string tag){
        var m=Regex.Match(tag,@"^roomdesk-v(\d+\.\d+\.\d+)(?:-preview\.(\d+))?$");
        if(!m.Success||!Version.TryParse(m.Groups[1].Value,out var version))return null;
        if(m.Groups[2].Success&&!int.TryParse(m.Groups[2].Value,out _))return null;
        return(version,m.Groups[2].Success?int.Parse(m.Groups[2].Value):int.MaxValue);
    }
    public static SoftwareRelease? SelectRelease(string json,string currentTag=CurrentTag){
        var current=ParseTag(currentTag)??throw new BoardException("当前版本号无效。");
        using var doc=JsonDocument.Parse(json);SoftwareRelease? best=null;var bestKey=current;
        foreach(var item in doc.RootElement.EnumerateArray()){
            if(item.GetProperty("draft").GetBoolean())continue;
            var tag=item.GetProperty("tag_name").GetString()??"";var key=ParseTag(tag);
            if(key==null||key.Value.Version<bestKey.Version||(key.Value.Version==bestKey.Version&&key.Value.Revision<=bestKey.Revision))continue;
            var version=key.Value.Version.ToString();var name=$"RoomDesk-Setup-{version}-win-x64.exe";
            foreach(var asset in item.GetProperty("assets").EnumerateArray()){
                if(asset.GetProperty("name").GetString()!=name||asset.GetProperty("state").GetString()!="uploaded")continue;
                var digest=asset.TryGetProperty("digest",out var d)?d.GetString()??"":"";
                var url=asset.GetProperty("browser_download_url").GetString()??"";
                var expected=$"https://github.com/{Repository}/releases/download/{tag}/{name}";
                long size=asset.GetProperty("size").GetInt64();
                if(url!=expected||!Regex.IsMatch(digest,@"^sha256:[a-fA-F0-9]{64}$")||size is <1 or >250000000)continue;
                best=new(tag,version,item.TryGetProperty("body",out var body)?body.GetString()??"":"",$"https://github.com/{Repository}/releases/tag/{tag}",url,digest[7..].ToLowerInvariant(),size);bestKey=key.Value;
            }
        }
        return best;
    }
    public async Task<SoftwareRelease?> CheckAsync(CancellationToken cancellationToken=default){
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);timeout.CancelAfter(TimeSpan.FromSeconds(25));
        using var response=await http.GetAsync($"https://api.github.com/repos/{Repository}/releases?per_page=100",HttpCompletionOption.ResponseHeadersRead,timeout.Token);
        if(!response.IsSuccessStatusCode)throw new BoardException($"无法检测更新（GitHub HTTP {(int)response.StatusCode}），请稍后重试。");
        await using var source=await response.Content.ReadAsStreamAsync(timeout.Token);using var buffer=new MemoryStream();var bytes=new byte[32768];int count;
        while((count=await source.ReadAsync(bytes,timeout.Token))>0){if(buffer.Length+count>4000000)throw new BoardException("版本列表过大，请打开 Release 页面手动更新。");buffer.Write(bytes,0,count);}
        return SelectRelease(System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
    }
    public async Task<string> DownloadAsync(SoftwareRelease release,string folder,IProgress<int>? progress=null,CancellationToken cancellationToken=default){
        var parsed=ParseTag(release.Tag)??throw new BoardException("更新版本无效。");
        var name=$"RoomDesk-Setup-{parsed.Version}-win-x64.exe";
        if(release.DownloadUrl!=$"https://github.com/{Repository}/releases/download/{release.Tag}/{name}"||!Regex.IsMatch(release.Sha256,@"^[a-fA-F0-9]{64}$")||release.Size is <1 or >250000000)throw new BoardException("更新文件信息无效。");
        Directory.CreateDirectory(folder);var target=Path.Combine(folder,name);var partial=target+".partial";
        try{
            using var response=await http.GetAsync(release.DownloadUrl,HttpCompletionOption.ResponseHeadersRead,cancellationToken);response.EnsureSuccessStatusCode();
            await using(var source=await response.Content.ReadAsStreamAsync(cancellationToken))
            await using(var destination=new FileStream(partial,FileMode.Create,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous)){
                using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var buffer=new byte[65536];long received=0;int count;
                while((count=await source.ReadAsync(buffer,cancellationToken))>0){received+=count;if(received>release.Size)throw new BoardException("更新文件大小不符。");hash.AppendData(buffer,0,count);await destination.WriteAsync(buffer.AsMemory(0,count),cancellationToken);progress?.Report((int)(received*100/release.Size));}
                if(received!=release.Size||!Convert.ToHexString(hash.GetHashAndReset()).Equals(release.Sha256,StringComparison.OrdinalIgnoreCase))throw new BoardException("更新文件校验失败，请重新下载。");
                await destination.FlushAsync(cancellationToken);destination.Flush(true);
            }
            File.Move(partial,target,true);return target;
        }finally{if(File.Exists(partial))File.Delete(partial);}
    }
}
