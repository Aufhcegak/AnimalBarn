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
    public bool Unlocked { get; set; } = Unlocked;          // 整体升级后房间解锁态要刷新
    public int UpgradeLevel { get; set; } = UpgradeLevel;   // 房间升级后等级显示要刷新
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
    public int OverallLevel { get; set; } = OverallLevel;               // 整体升级后等级显示要刷新
    public bool CanUpgradeOverall { get; set; } = CanUpgradeOverall;    // 升到满级后按钮要消失
    public int OverallUpgradeCost { get; set; } = OverallUpgradeCost;
    public string OverallUpgradeUnlocks { get; set; } = OverallUpgradeUnlocks;
}
