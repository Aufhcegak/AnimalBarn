using StardewValley;

namespace AnimalBarn;

/// <summary>每日结算编排(AnimalHouse.DayUpdate Harmony 补丁调用):实体动物走原生结算(已在 base.DayUpdate 完成),
/// 台账动物走纯逻辑结算,产品合并入房间存档,干草统一从全局库存扣。
/// 结算顺序:台账加载 → 实体↔台账同步 → 台账结算(扣干草) → 实体自动护理 → 台账写回。
/// 房间用原版 AnimalHouse(存档序列化安全),靠地图属性识别(AnimalBarnLocations.TryGetRoomType)。</summary>
public static class SettlementService
{
    /// <summary>BarnManager 实例。由 ModEntry 注册时注入(SettlementService.Current = new BarnManager();),
    /// 不依赖 ModEntry.Instance.Barn(尚未接线)。房主进程(IsMasterGame)在 DayUpdate 里使用。</summary>
    public static BarnManager? Current;

    /// <summary>结算一个动物房间(由 AnimalHouse.DayUpdate 的 Harmony postfix 调用)。
    /// 实体动物已在 base.DayUpdate 完成原生结算(喂食/产/好感/衰减);
    /// 这里只处理台账部分与全局干草扣减,并把实体最终值同步回台账(下次结算的基线)。</summary>
    public static void SettleRoom(GameLocation room)
    {
        if (room == null || Current == null) return;
        if (!AnimalBarnLocations.TryGetRoomType(room, out var roomType)) return;  // 非养殖场房间不结算
        var barn = Current;
        var building = room.ParentBuilding;
        if (building == null) return;  // 未挂建筑的房间不结算
        var state = barn.GetOrCreate(building);
        var roomState = state.GetRoom(roomType);

        // 1. 台账加载(容量按房间等级)
        var ledger = AnimalLedger.FromRoom(roomState);
        ledger.Capacity = UpgradeSystem.CapacityAt(roomType, roomState.UpgradeLevel);

        // 2. 实体↔台账同步(实体结算已由 base.DayUpdate 完成,把实体最终值写回台账)
        SyncLedgerFromEntities(room, roomType, ledger);

        // 3. 台账结算(干草从全局库存扣)。
        //    实体动物由原生 AutoFeed 喂食【但依赖筒仓】—— 玩家没筒仓 → 实体永远饿着不产!
        //    修复:实体动物也走全局干草系统(不依赖筒仓):扣除全局 HayStock、满饱食 → 第二天原生产蛋。
        //    台账动物照常 SettleDay(排除实体,防双结算)。
        //    自动抚摸机效果:每房间相当于装了自动抚摸机,1级房间=一半效果、满级房间=一倍效果。
        //    原版自动抚摸机(pet() 源码 is_auto_pet):好感+8/天,心情+30+HappinessDrain(鸡=40)。
        float petScale = 0.5f + 0.5f * (roomState.UpgradeLevel / 5f);   // 0级=0.5, 5级=1.0
        int autoPetFriendship = (int)Math.Round(8 * petScale);
        int autoPetHappiness = (int)Math.Round(40 * petScale);
        var ctx = new SettleContext(
            FriendshipGain: autoPetFriendship,
            HappinessGain: autoPetHappiness);
        var entityIds = room is AnimalHouse ah0 ? GetEntityIds(ah0) : null;
        var hay = ledger.SettleDay(ctx, state.HayStock, entityIds);

        // ⚠️ 实体动物喂食已在 BarnPatches.BeforeDayUpdate(原生结算前)完成(喂饱+扣草),
        // 这里【不重复扣草】—— 否则同一批实体可能双扣。
        state.HayStock = Math.Max(0, state.HayStock - hay.HayConsumed);

        // 4. 实体动物自动护理 = 【每个房间装了一台自动抚摸机】:
        //    1级房间 = 自动抚摸机一半效果,满级房间 = 一倍效果(AutoPetter 原版:好感+15、心情+5/天)。
        //    wasAutoPet=true → 动物判定"已被抚摸"(在屋里被照顾),不会掉心情/好感。
        if (room is AnimalHouse ah)
        {
            foreach (var animal in ah.animals.Values)
            {
                animal.friendshipTowardFarmer.Value = Math.Min(1000,
                    animal.friendshipTowardFarmer.Value + ctx.FriendshipGain);
                animal.happiness.Value = (byte)Math.Min(255,
                    animal.happiness.Value + ctx.HappinessGain);
                animal.wasAutoPet.Value = true;   // 判定"在屋里被自动抚摸",防"关外面"的心情/好感衰减
            }

            // 动物归位:每天把实体动物拉回【动物区】(左右栅栏和墙之间),防止它们逛到中央走道(人走)。
            RoomAnimalRenderer.RepositionAnimalsToPens(ah);
        }

        // 5. 台账写回
        ledger.SaveTo(roomState);
    }

