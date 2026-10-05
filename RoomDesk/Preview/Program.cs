using System.Text.Json;
using System.Security.Cryptography;
using RoomDesk.Core;

var builder = WebApplication.CreateBuilder(args);
// Deliberately local only. This preview has no multi-user authentication.
builder.WebHost.UseUrls("http://127.0.0.1:5188");
var app = builder.Build();
var store = new BoardStore(builder.Configuration["data"] ?? BoardStore.DefaultDatabasePath);
await store.InitializeAsync();
var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
app.Use(async (context, next) => {
    if (context.Request.Host.Host != "127.0.0.1") { context.Response.StatusCode = 403; return; }
    context.Response.Headers["Cache-Control"] = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; style-src 'self'; script-src 'self'; img-src 'self' data:; frame-ancestors 'none'";
    if (context.Request.Method == "POST" && context.Request.Headers["X-RoomDesk-Token"] != token) { context.Response.StatusCode = 403; return; }
    try { await next(); }
    catch (BoardException ex) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { error = ex.Message }); }
    catch (BadHttpRequestException) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "请求格式不正确。" }); }
    catch (Exception ex) { app.Logger.LogError(ex, "Preview operation failed"); context.Response.StatusCode = 500; await context.Response.WriteAsJsonAsync(new { error = "本地存储操作失败，请查看运行日志。" }); }
});
app.UseDefaultFiles(); app.UseStaticFiles();
app.MapGet("/api/board", async () => new { snapshot = await store.ReadAsync(), token });
app.MapPost("/api/rooms/{id:int}/action", async (int id, ChangeRequest request) => {
    await store.ChangeAsync(id, request.Version, request.Action, request.Guest); return Results.Ok(new { ok = true });
});
app.MapGet("/api/rooms/{id:int}/guest", async (int id) => Results.Ok(new { guest = await store.GetCurrentGuestAsync(id) }));
app.MapGet("/api/guests/suggestions", async (string? query, string? field) =>
    await store.SuggestGuestsAsync(query, field ?? "name"));
app.MapPost("/api/identity/parse", (IdentityTextRequest request) => IdentityCardInput.Parse(request.Text));
app.MapGet("/api/stays", async (string? search, string? status, int? page) =>
    await store.SearchStaysAsync(search, status, page ?? 1));
app.MapPost("/api/stays/export", async (ExportRequest request) =>
    Results.File(await store.ExportStaysCsvAsync(request.Search, request.Status, request.Password), "text/csv; charset=utf-8", $"入住记录-{DateTime.UtcNow.AddHours(8):yyyyMMdd-HHmmss}.csv"));
app.MapGet("/api/security", async () => new { configured = await store.HasPasswordAsync() });
app.MapPost("/api/security/password", async (PasswordChange request) => { await store.SetPasswordAsync(request.NewPassword,request.CurrentPassword);return Results.Ok(new { ok=true }); });
app.MapGet("/api/bills", async (string start,string end)=> await store.ReadBillAsync(start,end));
app.MapPost("/api/bills/export", async (BillRequest request)=>Results.File(await store.ExportBillAsync(request.Start,request.End,request.Password),"text/csv; charset=utf-8",$"入住账单-{DateTime.UtcNow.AddHours(8):yyyyMMdd-HHmmss}.csv"));
app.MapPost("/api/stays/{id:long}/delete",async(long id,PasswordRequest request)=>{await store.DeleteStayAsync(id,request.Password);return Results.Ok(new{ok=true});});
app.MapPost("/api/rooms", async (AddRequest request) => { await store.AddRoomAsync(request.Number, request.Floor, request.Type,request.DefaultPrice); return Results.Ok(new { ok = true }); });
app.MapPost("/api/rooms/{id:int}/price",async(int id,PriceRequest request)=>{await store.SetRoomPriceAsync(id,request.Version,request.Price ?? throw new BoardException("请填写房间默认价格。"));return Results.Ok(new{ok=true});});
app.MapPost("/api/backup", async (PasswordRequest request) => Results.Ok(new { path = await store.BackupAsync(request.Password) }));
await app.RunAsync();
record ChangeRequest(long Version, string Action, GuestInput? Guest = null);
record AddRequest(int Number, int Floor, string Type, decimal? DefaultPrice = null);
record PriceRequest(long Version,decimal? Price);
record PasswordRequest(string? Password);
record PasswordChange(string NewPassword,string? CurrentPassword);
record ExportRequest(string? Search,string? Status,string? Password);
record BillRequest(string Start,string End,string? Password);

record IdentityTextRequest(string? Text);
