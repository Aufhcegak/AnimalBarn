using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Buildings;

namespace AnimalBarn;

/// <summary>房间实体动物渲染:把台账(LedgerAnimal)前 <see cref="AnimalLedger.MaxVisible"/> 只
/// 实例化成真正的 FarmAnimal 放进房间,让玩家进门就能看到动物走动(此前只写台账、从不生成实体 → 看不见动物)。
/// 实体只是"渲染层",权威数据仍在台账;每日结算(SettlementService.SyncLedgerFromEntities)把实体最终值
/// 同步回台账,这里只做"台账 → 实体"的展示同步,不进存档(房间不进存档,实体随房间重建)。</summary>
public static class RoomAnimalRenderer
{
    /// <summary>进入房间时同步:确保该房间的可见实体与台账一致(缺则补生成)。
    /// 由 LobbyDoors 在 warp 进房间前调用,保证玩家看到的房间里有动物。</summary>
    public static void EnsureVisibleOnEnter(GameLocation roomLocation)
    {
        if (roomLocation is not AnimalHouse ah) return;
        if (!AnimalBarnLocations.TryGetRoomType(roomLocation, out var houseRoom)) return;
        var building = roomLocation.ParentBuilding;
        var barn = SettlementService.Current;
        if (building == null || barn == null) return;
        var roomState = barn.GetOrCreate(building).GetRoom(houseRoom);
        SyncEntities(ah, houseRoom, roomState);
        // 进门即归位:把已存在的实体(含上一轮残留位置的)全部拉回动物区,杜绝过道刷动物。
        RepositionAnimalsToPens(ah);
        // ⚠️ 补设 currentLocation + homeInterior:旧存档动物是旧代码生成的(都没设),
        // 游戏判定"在外面"→ F1 显示"被关在外面"+心情减半。每次进房间补设 → 游戏认为动物在屋里。
        // 同时把心情拉到高位(自动抚摸机:动物在屋里被照顾,心情好)。
        foreach (var animal in ah.animals.Values)
        {
            animal.currentLocation = ah;
            animal.homeInterior = ah;   // IsHome=true → 不判"被关在外面"
            animal.wasAutoPet.Value = true;
            if (animal.happiness.Value < 200)
                animal.happiness.Value = 200;
        }
    }

    /// <summary>按建筑 + 房间同步(购买后调用):找到该建筑的该房间(若已创建)并补齐可见实体。
    /// 房间还没创建(玩家从没进去过)时跳过 —— 等首次进门 EnsureVisibleOnEnter 再生成,无副作用。</summary>
    public static void SyncRoom(Building building, RoomType houseRoom, BarnSaveData.RoomSaveData roomState)
    {
        var room = RoomManager.GetExisting(building, houseRoom);
        if (room is AnimalHouse ah)
            SyncEntities(ah, houseRoom, roomState);
    }

    /// <summary>核心:把台账前 MaxVisible 只补成实体(已存在的跳过)。只增不减 —— 实体数量恒等于
    /// min(台账数, MaxVisible),买动物只会让它变多;卖动物本 mod 暂不支持,无需减。</summary>
    private static void SyncEntities(AnimalHouse ah, RoomType houseRoom, BarnSaveData.RoomSaveData roomState)
    {
        // 已存在的实体 id(去重,防重复生成)
        var present = new HashSet<long>();
        foreach (var a in ah.animals.Values)
            present.Add(a.myID.Value);

        var ledger = AnimalLedger.FromRoom(roomState);
        foreach (var rec in ledger.GetVisible())
        {
            if (present.Contains(rec.Id)) continue;
            var animal = CreateEntity(rec, ah);
            if (animal != null)
                ah.animals.TryAdd(animal.myID.Value, animal);
        }
    }

