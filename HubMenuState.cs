namespace AnimalBarn;

/// <summary>单个动物房间的状态快照(可变字段:菜单操作后刷新 UI)。</summary>
public record RoomSnapshot(
    RoomType Room,
    string DisplayName,
    bool Unlocked,
    int Count,
    int Capacity,
    int UpgradeLevel,
    int ProduceCount,
    Dictionary<string, int>? ProduceStacks = null  // 房间产品栈(QualifiedId -> 数量);可选,测试可不传
)
{
    public int Count { get; set; } = Count;
    public int Capacity { get; set; } = Capacity;
    public int ProduceCount { get; set; } = ProduceCount;
}

/// <summary>中枢菜单的状态快照(可变字段:菜单操作后刷新 UI)。</summary>
public record HubSnapshot(
    int OverallLevel,
    int HayStock,
    int ProduceCount,
    List<RoomSnapshot> Rooms,
    bool CanUpgradeOverall,
    int OverallUpgradeCost,
    string OverallUpgradeUnlocks
)
{
    public int HayStock { get; set; } = HayStock;
    public int ProduceCount { get; set; } = ProduceCount;
}
