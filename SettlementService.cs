using StardewValley;

namespace AnimalBarn;

/// <summary>每日结算编排(房间 DayUpdate 调用):实体动物走原生结算(已在 base.DayUpdate 完成),
/// 台账动物走纯逻辑结算,产品合并入房间存档,干草统一从全局库存扣。
/// 结算顺序:台账加载 → 实体↔台账同步 → 台账结算(扣干草) → 实体自动护理 → 台账写回。</summary>
public static class SettlementService
{
    /// <summary>BarnManager 实例。由 ModEntry 注册时注入(SettlementService.Current = new BarnManager();),
    /// 不依赖 ModEntry.Instance.Barn(尚未接线)。房主进程(IsMasterGame)在 DayUpdate 里使用。</summary>
    public static BarnManager? Current;

    /// <summary>结算一个动物房间。实体动物已在 base.DayUpdate 完成原生结算(喂食/产/好感/衰减);
    /// 这里只处理台账部分与全局干草扣减,并把实体最终值同步回台账(下次结算的基线)。</summary>
    public static void SettleRoom(AnimalBarnRoom room)
    {
        if (room.RoomType == null || Current == null) return;
        var barn = Current;
        var building = room.ParentBuilding;
        if (building == null) return;  // 大堂/未挂建筑的房间不结算
        var state = barn.GetOrCreate(building);
        var roomState = state.GetRoom(room.RoomType.Value);

        // 1. 台账加载(容量按房间等级)
        var ledger = AnimalLedger.FromRoom(roomState);
        ledger.Capacity = UpgradeSystem.CapacityAt(room.RoomType.Value, roomState.UpgradeLevel);

        // 2. 实体↔台账同步(实体结算已由 base.DayUpdate 完成,把实体最终值写回台账)
        SyncLedgerFromEntities(room, ledger);

        // 3. 台账结算(干草从全局库存扣;实体动物的干草由原生 AutoFeed 从干草槽消耗,两套独立)
        var ctx = new SettleContext(
            FriendshipGain: UpgradeSystem.FriendshipGainAt(state.OverallLevel),
            HappinessGain: 20);
        var hay = ledger.SettleDay(ctx, state.HayStock);
        state.HayStock = Math.Max(0, state.HayStock - hay.HayConsumed);

        // 4. 实体动物自动护理:好感按整体等级补增 + wasAutoPet 标记,防止原生结算里
        //    "没被摸过" 的衰减(farmAnimal.dayUpdate: !wasPet && !wasAutoPet → 好感-1%~-9%、心情-50)
        foreach (var animal in room.animals.Values)
        {
            animal.friendshipTowardFarmer.Value = Math.Min(1000,
                animal.friendshipTowardFarmer.Value + ctx.FriendshipGain);
            animal.wasAutoPet.Value = true;   // 视为已自动抚摸,防原生衰减
        }

        // 5. 台账写回
        ledger.SaveTo(roomState);
    }

    /// <summary>把实体动物的最终值(结算后)同步回台账记录;实体不在台账(如直接购买)则加入。
    /// 实体是台账前 30 只的渲染;台账中其余动物没有实体,状态只由台账自持。</summary>
    private static void SyncLedgerFromEntities(AnimalBarnRoom room, AnimalLedger ledger)
    {
        foreach (var animal in room.animals.Values)
        {
            var match = ledger.Animals.FirstOrDefault(a => a.Id == animal.myID.Value);
            if (match == null)
            {
                // 实体不在台账(直接买的)→ 加入台账
                match = new LedgerAnimal
                {
                    Id = animal.myID.Value,
                    Room = room.RoomType!.Value,
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
