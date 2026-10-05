using LantelHotelApp.Data;
using LantelHotelApp.Models;
using LantelHotelApp.Models.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace RoomDesk.Core;

// Both the WPF client and the Mac preview call this service. No duplicate JS business rules.
public sealed partial class BoardStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public string DatabasePath { get; }
    public static string DefaultDatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoomDeskPrototype", "rooms-v1.db");
    public BoardStore(string path)
    {
        DatabasePath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
    }
    private BoardDbContext Open()
    {
        var connection=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=DatabasePath, DefaultTimeout=5, Pooling=false }.ToString());
        try {
            connection.Open();
            using var command=connection.CreateCommand();
            // FULL commits the WAL to stable storage before success is returned. Cache is per connection.
            command.CommandText="PRAGMA synchronous=FULL; PRAGMA fullfsync=ON; PRAGMA foreign_keys=ON; PRAGMA cache_size=-2048; PRAGMA mmap_size=0; PRAGMA temp_store=FILE; PRAGMA wal_autocheckpoint=1000; PRAGMA journal_size_limit=16777216;";
            command.ExecuteNonQuery();
            return new(new DbContextOptionsBuilder<HotelDbContext>().UseSqlite(connection,contextOwnsConnection:true).Options);
        } catch { connection.Dispose();throw; }
    }

    public async Task InitializeAsync(int seedCount = 24)
    {
        await gate.WaitAsync();
        try
        {
            await using var db = Open();
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
            // Additive, idempotent v0.2 upgrade for existing v0.1 databases. No room data is rewritten.
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS GuestRegistrations (
                    Id INTEGER NOT NULL CONSTRAINT PK_GuestRegistrations PRIMARY KEY AUTOINCREMENT,
                    RoomId INTEGER NOT NULL, Name TEXT NOT NULL, Phone TEXT NOT NULL,
                    DocumentType TEXT NOT NULL, DocumentNumber TEXT NOT NULL, Notes TEXT NOT NULL,
                    CheckedInAtUtc TEXT NOT NULL, CheckedOutAtUtc TEXT NULL,
                    FOREIGN KEY (RoomId) REFERENCES Rooms(Id) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_GuestRegistrations_RoomId
                    ON GuestRegistrations(RoomId) WHERE CheckedOutAtUtc IS NULL;
                """);
            await UpgradeAsync(db);
            if (await db.Rooms.AnyAsync()) return;
            for (int i = 0; i < seedCount; i++)
            {
                int floor = i / 8 + 1;
                var room = new Room { RoomNumber = floor * 100 + i % 8 + 1, FloorNumber = floor,
                    RoomType = i % 8 >= 6 ? RoomType.Deluxe : RoomType.Standard, LastCleaned = null };
                var state = new BoardState { Room = room, IsClean = true };
                // Predictable sample rooms; this database is isolated from upstream Lantel data.
                if (i % 8 == 1) state.Occupancy = Occupancy.Occupied;
                if (i % 8 == 2) state.Occupancy = Occupancy.Reserved;
                if (i % 8 == 3) state.IsClean = false;
                if (i == 6) { state.Service = ServiceState.Maintenance; state.IsClean = false; }
                if (i == 7) state.Service = ServiceState.Disabled;
                room.Status = LegacyStatus(state);
                db.BoardStates.Add(state);
            }
            await db.SaveChangesAsync();
        }
        finally { gate.Release(); }
    }

    public Task<BoardSnapshot> ReadAsync() => ReadAsync(false);
    public async Task<BoardSnapshot> ReadAsync(bool includeDeleted)
    {
        await using var db = Open();
        var states=includeDeleted?db.BoardStates.IgnoreQueryFilters():db.BoardStates;
        var rows = await states.AsNoTracking().Include(s => s.Room)
            .OrderBy(s => s.Room.FloorNumber).ThenBy(s => s.Room.RoomNumber)
            .Select(s => new { State = s, GuestName = db.GuestRegistrations
                .Where(g => g.RoomId == s.RoomId && g.CheckedOutAtUtc == null && s.Occupancy == Occupancy.Occupied)
                .Select(g => g.Name).FirstOrDefault() }).ToListAsync();
        var activities = await db.Activities.AsNoTracking().OrderByDescending(a => a.Id).Take(100).ToListAsync();
        return new(rows.Select(row => Card(row.State) with { IsDeleted=row.State.IsDeleted, GuestName = row.GuestName, DefaultPriceCents = row.State.DefaultPriceCents, ReservationName = row.State.ReservationName }).ToList(), activities);
    }

    public async Task<GuestRegistration?> GetCurrentGuestAsync(int roomId)
    {
        await using var db = Open();
        return await db.GuestRegistrations.AsNoTracking().SingleOrDefaultAsync(g => g.RoomId == roomId && g.CheckedOutAtUtc == null);
    }

    public static GuestInput ValidateGuest(GuestInput? input)
    {
        if (input is null || string.IsNullOrWhiteSpace(input.Name)) throw new BoardException("请填写入住人姓名。");
        var guest = new GuestInput(input.Name.Trim(), input.Phone?.Trim() ?? "", input.DocumentType?.Trim() ?? "",
            input.DocumentNumber?.Trim() ?? "", input.Notes?.Trim() ?? "", input.SalePrice, ValidatePlatform(input.Platform));
        if(guest.SalePrice is null) throw new BoardException("请填写本次入住售出总价。");
        PriceCents(guest.SalePrice.Value);
        if (guest.Name.Length > 80 || guest.Phone!.Length > 40 || guest.DocumentNumber!.Length > 50 || guest.Notes!.Length > 500)
            throw new BoardException("姓名最多 80 字、电话 40 字、证件号码 50 字、备注 500 字。");
        if (guest.DocumentType is not ("" or "身份证" or "护照" or "其他")) throw new BoardException("请选择有效的证件类型。");
        if (guest.DocumentNumber.Length > 0 && guest.DocumentType.Length == 0) throw new BoardException("填写证件号码时，请选择证件类型。");
        return guest;
    }

    public async Task ChangeAsync(int roomId, long expectedVersion, string actionKey, GuestInput? guest = null, ReservationInput? reservation = null)
    {
        if (!Enum.TryParse<RoomAction>(actionKey, out var action) || !Enum.IsDefined(action))
            throw new BoardException("不支持的房态操作。");
        var registration = action == RoomAction.CheckIn ? ValidateGuest(guest) : null;
        var booking = action == RoomAction.Reserve ? ValidateReservation(reservation) : null;
        await gate.WaitAsync();
        try
        {
            await using var db = Open();
            await using var tx = await db.Database.BeginTransactionAsync();
            var s = await db.BoardStates.Include(s => s.Room).SingleOrDefaultAsync(s => s.RoomId == roomId)
                ?? throw new BoardException("房间不存在，请刷新。");
            if (s.Version != expectedVersion) throw new BoardException("房态已被其他操作更新，请刷新后重试。");
            if (!Allowed(s).Contains(action)) throw new BoardException("当前房态不允许此操作，请先完成清扫或解除停用。");
            var before = Description(s);
            switch (action)
            {
                case RoomAction.Reserve:
                    s.Occupancy = Occupancy.Reserved;
                    s.ReservationName = booking!.Name; s.ReservationPhone = booking.Phone; s.ReservationPlatform = booking.Platform;
                    break;
                case RoomAction.CancelReservation: s.Occupancy = Occupancy.Vacant; ClearReservation(s); break;
                case RoomAction.CheckIn:
                    db.GuestRegistrations.Add(new GuestRegistration { RoomId = roomId,
                        Name = registration!.Name, Platform = registration.Platform!, Phone = registration.Phone!, DocumentType = registration.DocumentType!,
                        DocumentNumber = registration.DocumentNumber!, Notes = registration.Notes!, CheckedInAtUtc = DateTime.UtcNow, SalePriceCents = PriceCents(registration.SalePrice!.Value) });
                    s.Occupancy = Occupancy.Occupied; ClearReservation(s);
                    break;
                case RoomAction.CheckOut:
                    var current = await db.GuestRegistrations.SingleOrDefaultAsync(g => g.RoomId == roomId && g.CheckedOutAtUtc == null);
                    if (current != null) current.CheckedOutAtUtc = DateTime.UtcNow;
                    s.Occupancy = Occupancy.Vacant; s.IsClean = false;
                    break;
                case RoomAction.MarkDirty: s.IsClean = false; break;
                case RoomAction.Clean: s.IsClean = true; s.Room.LastCleaned = DateTime.Now; break;
                case RoomAction.StartMaintenance: s.Service = ServiceState.Maintenance; s.IsClean = false; break;
                case RoomAction.Disable: s.Service = ServiceState.Disabled; break;
                case RoomAction.Restore: s.Service = ServiceState.Normal; s.IsClean = false; break;
            }
            s.Version++;
            s.Room.Status = LegacyStatus(s);
            db.Activities.Add(new RoomActivity { RoomNumber = s.Room.RoomNumber, Action = Label(action),
                Before = before, After = Description(s), AtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateConcurrencyException) { throw new BoardException("房态已更新，请刷新后重试。"); }
        finally { gate.Release(); }
    }

    public Task AddRoomAsync(int number, int floor, string type, decimal? defaultPrice = null)
        => AddRoomsAsync(number.ToString(System.Globalization.CultureInfo.InvariantCulture),floor,type,defaultPrice);

    // SQLite online backup includes committed WAL data, unlike copying only the .db file.
    public async Task<string> BackupAsync(string? password = null)
    {
        await RequirePasswordAsync(password);
        var folder=Path.Combine(Path.GetDirectoryName(DatabasePath)!,"backups");Directory.CreateDirectory(folder);
        var path=Path.Combine(folder,$"rooms-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString()[..6]}.db");
        await CopyOnlineBackupAsync(path);return path;
    }

    private static IReadOnlyList<RoomAction> Allowed(BoardState s)
    {
        if (s.Service != ServiceState.Normal) return [RoomAction.Restore];
        var actions = new List<RoomAction>();
        if (s.Occupancy != Occupancy.Occupied && s.IsClean) actions.Add(RoomAction.CheckIn);
        if (s.Occupancy == Occupancy.Vacant && s.IsClean) actions.Add(RoomAction.Reserve);
        if (s.Occupancy == Occupancy.Reserved) actions.Add(RoomAction.CancelReservation);
        if (s.Occupancy == Occupancy.Occupied) actions.Add(RoomAction.CheckOut);
        actions.Add(s.IsClean ? RoomAction.MarkDirty : RoomAction.Clean);
        if (s.Occupancy == Occupancy.Vacant) { actions.Add(RoomAction.StartMaintenance); actions.Add(RoomAction.Disable); }
        return actions;
    }
    public static string Label(RoomAction action) => action switch
    {
        RoomAction.Reserve => "办理预订", RoomAction.CancelReservation => "取消预订",
        RoomAction.CheckIn => "办理入住", RoomAction.CheckOut => "标记退房",
        RoomAction.MarkDirty => "设为待清扫", RoomAction.Clean => "清扫完成",
        RoomAction.StartMaintenance => "设为维修", RoomAction.Disable => "停用房间", _ => "恢复使用"
    };
    private static string Description(BoardState s) => $"{(s.Occupancy switch { Occupancy.Vacant => "空房", Occupancy.Reserved => "预订", _ => "在住" })} / {(s.IsClean ? "已清洁" : "待清扫")} / {(s.Service switch { ServiceState.Normal => "正常", ServiceState.Maintenance => "维修", _ => "停用" })}";
    private static RoomStatus LegacyStatus(BoardState s) => s.Service switch
    {
        ServiceState.Maintenance => RoomStatus.UnderMaintenance, ServiceState.Disabled => RoomStatus.OutOfService,
        _ => s.Occupancy switch { Occupancy.Occupied => RoomStatus.Occupied, Occupancy.Reserved => RoomStatus.Reserved,
            _ => s.IsClean ? RoomStatus.Available : RoomStatus.ScheduledCleaning }
    };
    private static RoomCard Card(BoardState s)
    {
        var (key, label) = s.Service switch
        {
            ServiceState.Maintenance => ("maintenance", "维修中"), ServiceState.Disabled => ("disabled", "已停用"),
            _ => s.Occupancy switch { Occupancy.Occupied => ("occupied", "在住"), Occupancy.Reserved => ("reserved", "已预订"),
                _ => s.IsClean ? ("ready", "可入住") : ("dirty", "待清扫") }
        };
        return new(s.RoomId, s.Room.RoomNumber, s.Room.FloorNumber, s.TypeName ?? (s.Room.RoomType switch {
            RoomType.Standard => "标准间", RoomType.Deluxe => "豪华房", RoomType.ExecutiveSuite => "行政套房",
            RoomType.VipSuite => "贵宾套房", _ => "顶层套房" }), label, key, s.Occupancy.ToString(), s.Service.ToString(), s.IsClean, s.Version,
            Allowed(s).Select(a => new ActionOption(a.ToString(), Label(a))).ToList());
    }
}
