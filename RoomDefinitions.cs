namespace AnimalBarn;

/// <summary>9 个动物房间的定义(地图名/显示名)。每种动物独立一间房(羊和山羊也分开)。</summary>
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
        new(RoomType.Sheep,    MapPrefix + "Sheep",    "养羊场"),
    };

    public static RoomDef Get(RoomType room) => All.First(r => r.Room == room);

    /// <summary>动物类型 -> 实际容纳它的房间。9 种动物现在各一间房,1:1 映射(羊和山羊已分开)。
    /// 保留此方法仅为兼容旧存档(旧版羊在羊场 Goat 房,读档后迁移到 Sheep 房)。</summary>
    public static RoomType RoomFor(RoomType animalType) => animalType == RoomType.Sheep && !HasRoom(animalType) ? RoomType.Goat : animalType;

    /// <summary>是否已有该房间定义。</summary>
    public static bool HasRoom(RoomType room) => All.Any(r => r.Room == room);
}
