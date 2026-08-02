namespace AnimalBarn;

/// <summary>从真实状态构建中枢菜单快照(菜单打开时调用一次,不查实时)。</summary>
public static class HubSnapshotBuilder
{
    public static HubSnapshot Build(BarnSaveData state)
    {
        var rooms = new List<RoomSnapshot>();
        foreach (var def in RoomDefinitions.All)
        {
            var rs = state.HasRoom(def.Room) ? state.GetRoom(def.Room) : null;
            bool unlocked = UpgradeSystem.IsUnlocked(def.Room, state.OverallLevel);
            rooms.Add(new RoomSnapshot(
                Room: def.Room,
                DisplayName: def.DisplayName,
                Unlocked: unlocked,
                Count: rs?.Animals.Count ?? 0,
                Capacity: UpgradeSystem.CapacityAt(def.Room, rs?.UpgradeLevel ?? 0),
                UpgradeLevel: rs?.UpgradeLevel ?? 0,
                ProduceCount: rs?.ProduceCount ?? 0,
                ProduceStacks: rs?.ProduceStacks
            ));
        }

        bool canUpgrade = state.OverallLevel < 5;
        var next = canUpgrade ? UpgradeSystem.Overall[state.OverallLevel] : null;  // Overall[level] = 升到 level+1 的档
        return new HubSnapshot(
            OverallLevel: state.OverallLevel,
            HayStock: state.HayStock,
            ProduceCount: state.ProduceCount,
            Rooms: rooms,
            CanUpgradeOverall: canUpgrade,
            OverallUpgradeCost: next?.Cost ?? 0,
            OverallUpgradeUnlocks: next?.Unlocks ?? ""
        );
    }
}