    /// <summary>当前房间的实体动物 id 集合(结算时传给台账,跳过这批动物)。</summary>
    private static HashSet<long> GetEntityIds(AnimalHouse ah)
    {
        var ids = new HashSet<long>();
        foreach (var animal in ah.animals.Values)
            ids.Add(animal.myID.Value);
        return ids;
    }

    /// <summary>【建筑级结算】:某建筑所有房间的台账每日结算(不管房间地图创建没)。
    /// ⚠️ 只有已创建的房间才有实体动物(原生 DayUpdate 已结算它们 → 台账跳过)。
    /// 未创建房间 = 纯台账 → 全部结算。这是"买 100 只鸡只有 3 个蛋"的修复:
    /// 此前只结算已创建房间,没进门的房间台账永远不产。</summary>
    public static void SettleAllRooms(BarnSaveData state)
    {
        foreach (var (roomKey, roomState) in state.Rooms.ToList())
        {
            if (!Enum.TryParse<RoomType>(roomKey, out var roomType)) continue;

            // 自动抚摸机效果(按房间等级:1级=一半,满级=一倍;原版 AutoPetter 好感+8/心情+40)
            float petScale = 0.5f + 0.5f * (roomState.UpgradeLevel / 5f);
            var ctx = new SettleContext(
                FriendshipGain: (int)Math.Round(8 * petScale),
                HappinessGain: (int)Math.Round(40 * petScale));

            var ledger = AnimalLedger.FromRoom(roomState);
            ledger.Capacity = UpgradeSystem.CapacityAt(roomType, roomState.UpgradeLevel);

            // 该房间的实体动物 id(已创建房间才有;未创建 = 空 = 全部台账结算)
            var entityIds = RoomManager.GetExistingRoomAnimals(roomType);
            var hay = ledger.SettleDay(ctx, state.HayStock, entityIds);
            state.HayStock = Math.Max(0, state.HayStock - hay.HayConsumed);

            ledger.SaveTo(roomState);
        }
    }

    /// <summary>把实体动物的最终值(结算后)同步回台账记录;实体不在台账(如直接购买)则加入。
    /// 实体是台账前 30 只的渲染;台账中其余动物没有实体,状态只由台账自持。</summary>
    private static void SyncLedgerFromEntities(GameLocation room, RoomType roomType, AnimalLedger ledger)
    {
        if (room is not AnimalHouse ah) return;
        foreach (var animal in ah.animals.Values)
        {
            var match = ledger.Animals.FirstOrDefault(a => a.Id == animal.myID.Value);
            if (match == null)
            {
                // 实体不在台账(直接买的)→ 加入台账
                match = new LedgerAnimal
                {
                    Id = animal.myID.Value,
                    Room = roomType,
                    TypeKey = animal.type.Value,
                    AgeDays = animal.age.Value,
                    Friendship = animal.friendshipTowardFarmer.Value,
                    Happiness = animal.happiness.Value,
                    Fullness = animal.fullness.Value,
                    DaysSinceProduce = animal.daysSinceLastLay.Value,
                    OwnerId = animal.ownerID.Value,
                };
                ledger.Animals.Add(match);
            }
            else
            {
                match.Friendship = animal.friendshipTowardFarmer.Value;
                match.Happiness = animal.happiness.Value;
                match.Fullness = animal.fullness.Value;
                match.AgeDays = animal.age.Value;
            }
        }
    }
}
