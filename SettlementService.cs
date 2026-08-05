using StardewValley;
using StardewValley.Buildings;

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
            HappinessGain: autoPetHappiness,
            DaySeed: (int)Game1.stats.DaysPlayed);   // 确定性星级种子:同一天所有端一致
        var entityIds = room is AnimalHouse ah0 ? GetEntityIds(ah0) : null;
        var hay = ledger.SettleDay(ctx, state.HayStock, entityIds);

        // ⚠️ 实体动物喂食已在 BarnPatches.BeforeDayUpdate(原生结算前)完成(喂饱+扣草),
        // 这里【不重复扣草】—— 否则同一批实体可能双扣。
        state.HayStock = Math.Max(0, state.HayStock - hay.HayConsumed);

        // 4. 实体动物【自动收产 + 自动护理】(= 每房间一台自动抚摸机 + 无限挤奶桶/剪刀):
        //    自动收产:牛/羊/山羊的产物挂在 currentProduce 身上(原版 HarvestWithTool,
        //    要玩家用挤奶桶/剪刀手动收),猪的产物要户外拱地(DigUp)才掉—— 房间室内永远触发不了
        //    → 这批动物【永不产】!(= "9 个房子 9 个动物,其中随机一间当天没有产物" + 仓库缺页根因)
        //    自动收产 = 原版 MilkPail/Shears 的收获逻辑(MilkPail.cs:106-122):
        //    obj = ItemRegistry.Create("(O)"+currentProduce); obj.Quality = produceQuality; 清 currentProduce。
        //    产物按星级入仓(与掉地拦截器同款 key)。
        //    自动护理:好感+友情、wasAutoPet=true(判定"已被抚摸",防"关外面"衰减)。
        if (room is AnimalHouse ah)
        {
            foreach (var animal in ah.animals.Values)
            {
                // 自动收产
                if (!string.IsNullOrEmpty(animal.currentProduce.Value))
                {
                    string pid = animal.currentProduce.Value;   // 无 (O) 前缀
                    string qualified = "(O)" + pid;
                    if (qualified.StartsWith("(O)(O)")) qualified = qualified.Replace("(O)(O)", "(O)");
                    if (qualified != "(O)")
                    {
                        string key = AutoGrabberInterceptor.ProduceKey(qualified, animal.produceQuality.Value);
                        roomState.ProduceStacks.TryGetValue(key, out int n);
                        roomState.ProduceStacks[key] = n + 1;
                        roomState.ProduceCount++;
                        animal.currentProduce.Value = null;   // 清空:已入仓,不在地上了
                    }
                }

                // 自动护理
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

        // 联机:结算后立即落盘 modData(NetField 同步访客,仓库/中枢立刻可见)
        if (building != null)
            MultiplayerSync.CommitState(building);
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
    /// 此前只结算已创建房间,没进门的房间台账永远不产。
    /// 结算后立即落盘 modData(联机:NetField 同步访客,仓库/中枢立刻可见)。
    ///
    /// ⚠️ 双结算防护(2026-08-05 新增):SMAPI 的 DayStarted 事件在【所有地点 DayUpdate 之后】才触发
    /// (Game1._newDayAfterFade 协程跑完 location.DayUpdate 循环,才轮到 SMAPI 触发 DayStarted)。
    /// 而 DayUpdate 的 postfix(SettleRoom)已结算过【已创建房间】的台账 → 这里再跑 SettleDay 一次 =
    /// 台账动物被结算两次(双倍产物/双扣草)!所以这里只结算【未创建房间】(纯台账房间),
    /// 已创建房间已由 SettleRoom 结算,跳过。</summary>
    public static void SettleAllRooms(BarnSaveData state, Building? building)
    {
        foreach (var (roomKey, roomState) in state.Rooms.ToList())
        {
            if (!Enum.TryParse<RoomType>(roomKey, out var roomType)) continue;

            // ⚠️ 已创建房间:DayUpdate postfix 已结算(含实体收产)→ 跳过,防台账双倍结算
            // (building 为 null 时无房间可查 = 全部纯台账结算,测试/无建筑场景)
            if (building != null && RoomManager.GetExisting(building, roomType) != null) continue;

            // 自动抚摸机效果(按房间等级:1级=一半,满级=一倍;原版 AutoPetter 好感+8/心情+40)
            float petScale = 0.5f + 0.5f * (roomState.UpgradeLevel / 5f);
            var ctx = new SettleContext(
                FriendshipGain: (int)Math.Round(8 * petScale),
                HappinessGain: (int)Math.Round(40 * petScale),
                DaySeed: (int)Game1.stats.DaysPlayed);   // 确定性星级种子:同一天所有端一致

            var ledger = AnimalLedger.FromRoom(roomState);
            ledger.Capacity = UpgradeSystem.CapacityAt(roomType, roomState.UpgradeLevel);

            // 该房间的实体动物 id(已创建房间才有;未创建 = 空 = 全部台账结算)
            var entityIds = RoomManager.GetExistingRoomAnimals(roomType);
            var hay = ledger.SettleDay(ctx, state.HayStock, entityIds);
            state.HayStock = Math.Max(0, state.HayStock - hay.HayConsumed);

            ledger.SaveTo(roomState);
        }

        // 联机:结算后立即把状态落盘 modData(NetField 同步给访客,仓库/中枢立刻可见)
        if (building != null)
            MultiplayerSync.CommitState(building);
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
                // ⚠️ 关键:同步生产周期。实体当天产过(掉地/自动收产)→ 原生把 daysSinceLastLay 清零;
                // 台账不跟着清零 → 明天台账再产一份 = 双产(实体一份 + 台账一份)。
                // 原生 dayUpdate 每天 +1 后再判阈值,台账 SettleDay 也是每天 +1 后判 → 两路径周期一致。
                match.DaysSinceProduce = animal.daysSinceLastLay.Value;
            }
        }
    }
}
