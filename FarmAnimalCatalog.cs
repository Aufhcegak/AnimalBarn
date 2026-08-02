namespace AnimalBarn;

/// <summary>9 种动物类型(与动物房间一一对应;山羊和羊共用羊场房间)。</summary>
public enum RoomType
{
    Chicken, Duck, Rabbit, Dinosaur, Ostrich, Pig, Goat, Cow, Sheep
}

/// <summary>动物类型常量表:类型 key、价格、成长/生产数据。纯逻辑,无 SMAPI 依赖。</summary>
public static class FarmAnimalCatalog
{
    public record AnimalInfo(
        RoomType Room,       // 所属房间
        string TypeKey,      // Data/FarmAnimals 的 key
        int VanillaPrice,    // 玛妮原价
        int BuyPrice,        // 9 折价(整数)
        int MatureDays,      // 成年天数
        string? ProduceId,   // 默认产物 (O)xxx
        int DaysToProduce    // 生产间隔
    );

    public static readonly AnimalInfo[] All =
    {
        new(RoomType.Chicken,  "White Chicken", 800,  720,  3,  "(O)176", 1),
        new(RoomType.Duck,     "Duck",          1200, 1080, 4,  "(O)442", 2),
        new(RoomType.Rabbit,   "Rabbit",        8000, 7200, 4,  "(O)446", 4),
        new(RoomType.Dinosaur, "Dinosaur",      20000,18000, 7,  "(O)107", 7),
        new(RoomType.Ostrich,  "Ostrich",       15000,13500, 15, "(O)289", 7),
        new(RoomType.Pig,      "Pig",           16000,14400, 10, "(O)430", 1),
        new(RoomType.Goat,     "Goat",          4000, 3600, 5,  "(O)436", 1),
        new(RoomType.Cow,      "Dairy Cow",     1500, 1350, 5,  "(O)186", 1),
        new(RoomType.Sheep,    "Sheep",         8000, 7200, 4,  "(O)440", 4),
    };

    public static AnimalInfo Get(RoomType room) => All.First(a => a.Room == room);
}
