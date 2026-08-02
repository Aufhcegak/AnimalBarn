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
            postfix: new HarmonyMethod(typeof(BarnPatches), nameof(AfterDayUpdate))
        );
        harmony.Patch(
            original: AccessTools.Method(typeof(GameLocation), nameof(GameLocation.checkAction),
                new[] { typeof(Location), typeof(xTile.Dimensions.Rectangle), typeof(Farmer) }),
            prefix: new HarmonyMethod(typeof(BarnPatches), nameof(BeforeCheckAction))
        );
    }

    /// <summary>结算 postfix:AnimalHouse.DayUpdate 跑完实体结算 + AutoFeed 后,追加台账结算。
    /// 只对养殖场房间生效(地图属性标记);大堂/原版小屋/其他模组地点放行。</summary>
    private static void AfterDayUpdate(AnimalHouse __instance, int dayOfMonth)
    {
        if (!Game1.IsMasterGame) return;   // 只有主机结算
        if (!AnimalBarnLocations.TryGetRoomType(__instance, out _)) return;  // 非养殖场房间
        SettlementService.SettleRoom(__instance);
    }

    /// <summary>中枢操作台 prefix:玩家在大堂点击中枢台 tile → 打开中枢菜单;
    /// 门厅终端 tile → 打开房间选择菜单。返回 false 跳过原版 checkAction。
    /// 非中枢台/非门厅终端/非养殖场放行原版。</summary>
    private static bool BeforeCheckAction(GameLocation __instance, Location tileLocation, Farmer who)
    {
        if (who == null || !who.IsLocalPlayer) return true;

        // 门厅:终端 → 房间选择菜单
        if (AnimalBarnLocations.IsHall(__instance))
        {
            if (!HallMapBuilder.IsTerminalTile(tileLocation.X, tileLocation.Y)) return true;
            var building = __instance.ParentBuilding;
            if (building == null || ModEntry.Instance.Barn == null) return true;
            Game1.activeClickableMenu = new RoomSelectMenu(__instance, building, ModEntry.Instance.Barn);
            Game1.playSound("bigSelect");
            return false;
        }

        if (!AnimalBarnLocations.IsLobby(__instance)) return true;   // 只在大堂
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
}
