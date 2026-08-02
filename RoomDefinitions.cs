namespace AnimalBarn;

/// <summary>8 个动物房间的定义(地图名/显示名)。</summary>
public record RoomDef(RoomType Room, string MapName, string DisplayName);

public static class RoomDefinitions
{
    public const string MapPrefix = "xiepe.AnimalBarn.Room.";

    public static readonly RoomDef[] All =
    {
        new(RoomType.Chicken,  MapPrefix + "Chicken",  "养鸡场"),
        new(RoomType.Duck,     MapPrefix + "Duck",     "养鸭场"),
        new(RoomType.Rabbit,   MapPrefix + "Rabbit",   "养兔场"),
        new(RoomType.Dinosaur, MapPrefix + "Dino",     "恐龙场"),
        new(RoomType.Ostrich,  MapPrefix + "Ostrich",  "鸵鸟场"),
        new(RoomType.Pig,      MapPrefix + "Pig",      "养猪场"),
        new(RoomType.Goat,     MapPrefix + "Goat",     "羊场"),
        new(RoomType.Cow,      MapPrefix + "Cow",      "养牛场"),
    };

    public static RoomDef Get(RoomType room) => All.First(r => r.Room == room);
}
