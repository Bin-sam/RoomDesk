using System.Text;
namespace RoomDesk.Core;
public sealed partial class BoardStore
{
    private readonly SemaphoreSlim exportGate=new(1,1);
    private static Task WriteCsvLine(StreamWriter writer,IEnumerable<string> values)=>writer.WriteLineAsync(string.Join(',',values.Select(CsvCell)));
    private async Task<FileStream> CreateCsvAsync(string? password,Func<StreamWriter,Task> write)
    {
        await RequirePasswordAsync(password);
        if(!await exportGate.WaitAsync(0))throw new BoardException("已有导出正在生成，请完成后再导出。");
        FileStream? file=null;
        try{
            var folder=Path.Combine(Path.GetDirectoryName(DatabasePath)!,"export-cache");Directory.CreateDirectory(folder);
            file=new FileStream(Path.Combine(folder,Guid.NewGuid()+".tmp"),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,65536,FileOptions.Asynchronous|FileOptions.DeleteOnClose);
            using(var writer=new StreamWriter(file,new UTF8Encoding(true),65536,leaveOpen:true){NewLine="\r\n"}){await write(writer);await writer.FlushAsync();}
            file.Position=0;return file;
        }catch{if(file!=null)await file.DisposeAsync();throw;}
        finally{exportGate.Release();}
    }
    public static async Task SaveExportAsync(FileStream export,string destination)
    {
        await using(export){var temp=destination+"."+Guid.NewGuid()+".tmp";try{await using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true)){await export.CopyToAsync(output);output.Flush(true);}File.Move(temp,destination,true);}finally{if(File.Exists(temp))File.Delete(temp);}}
    }
}
