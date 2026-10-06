using LantelHotelApp.Models;
using LantelHotelApp.Models.Enums;
namespace RoomDesk.Core;

public sealed record DefaultRoom(int Number,int Floor,string Type);
public sealed partial class BoardStore
{
    // Transcribed from the owner's two room-list photographs; prices were clipped.
    public static IReadOnlyList<DefaultRoom> DefaultRooms { get; } = Array.AsReadOnly(new[] {
        new DefaultRoom(8510,5,"标准双人间"),new(8402,4,"标准双人间"),new(8512,5,"标准双人间"),
        new(8410,4,"标准双人间"),new(8502,5,"标准双人间"),new(8602,6,"标准双人间"),new(8610,6,"标准双人间"),
        new(8403,4,"豪华单人房"),new(8611,6,"豪华单人房"),new(8505,5,"豪华单人房"),
        new(8608,6,"豪华单人房"),new(8605,6,"豪华单人房"),new(8503,5,"豪华单人房"),
        new(8603,6,"豪华单人房"),new(8508,5,"豪华单人房"),new(8411,4,"豪华单人房"),
        new(8405,4,"豪华单人房"),new(8511,5,"豪华单人房"),new(8408,4,"豪华单人房"),
        new(8606,6,"豪华双人房"),new(8506,5,"豪华双人房"),new(8406,4,"豪华双人房"),
        new(8507,5,"商务双人房【棋牌】"),new(8509,5,"商务双人房【棋牌】"),new(8401,4,"商务双人房【棋牌】"),
        new(8609,6,"商务双人房【棋牌】"),new(8409,4,"商务双人房【棋牌】"),new(8407,4,"商务双人房【棋牌】"),
        new(8607,6,"商务双人房【棋牌】"),new(8601,6,"商务双人房【棋牌】"),new(8501,5,"商务双人房【棋牌】"),
        new(8612,6,"商务套房"),new(8412,4,"商务套房")
    });
    private static void SeedDefaultRooms(BoardDbContext db)
    {
        foreach(var item in DefaultRooms)
            db.BoardStates.Add(new BoardState {TypeName=item.Type,IsClean=false,
                Room=new Room {RoomNumber=item.Number,FloorNumber=item.Floor,RoomType=item.Type=="标准双人间"?RoomType.Standard:item.Type=="商务套房"?RoomType.ExecutiveSuite:RoomType.Deluxe,Status=RoomStatus.ScheduledCleaning}});
    }
}
