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

    /// <summary>动物类型 -> 实际容纳它的房间。绵羊(Sheep)与山羊(Goat)共用一间「羊场」(9 种动物 8 间房),
    /// 所以买绵羊、判解锁/容量、生成实体动物都要落到 RoomType.Goat 这间房;其余动物 1:1。
    /// 直接 Get(RoomType.Sheep) 会抛异常(All 里没有 Sheep 房),所有按"动物买的房间"走的代码必须改用本方法。</summary>
    public static RoomType RoomFor(RoomType animalType) => animalType == RoomType.Sheep ? RoomType.Goat : animalType;
}
