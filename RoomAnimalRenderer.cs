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

            // 随机站位:优先用原生 setRandomPosition(反编译确认存在, AnimalHouse 是 GameLocation),
            // 兜底手动挑活动区格(若原生把动物放墙里)。
            try { animal.setRandomPosition(home); } catch { animal.Position = FindOpenPosition(home); }

            // home/reload 需要 Building(反编译确认 FarmAnimal.home 是 Building,不是 AnimalHouse):
            // 用房间挂的父建筑作为 home,动物才不会"无家可归"报错。无法确定父建筑则跳过 home(仅展示)。
            if (home.ParentBuilding is Building parent)
            {
                animal.home = parent;
                animal.reload(parent);
            }
            return animal;
        }
        catch (System.Exception ex)
        {
            ModEntry.Instance.Monitor.Log($"RoomAnimalRenderer: 生成实体失败({rec.TypeKey}#{rec.Id}): {ex.Message}",
                StardewModdingAPI.LogLevel.Warn);
            return null;
        }
    }

    /// <summary>在房间活动区里随机挑一个可通行格(像素坐标)。优先动物区(中央走道两侧大片区域,
    /// x 避开中央走道列),兜底北入口下方中央走道(进房间的地方)。</summary>
    private static Vector2 FindOpenPosition(AnimalHouse ah)
    {
        var buildings = ah.map?.GetLayer("Buildings");
        int xMid = RoomMapBuilder.DoorX;
        for (int tries = 0; tries < 12; tries++)
        {
            int x = Game1.random.Next(2) == 0
                ? Game1.random.Next(1, xMid - 1)     // 左动物区 x=1..xMid-2
                : Game1.random.Next(xMid + 2, RoomMapBuilder.Width - 1);  // 右动物区 x=xMid+2..13
            int y = Game1.random.Next(4, RoomMapBuilder.Height - 2);
            if (buildings == null || buildings.Tiles[x, y] == null)
                return new Vector2(x * 64f, y * 64f);
        }
        // 兜底:北入口下方中央走道 (DoorX, 4)
        return new Vector2(xMid * 64f, 4 * 64f);
    }
}
