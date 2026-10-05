using LantelHotelApp.Data;
using LantelHotelApp.Models;
using Microsoft.EntityFrameworkCore;

namespace RoomDesk.Core;

public enum Occupancy { Vacant, Reserved, Occupied }
public enum ServiceState { Normal, Maintenance, Disabled }
public enum RoomAction { Reserve, CancelReservation, CheckIn, CheckOut, MarkDirty, Clean, StartMaintenance, Disable, Restore }

public sealed class BoardState
{
    public int RoomId { get; set; }
    public Room Room { get; set; } = null!;
    public Occupancy Occupancy { get; set; }
    public ServiceState Service { get; set; }
    public bool IsClean { get; set; } = true;
    public long Version { get; set; }
    public long? DefaultPriceCents { get; set; }
}

public sealed class RoomActivity
{
    public long Id { get; set; }
    public int RoomNumber { get; set; }
    public string Action { get; set; } = "";
    public string Before { get; set; } = "";
    public string After { get; set; } = "";
    public DateTime AtUtc { get; set; }
}

public sealed record GuestInput(string Name, string? Phone = null, string? DocumentType = null,
    string? DocumentNumber = null, string? Notes = null, decimal? SalePrice = null);

public sealed class GuestRegistration
{
    public long Id { get; set; }
    public int RoomId { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string DocumentType { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime CheckedInAtUtc { get; set; }
    public DateTime? CheckedOutAtUtc { get; set; }
    public long? SalePriceCents { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public decimal? SalePrice => SalePriceCents / 100m;
}

public sealed class BoardDbContext(DbContextOptions<HotelDbContext> options) : HotelDbContext(options)
{
    public DbSet<SecuritySetting> SecuritySettings => Set<SecuritySetting>();
    public DbSet<BoardState> BoardStates => Set<BoardState>();
    public DbSet<RoomActivity> Activities => Set<RoomActivity>();
    public DbSet<GuestRegistration> GuestRegistrations => Set<GuestRegistration>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<BoardState>().HasKey(s => s.RoomId);
        builder.Entity<BoardState>().Property(s => s.Version).IsConcurrencyToken();
        builder.Entity<BoardState>().HasOne(s => s.Room).WithOne().HasForeignKey<BoardState>(s => s.RoomId);
        builder.Entity<RoomActivity>().HasIndex(a => a.AtUtc);
        builder.Entity<GuestRegistration>().HasOne<Room>().WithMany().HasForeignKey(g => g.RoomId);
        builder.Entity<GuestRegistration>().HasIndex(g => g.RoomId).IsUnique().HasFilter("CheckedOutAtUtc IS NULL");
    }
}

public sealed record RoomCard(int Id, int Number, int Floor, string Type, string Status,
    string StatusKey, string Occupancy, string Service, bool IsClean, long Version,
    IReadOnlyList<ActionOption> Actions)
{
    public string? GuestName { get; init; }
    public long? DefaultPriceCents { get; init; }
    public decimal? DefaultPrice => DefaultPriceCents / 100m;
    public string DefaultPriceText => BoardStore.PriceText(DefaultPriceCents);
    public string GuestLabel => Occupancy == nameof(Core.Occupancy.Occupied)
        ? "入住人：" + (string.IsNullOrWhiteSpace(GuestName) ? "未登记" : GuestName) : "";
    public string BoardCaption => Occupancy == nameof(Core.Occupancy.Occupied)
        ? (string.IsNullOrWhiteSpace(GuestName) ? "未登记入住人" : GuestName) : IsClean ? "已清洁" : "等待清扫";
    public string CleanLabel => IsClean ? "已清洁" : "待清扫";
    public string Subtitle => $"{Type} · {Floor} 楼";
    public string Color => StatusKey switch
    {
        "ready" => "#237B66", "occupied" => "#426AB2", "reserved" => "#AA7931",
        "dirty" => "#B26D3D", "maintenance" => "#8362A6", _ => "#707785"
    };
    public string SoftColor => StatusKey switch
    {
        "ready" => "#EFF8F4", "occupied" => "#F0F4FD", "reserved" => "#FCF7EA",
        "dirty" => "#FFF3EB", "maintenance" => "#F6F0FB", _ => "#F0F1F3"
    };
}
public sealed record ActionOption(string Key, string Label);
public sealed record BoardSnapshot(IReadOnlyList<RoomCard> Rooms, IReadOnlyList<RoomActivity> Activities)
{
    public int Ready => Rooms.Count(r => r.StatusKey == "ready");
    public int Occupied => Rooms.Count(r => r.Occupancy == nameof(Core.Occupancy.Occupied));
    public int Reserved => Rooms.Count(r => r.Occupancy == nameof(Core.Occupancy.Reserved));
    public int Dirty => Rooms.Count(r => !r.IsClean);
    public int Unavailable => Rooms.Count(r => r.Service != nameof(ServiceState.Normal));
}
public sealed class BoardException(string message) : Exception(message);