    /// <summary>由台账记录实例化一只 FarmAnimal 并放进房间随机空位。</summary>
    private static FarmAnimal? CreateEntity(LedgerAnimal rec, AnimalHouse home)
    {
        try
        {
            var animal = new FarmAnimal(rec.TypeKey, rec.Id, rec.OwnerId);
            animal.myID.Value = rec.Id;
            animal.ownerID.Value = rec.OwnerId;
            animal.age.Value = rec.AgeDays;
            animal.fullness.Value = (byte)Math.Clamp(rec.Fullness, 0, 255);
            animal.friendshipTowardFarmer.Value = rec.Friendship;
            animal.happiness.Value = (byte)Math.Clamp(rec.Happiness, 0, 255);
            animal.daysSinceLastLay.Value = rec.DaysSinceProduce;
            animal.wasAutoPet.Value = true;   // 自动护理:防止"没摸过"的好感/心情衰减
            // 买来即可产:成年动物把 daysSinceLastLay 设到阈值(原版玛妮卖的是能产的)
            if (rec.IsAdult && rec.DaysSinceProduce >= FarmAnimalCatalog.Get(rec.Room).DaysToProduce)
                animal.daysSinceLastLay.Value = Math.Max(1, FarmAnimalCatalog.Get(rec.Room).DaysToProduce);

            // 站位:强制放在【动物区】(左右栅栏和墙之间),绝不在中央走道(人走)。
            // 不依赖 setRandomPosition(可能把动物放走道/门口),直接用 FindOpenPosition 钉在动物区。
            animal.Position = FindOpenPosition(home);

            // home/reload 需要 Building(反编译确认 FarmAnimal.home 是 Building,不是 AnimalHouse):
            // 用房间挂的父建筑作为 home,动物才不会"无家可归"报错。无法确定父建筑则跳过 home(仅展示)。
            if (home.ParentBuilding is Building parent)
            {
                animal.home = parent;
                animal.reload(parent);
            }

            // ⚠️ 关键:设 currentLocation = 房间(AnimalHouse)。否则游戏判定动物"在外面"
            // (currentLocation 不是室内) → F1 显示"被关在外面过夜很生气"!
            animal.currentLocation = home;

            // ⚠️ 关键2:设 homeInterior = 房间。原版 IsHome = homeInterior.animals.ContainsKey(myID)
            // → homeInterior 指向房间后 IsHome=true → 结算不判"被关在外面"(moodMessage=6 + 心情减半)!
            // (源码 FarmAnimal.cs: homeInterior 是 netHomeInterior.Value 可设)
            animal.homeInterior = home;

            return animal;
        }
        catch (System.Exception ex)
        {
            ModEntry.Instance.Monitor.Log($"RoomAnimalRenderer: 生成实体失败({rec.TypeKey}#{rec.Id}): {ex.Message}",
                StardewModdingAPI.LogLevel.Warn);
            return null;
        }
    }

    /// <summary>把房间里所有实体动物拉回动物区(左右栅栏和墙之间,不在中央走道)。
    /// 原生 FarmAnimal 每天会随机走动,可能逛到走道 → 每天结算时归位。</summary>
    public static void RepositionAnimalsToPens(AnimalHouse ah)
    {
        foreach (var animal in ah.animals.Values)
        {
            var tile = animal.TilePoint;   // Character.TilePoint:当前格
            // 是否在中央走道区(过道 x=6..8) → 拉回动物区
            if (tile.X >= 6 && tile.X <= 8)
            {
                animal.Position = FindOpenPosition(ah);
            }
        }
    }

    /// <summary>在房间【动物区】里随机挑一个可通行格(像素坐标)。动物区 = 左右栅栏和墙之间
    /// (左 x=1..4 / 右 x=10..13,y=4..8),绝不在中央走道(x=6..8,人走)里生成。
    /// 兜底固定放动物区内 (3,6),保证动物一定在栅栏里。</summary>
    private static Vector2 FindOpenPosition(AnimalHouse ah)
    {
        var buildings = ah.map?.GetLayer("Buildings");
        for (int tries = 0; tries < 12; tries++)
        {
            int x = Game1.random.Next(2) == 0
                ? Game1.random.Next(1, 5)                    // 左动物区 x=1..4
                : Game1.random.Next(10, 14);                 // 右动物区 x=10..13
            int y = Game1.random.Next(4, 9);
            // 双重保险:避开中央走道 3 列(x=6..8) 且 无 Buildings 阻挡
            if (x >= 6 && x <= 8) continue;
            if (buildings == null || buildings.Tiles[x, y] == null)
                return new Vector2(x * 64f, y * 64f);
        }
        // 兜底:左动物区中间 (3,6),一定在栅栏里
        return new Vector2(3 * 64f, 6 * 64f);
    }
}
