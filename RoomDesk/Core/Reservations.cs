using Microsoft.EntityFrameworkCore;
namespace RoomDesk.Core;

public sealed partial class BoardStore
{
    public static IReadOnlyList<string> DefaultPlatforms { get; } = Array.AsReadOnly(new[] { "线下", "携程", "美团", "飞猪" });
    private static string ValidatePlatform(string? platform)
    {
        var value = platform?.Trim() ?? "线下";
        if (string.IsNullOrWhiteSpace(value)) throw new BoardException("请填写预订平台。");
        if (value.Length > 40 || value.Any(char.IsControl)) throw new BoardException("平台名称最多 40 字，不能包含换行或控制字符。");
        return value;
    }
    public static ReservationInput ValidateReservation(ReservationInput? input)
    {
        if (input is null || string.IsNullOrWhiteSpace(input.Name)) throw new BoardException("请填写预订人姓名。");
        var name = input.Name.Trim(); var phone = input.Phone?.Trim() ?? "";
        if (name.Length > 80 || phone.Length > 40) throw new BoardException("姓名最多 80 字、电话最多 40 字。");
        return new(name, ValidatePlatform(input.Platform), phone);
    }
    public async Task<ReservationInput?> GetReservationAsync(int roomId)
    {
        await using var db = Open();
        var state = await db.BoardStates.AsNoTracking().SingleOrDefaultAsync(s => s.RoomId == roomId)
            ?? throw new BoardException("房间不存在，请刷新。");
        return state.Occupancy == Occupancy.Reserved && state.ReservationName != null
            ? new(state.ReservationName, state.ReservationPlatform, state.ReservationPhone) : null;
    }
    private static void ClearReservation(BoardState state)
    {
        state.ReservationName = null; state.ReservationPhone = null; state.ReservationPlatform = null;
    }
}
