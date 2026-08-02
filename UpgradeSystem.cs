namespace AnimalBarn;

/// <summary>升级规则:整体等级(解锁房间+护理效率)+ 房间独立等级(容量)。纯逻辑。</summary>
public static class UpgradeSystem
{
    // 整体:1→5,每次 35000g + 木500 + 石(200/300/400/500)
    public record OverallTier(int Level, int Cost, int Wood, int Stone, string Unlocks, int FriendshipGain);
    public static readonly OverallTier[] Overall =
    {
        new(1, 0, 0, 0, "养鸡场、干草房", 6),
        new(2, 35000, 500, 200, "养猪场", 8),
        new(3, 35000, 500, 300, "养牛场", 9),
        new(4, 35000, 500, 400, "养鸭场、养兔场", 10),
        new(5, 35000, 500, 500, "恐龙场、鸵鸟场、羊场", 12),
    };

    // 房间容量曲线(Level 0 = 初始容量,1-5 为升级档)
    public record CapacityRow(int Level, int Capacity, int Cost, int Wood, int Stone);
    public static CapacityRow[] CapacityFor(RoomType room) => room switch
    {
        RoomType.Chicken or RoomType.Duck => new[]
        {
            new CapacityRow(0, 100, 0, 0, 0), new CapacityRow(1, 200, 20000, 200, 0),
            new CapacityRow(2, 300, 40000, 300, 0), new CapacityRow(3, 400, 60000, 400, 0),
            new CapacityRow(4, 500, 80000, 500, 0), new CapacityRow(5, 600, 100000, 600, 0),
        },
        RoomType.Rabbit => new[]
        {
            new CapacityRow(0, 50, 0, 0, 0), new CapacityRow(1, 100, 20000, 200, 0),
            new CapacityRow(2, 150, 30000, 300, 0), new CapacityRow(3, 200, 40000, 400, 0),
            new CapacityRow(4, 250, 50000, 500, 0), new CapacityRow(5, 300, 60000, 600, 0),
        },
        RoomType.Dinosaur => new[]
        {
            new CapacityRow(0, 10, 0, 0, 0), new CapacityRow(1, 20, 20000, 0, 100),
            new CapacityRow(2, 30, 40000, 0, 200), new CapacityRow(3, 40, 60000, 0, 300),
            new CapacityRow(4, 50, 80000, 0, 400), new CapacityRow(5, 60, 100000, 0, 500),
        },
        RoomType.Ostrich => new[]
        {
            new CapacityRow(0, 20, 0, 0, 0), new CapacityRow(1, 30, 20000, 300, 0),
            new CapacityRow(2, 40, 40000, 400, 0), new CapacityRow(3, 50, 60000, 500, 0),
            new CapacityRow(4, 60, 80000, 600, 0), new CapacityRow(5, 60, 100000, 700, 0),
        },
        _ => new[]  // 猪/牛/羊/山羊
        {
            new CapacityRow(0, 40, 0, 0, 0), new CapacityRow(1, 50, 10000, 200, 0),
            new CapacityRow(2, 60, 20000, 300, 0), new CapacityRow(3, 70, 30000, 400, 0),
            new CapacityRow(4, 80, 40000, 500, 0), new CapacityRow(5, 90, 50000, 600, 0),
        },
    };

    /// <summary>房间是否在当前整体等级解锁。</summary>
    public static bool IsUnlocked(RoomType room, int overallLevel) => room switch
    {
        RoomType.Chicken => overallLevel >= 1,
        RoomType.Pig => overallLevel >= 2,
        RoomType.Cow => overallLevel >= 3,
        RoomType.Duck or RoomType.Rabbit => overallLevel >= 4,
        RoomType.Dinosaur or RoomType.Ostrich or RoomType.Sheep => overallLevel >= 5,
        RoomType.Goat => overallLevel >= 5,
    };

    /// <summary>房间在指定独立等级下的容量。</summary>
    public static int CapacityAt(RoomType room, int roomLevel)
    {
        var rows = CapacityFor(room);
        return rows[Math.Clamp(roomLevel, 0, rows.Length - 1)].Capacity;
    }

    /// <summary>整体等级对应的每日好感增量。</summary>
    public static int FriendshipGainAt(int overallLevel) =>
        Overall[Math.Clamp(overallLevel, 1, 5) - 1].FriendshipGain;
}
