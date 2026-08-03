using HarmonyLib;
using StardewValley;
using xTile.Dimensions;

namespace AnimalBarn;

/// <summary>原版类型的低频 Harmony 补丁(存档序列化安全方案):
/// 1) AnimalHouse.DayUpdate postfix —— 每日结算:养殖场房间跑台账结算(实体已由 base 结算)。
/// 2) GameLocation.checkAction prefix —— 大堂中枢操作台点击打开中枢菜单。
/// 两个都是低频点(每天一次/玩家点击),无性能风险。</summary>
public static class BarnPatches
{
    public static void Register(Harmony harmony)
    {
        harmony.Patch(
            original: AccessTools.Method(typeof(AnimalHouse), nameof(AnimalHouse.DayUpdate),
                new[] { typeof(int) }),
            prefix: new HarmonyMethod(typeof(BarnPatches), nameof(BeforeDayUpdate)),   // ⚠️ 关键:原生结算前设 wasAutoPet,否则原生判定"没被抚摸"扣心情
            postfix: new HarmonyMethod(typeof(BarnPatches), nameof(AfterDayUpdate))
        );
        harmony.Patch(
            original: AccessTools.Method(typeof(GameLocation), nameof(GameLocation.checkAction),
                new[] { typeof(Location), typeof(xTile.Dimensions.Rectangle), typeof(Farmer) }),
            prefix: new HarmonyMethod(typeof(BarnPatches), nameof(BeforeCheckAction))
        );
        harmony.Patch(
            original: AccessTools.Method(typeof(StardewValley.FarmAnimal), "getMoodMessage"),
            prefix: new HarmonyMethod(typeof(BarnPatches), nameof(BeforeGetMoodMessage))   // 动物心情提示:永远"开心"
        );
    }

    /// <summary>动物心情提示 prefix:养殖场动物【永远显示开心】(自动抚摸机效果)。
    /// 原版 getMoodMessage 按序判定:新家→被关外面过夜→被狗扰→心情<30伤心→心情>=200开心。
    /// 我们的动物在屋里被自动抚摸机照顾,永远不该命中"关外面/伤心"。
    /// 返回 false 跳过原版,直接给"开心"文案(不加载字符串,避免 API 风险)。</summary>
    private static bool BeforeGetMoodMessage(StardewValley.FarmAnimal __instance, ref string __result)
    {
        if (__instance?.home == null) return true;
        if (__instance.home.buildingType.Value != BarnManager.BuildingId) return true;   // 非养殖场动物
        __result = "看起来很快乐,它很喜欢它的新家";   // 简化:永远开心(自动抚摸机)
        return false;
    }

    /// <summary>【关键】AnimalHouse.DayUpdate 的 prefix:原生结算【之前】处理实体动物。
    /// 原版 dayUpdate: 动物没被抚摸(wasPet=false 且 wasAutoPet=false) → 心情大减、好感衰减、
    /// 判定"被关在外面很生气";饿了(fullness<=0)也大减心情。我们每个房间有自动抚摸机
    /// + 全局干草喂食 → 原生前标记已抚摸 + 喂饱 → 原生不扣心情。
    /// (postfix 设太晚:原生的心情衰减已发生,来不及。)</summary>
    private static void BeforeDayUpdate(AnimalHouse __instance, int dayOfMonth)
    {
        if (!Game1.IsMasterGame) return;
        if (!AnimalBarnLocations.TryGetRoomType(__instance, out var roomType)) return;   // 非养殖场房间

        var building = __instance.ParentBuilding;
        if (building == null || SettlementService.Current == null) return;
        var state = SettlementService.Current.GetOrCreate(building);
        var roomState = state.GetRoom(roomType);

        foreach (var animal in __instance.animals.Values)
        {
            animal.wasAutoPet.Value = true;   // 自动抚摸机:原生判定"已被抚摸"

            // 心情维持高位:动物在屋里被照顾,心情拉到 200+(开心状态,原生不再扣)
            animal.happiness.Value = (byte)Math.Max(animal.happiness.Value, 200);

            // 喂饱(全局干草,不依赖筒仓):⚠️ 原版判"饿"是 fullness<200(不是0)!
            // 没喂到 200 → 原版每天 心情-100/好感-20(FarmAnimal.cs dayUpdate 源码)。
            // 必须喂到 200+ 才不判饿。
            if (animal.isAdult() && animal.fullness.Value < 200 && state.HayStock > 0)
            {
                animal.fullness.Value = 255;   // 喂饱(255 > 200,原版不判饿)
                state.HayStock--;
            }
        }
    }

    /// <summary>结算 postfix:AnimalHouse.DayUpdate 跑完实体结算 + AutoFeed 后,追加台账结算。
    /// 只对养殖场房间生效(地图属性标记);大堂/原版小屋/其他模组地点放行。</summary>
    private static void AfterDayUpdate(AnimalHouse __instance, int dayOfMonth)
    {
        if (!Game1.IsMasterGame) return;   // 只有主机结算
        if (!AnimalBarnLocations.TryGetRoomType(__instance, out _)) return;  // 非养殖场房间
        SettlementService.SettleRoom(__instance);
    }

    /// <summary>【建筑级每日结算】:DayStarted 时对所有养殖场建筑的所有房间台账结算。
    /// ⚠️ 此前只结算"已创建的房间"(玩家进过门的)→ 没进门的房间台账永远不产蛋不吃草
    /// (买 100 只鸡只有 3 个蛋 = 只结算了进过门房间的 3 只实体!)。本方法补齐:
    /// 所有房间台账每天结算,不管玩家进没进门。</summary>
    public static void OnDayStarted(object? sender, StardewModdingAPI.Events.DayStartedEventArgs e)
    {
        if (!Game1.IsMasterGame || SettlementService.Current == null) return;
        var barn = SettlementService.Current;
        foreach (var building in barn.FindAllBarns())
        {
            var state = barn.GetOrCreate(building);
            SettlementService.SettleAllRooms(state);
        }
    }

    /// <summary>checkAction prefix:大堂中枢台 → 中枢菜单;大堂北墙动物房门 → 房间选择菜单。
    /// 返回 false 跳过原版 checkAction。非养殖场/非交互点放行原版。</summary>
    private static bool BeforeCheckAction(GameLocation __instance, Location tileLocation, Farmer who)
    {
        if (who == null || !who.IsLocalPlayer) return true;

        // 大堂:动物房门(北墙中央)→ 房间选择菜单(不再进小房间,右键门直接选房间进)
        if (AnimalBarnLocations.IsLobby(__instance))
        {
            if (LobbyMapBuilder.IsHallDoorTile(tileLocation.X, tileLocation.Y))
            {
                var building = __instance.ParentBuilding;
                if (building == null || ModEntry.Instance.Barn == null) return true;
                Game1.activeClickableMenu = new RoomSelectMenu(__instance, building, ModEntry.Instance.Barn);
                Game1.playSound("bigSelect");
                return false;
            }
            if (!LobbyMapBuilder.IsHubTile(tileLocation.X, tileLocation.Y)) return true;  // 非中枢台

            var barn = ModEntry.Instance.Barn;
            var lobbyBuilding = __instance.ParentBuilding;
            if (barn == null || lobbyBuilding == null) return true;
            var state = barn.GetOrCreate(lobbyBuilding);
            var snapshot = HubSnapshotBuilder.Build(state);
            Game1.activeClickableMenu = new HubMenu(snapshot, barn, lobbyBuilding);
            Game1.playSound("bigSelect");
            return false;   // 已处理,跳过原版
        }
        return true;
    }
}
