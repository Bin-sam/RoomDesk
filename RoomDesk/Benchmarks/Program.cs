using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using RoomDesk.Core;

var path=Path.GetFullPath(args[0]);var store=new BoardStore(path);
await store.InitializeAsync(300);
if(args.Contains("--prepare")){
    var count=int.Parse(args[Array.IndexOf(args,"--prepare")+1]);
    using var db=new SqliteConnection("Data Source="+path);db.Open();using var command=db.CreateCommand();command.CommandTimeout=300;
    command.CommandText=$"""
        PRAGMA cache_size=-2048; PRAGMA synchronous=FULL; PRAGMA temp_store=FILE;
        WITH RECURSIVE seq(i) AS (SELECT 1 UNION ALL SELECT i+1 FROM seq WHERE i<{count})
        INSERT INTO GuestRegistrations(RoomId,Name,Phone,DocumentType,DocumentNumber,Notes,Platform,CheckedInAtUtc,CheckedOutAtUtc,SalePriceCents)
        SELECT 1+(i%300),'旅客'||(i%10000),'138'||printf('%08d',i%10000),'其他','TEST'||printf('%014d',i%10000),'十年模拟记录 '||i,CASE WHEN i%2=0 THEN '线下' ELSE '携程' END,datetime('2016-01-01','+'||(i%3650)||' days'),datetime('2016-01-02','+'||(i%3650)||' days'),18800+(i%1000) FROM seq;
        WITH RECURSIVE seq(i) AS (SELECT 1 UNION ALL SELECT i+1 FROM seq WHERE i<{count*2})
        INSERT INTO Activities(RoomNumber,Action,Before,After,AtUtc) SELECT 101,'历史模拟操作','空房','在住',datetime('2016-01-01','+'||(i%3650)||' days') FROM seq;
        ANALYZE;
        """;
    command.ExecuteNonQuery();await store.SetPasswordAsync("Benchmark-only-2026");Console.WriteLine($"Prepared {count} stays / {count*2} activities at {path}");return;
}
if(args.Contains("--uncommitted")){
    using var db=new SqliteConnection("Data Source="+path);db.Open();using var tx=db.BeginTransaction();using var cmd=db.CreateCommand();cmd.Transaction=tx;cmd.CommandText="UPDATE HotelSettings SET Name='UNCOMMITTED' WHERE Id=1";cmd.ExecuteNonQuery();Console.WriteLine("UNCOMMITTED_READY");await Task.Delay(Timeout.Infinite);return;
}
var report=new Dictionary<string,object>();var samples=new Dictionary<string,List<double>>();long peak=0;using var stop=new CancellationTokenSource();
var sampler=Task.Run(async()=>{try{while(!stop.IsCancellationRequested){using var process=Process.GetCurrentProcess();peak=Math.Max(peak,process.WorkingSet64);await Task.Delay(20,stop.Token);}}catch(OperationCanceledException){}});
async Task Measure(string name,Func<Task> work,int runs=12){var list=new List<double>();for(int i=0;i<runs;i++){var watch=Stopwatch.StartNew();await work();list.Add(watch.Elapsed.TotalMilliseconds);}samples[name]=list;Console.WriteLine($"{name}: first={list[0]:F1} max={list.Max():F1} ms");}
await Measure("board",async()=>{await store.ReadAsync();});
await Measure("history_first",async()=>{await store.SearchStaysAsync();});
await Measure("history_last_page",async()=>{await store.SearchStaysAsync(page:10000000);});
await Measure("search_name",async()=>{await store.SearchStaysAsync("旅客9988");});
await Measure("search_short_name",async()=>{await store.SearchStaysAsync("旅客");});
await Measure("search_short_no_match",async()=>{await store.SearchStaysAsync("不");});
await Measure("search_no_match",async()=>{await store.SearchStaysAsync("不存在名字");});
await Measure("search_document",async()=>{await store.SearchStaysAsync("000000009988");});
await Measure("suggest_name",async()=>{await store.SuggestGuestsAsync("旅客99");});
await Measure("suggest_one_character",async()=>{await store.SuggestGuestsAsync("旅");});
await Measure("suggest_no_match",async()=>{await store.SuggestGuestsAsync("不");});
await Measure("bill_10years",async()=>{await store.ReadBillAsync("2016-01-01T00:00","2026-12-31T23:59");});
var room=(await store.ReadAsync()).Rooms.Single(r=>r.Number==101);
long version=room.Version;
await Measure("checkin_checkout_clean",async()=>{
    await store.ChangeAsync(room.Id,version++,"CheckIn",new("性能测试",SalePrice:188,Platform:"线下"));
    await store.ChangeAsync(room.Id,version++,"CheckOut");
    await store.ChangeAsync(room.Id,version++,"Clean");
    await store.ReadAsync();
},30);
await Measure("stream_full_export",async()=>{await using var file=await store.OpenStaysCsvAsync(password:"Benchmark-only-2026");if(file.Length==0)throw new Exception("Empty export");},1);
var backupTask=store.BackupAsync("Benchmark-only-2026");await Measure("foreground_during_backup",async()=>{await store.SearchStaysAsync("旅客9988");await store.ChangeAsync(room.Id,version++,"CheckIn",new("备份中前台测试",SalePrice:188));await store.ChangeAsync(room.Id,version++,"CheckOut");await store.ChangeAsync(room.Id,version++,"Clean");await store.ReadAsync();},20);var backupFile=await backupTask;File.Delete(backupFile);
stop.Cancel();await sampler;using(var process=Process.GetCurrentProcess())peak=Math.Max(peak,process.PeakWorkingSet64);
report["os"]=System.Runtime.InteropServices.RuntimeInformation.OSDescription;report["sqlite"]=typeof(SqliteConnection).Assembly.GetName().Version!.ToString();report["storage"]=await store.ReadStorageInfoAsync();report["peakRssBytes"]=peak;report["peakRssMB"]=peak/1000000d;report["peakRssMiB"]=peak/1048576d;
report["timings"]=samples.ToDictionary(p=>p.Key,p=>new{first=p.Value[0],p50=p.Value.Order().ElementAt(p.Value.Count/2),max=p.Value.Max(),runs=p.Value.Count});
report["foregroundUnder1s"]=samples.Where(p=>p.Key!="stream_full_export").All(p=>p.Value.Max()<1000);
report["coreProcessUnder200Mb"]=peak<200000000L;
var output=JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});await File.WriteAllTextAsync(path+".benchmark.json",output);Console.WriteLine(output);
if(!(bool)report["foregroundUnder1s"]||!(bool)report["coreProcessUnder200Mb"])Environment.ExitCode=2;
