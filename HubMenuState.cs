namespace AnimalBarn;

/// <summary>单个动物房间的状态快照。</summary>
public record RoomSnapshot(
    RoomType Room,
    string DisplayName,
    bool Unlocked,
    int Count,
    int Capacity,
    int UpgradeLevel,
    int ProduceCount
);

/// <summary>中枢菜单的状态快照(打开时由 BarnManager 生成,菜单本身不查实时状态)。</summary>
public record HubSnapshot(
    int OverallLevel,
    int HayStock,
    int ProduceCount,
    List<RoomSnapshot> Rooms,
    bool CanUpgradeOverall,
    int OverallUpgradeCost,
    string OverallUpgradeUnlocks
);
