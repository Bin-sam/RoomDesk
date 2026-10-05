using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace RoomDesk.Core;

public sealed class SecuritySetting
{
    public int Id { get; set; } = 1;
    public string Salt { get; set; } = "";
    public string Hash { get; set; } = "";
    public int FailedAttempts { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
}

public sealed partial class BoardStore
{
    private readonly SemaphoreSlim securityGate = new(1,1);
    private async Task UpgradeAsync(BoardDbContext db)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS SecuritySettings (Id INTEGER PRIMARY KEY, Salt TEXT NOT NULL, Hash TEXT NOT NULL, FailedAttempts INTEGER NOT NULL, LockedUntilUtc TEXT NULL)");
        async Task Column(string table, string name, string sql)
        {
            var connection=db.Database.GetDbConnection();
            await using var cmd=connection.CreateCommand();cmd.Transaction=tx.GetDbTransaction();cmd.CommandText=$"PRAGMA table_info({table})";
            bool exists=false;
            await using(var reader=await cmd.ExecuteReaderAsync()) while(await reader.ReadAsync()) if(reader.GetString(1)==name) exists=true;
            if(!exists) await db.Database.ExecuteSqlRawAsync(sql);
        }
        await Column("BoardStates","IsDeleted","ALTER TABLE BoardStates ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0");
        await Column("BoardStates","TypeName","ALTER TABLE BoardStates ADD COLUMN TypeName TEXT NULL");
        await Column("BoardStates","DefaultPriceCents","ALTER TABLE BoardStates ADD COLUMN DefaultPriceCents INTEGER NULL");
        await Column("GuestRegistrations","SalePriceCents","ALTER TABLE GuestRegistrations ADD COLUMN SalePriceCents INTEGER NULL");
        await Column("GuestRegistrations","DeletedAtUtc","ALTER TABLE GuestRegistrations ADD COLUMN DeletedAtUtc TEXT NULL");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_GuestRegistrations_BillTime ON GuestRegistrations(CheckedInAtUtc) WHERE DeletedAtUtc IS NULL");
        await Column("BoardStates","ReservationName","ALTER TABLE BoardStates ADD COLUMN ReservationName TEXT NULL");
        await Column("BoardStates","ReservationPhone","ALTER TABLE BoardStates ADD COLUMN ReservationPhone TEXT NULL");
        await Column("BoardStates","ReservationPlatform","ALTER TABLE BoardStates ADD COLUMN ReservationPlatform TEXT NULL");
        await Column("GuestRegistrations","Platform","ALTER TABLE GuestRegistrations ADD COLUMN Platform TEXT NOT NULL DEFAULT ''");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS PlatformPresets (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, NameKey TEXT NOT NULL UNIQUE, IsHidden INTEGER NOT NULL DEFAULT 0)");
        for (int i=0;i<DefaultPlatforms.Count;i++)
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT OR IGNORE INTO PlatformPresets (Id,Name,NameKey,IsHidden) VALUES ({i-4},{DefaultPlatforms[i]},{DefaultPlatforms[i].ToUpperInvariant()},0)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS RoomTypePresets (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, NameKey TEXT NOT NULL UNIQUE, IsHidden INTEGER NOT NULL DEFAULT 0)");
        for (int i=0;i<DefaultRoomTypes.Count;i++)
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT OR IGNORE INTO RoomTypePresets (Id,Name,NameKey,IsHidden) VALUES ({i-5},{DefaultRoomTypes[i]},{DefaultRoomTypes[i].ToUpperInvariant()},0)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS HotelSettings (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("INSERT OR IGNORE INTO HotelSettings(Id,Name) VALUES(1,'栖间')");
        await UpgradeSearchAsync(db);
        await tx.CommitAsync();
    }
    public async Task<bool> HasPasswordAsync() { await using var db=Open();return await db.SecuritySettings.AnyAsync(); }
    public Task SetPasswordAsync(string newPassword,string? currentPassword=null)
    {
        if(string.IsNullOrWhiteSpace(newPassword)||newPassword.Length is <8 or >128) throw new BoardException("密码须为 8–128 位，不能全为空格。");
        return AuthenticateAsync(currentPassword,newPassword);
    }
    private Task RequirePasswordAsync(string? password)=>AuthenticateAsync(password,null);
    private async Task AuthenticateAsync(string? password,string? replacement)
    {
        await securityGate.WaitAsync();
        try
        {
            await using var db=Open();await using var tx=await db.Database.BeginTransactionAsync();
            var setting=await db.SecuritySettings.SingleOrDefaultAsync();string? error=null;
            if(setting==null && replacement==null) error="请先在数据与安全页面设置操作密码。";
            if(setting!=null)
            {
                if(setting.LockedUntilUtc>DateTime.UtcNow) error="密码错误次数过多，请一分钟后重试。";
                else
                {
                    var valid=password is { Length: >0 and <=128 } && CryptographicOperations.FixedTimeEquals(
                        Rfc2898DeriveBytes.Pbkdf2(password,Convert.FromBase64String(setting.Salt),210000,HashAlgorithmName.SHA256,32),Convert.FromBase64String(setting.Hash));
                    if(!valid)
                    {
                        setting.FailedAttempts++;
                        if(setting.FailedAttempts>=5){setting.LockedUntilUtc=DateTime.UtcNow.AddMinutes(1);setting.FailedAttempts=0;}
                        error="操作密码不正确。";
                    }
                    else {setting.FailedAttempts=0;setting.LockedUntilUtc=null;}
                }
            }
            if(error==null && replacement!=null)
            {
                if(setting==null){setting=new SecuritySetting();db.SecuritySettings.Add(setting);}
                var salt=RandomNumberGenerator.GetBytes(32);setting.Salt=Convert.ToBase64String(salt);
                setting.Hash=Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(replacement,salt,210000,HashAlgorithmName.SHA256,32));
                setting.FailedAttempts=0;setting.LockedUntilUtc=null;
            }
            await db.SaveChangesAsync();await tx.CommitAsync();
            if(error!=null)throw new BoardException(error);
        }
        finally{securityGate.Release();}
    }
    public static long PriceCents(decimal value)
    {
        if(value<0 || value>99999999.99m || decimal.Round(value,2)!=value)throw new BoardException("价格须为 0–99999999.99 元，最多两位小数。");
        return (long)(value*100m);
    }
    public static string PriceText(long? cents)=>cents.HasValue?(cents.Value/100m).ToString("0.00",CultureInfo.InvariantCulture):"未登记";
    public async Task SetRoomPriceAsync(int id,long version,decimal price)
    {
        var cents=PriceCents(price);await gate.WaitAsync();
        try
        {
            await using var db=Open();var room=await db.BoardStates.Include(r=>r.Room).SingleOrDefaultAsync(r=>r.RoomId==id)??throw new BoardException("房间不存在。");
            if(room.Version!=version)throw new BoardException("房间已更新，请刷新后重试。");
            var before=PriceText(room.DefaultPriceCents);room.DefaultPriceCents=cents;room.Version++;
            db.Activities.Add(new RoomActivity{RoomNumber=room.Room.RoomNumber,Action="设置默认价格",Before=before,After=PriceText(cents),AtUtc=DateTime.UtcNow});await db.SaveChangesAsync();
        }
        catch(DbUpdateConcurrencyException){throw new BoardException("房间已更新，请刷新后重试。");}
        finally{gate.Release();}
    }
    public async Task DeleteStayAsync(long id,string? password)
    {
        await RequirePasswordAsync(password);await gate.WaitAsync();
        try
        {
            await using var db=Open();var stay=await db.GuestRegistrations.SingleOrDefaultAsync(g=>g.Id==id && g.DeletedAtUtc==null)??throw new BoardException("入住记录不存在或已删除。");
            if(stay.CheckedOutAtUtc==null)throw new BoardException("在住记录不能删除，请先办理退房。");
            stay.DeletedAtUtc=DateTime.UtcNow;
            var number=await db.Rooms.Where(r=>r.Id==stay.RoomId).Select(r=>r.RoomNumber).SingleAsync();
            db.Activities.Add(new RoomActivity{RoomNumber=number,Action="删除入住记录",Before=$"记录 #{id}",After="已移出入住记录和账单",AtUtc=DateTime.UtcNow});
            await db.SaveChangesAsync();
        }
        finally{gate.Release();}
    }
}
