using HarmonyLib;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Menus;

namespace AnimalBarn;

/// <summary>拆除保护:养殖场非空(台账动物/产品/干草)时禁止拆除,避免数据随建筑一起静默丢失。
/// 双保险双补丁:
///  1) CarpenterMenu.CanDemolishThis(Building) —— 拆除确认前的门禁(在 demolishLock 获取之前拦截,
///     菜单内直接红字提示,无锁生命周期问题;玩家点选养殖场时立刻被拦)。
///  2) GameLocation.destroyStructure(Building) —— 硬兜底:1.6 拆除的实际执行点
///     (buildings.Remove + performActionOnDemolition + SendBuildingDemolishedEvent 的唯一入口,
///     CarpenterMenu 的 ContinueDemolish 也走这里)。任何拆除路径(含未来功能/其他模组)非空一律拦下。
/// 空养殖场照常放行原版拆除。</summary>
public static class DemolitionGuard
{
    /// <summary>由协调者接线时注入(BarnManager 实例)。null 时本守卫完全放行原版。</summary>
    public static BarnManager? Barn;

    /// <summary>注册 Harmony 补丁(由协调者最后接入 ModEntry)。</summary>
    public static void Register(Harmony harmony)
    {
        harmony.Patch(
            original: AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.CanDemolishThis),
                new[] { typeof(Building) }),
            prefix: new HarmonyMethod(typeof(DemolitionGuard), nameof(BeforeCanDemolish))
        );
        harmony.Patch(
            original: AccessTools.Method(typeof(GameLocation), nameof(GameLocation.destroyStructure),
                new[] { typeof(Building) }),
            prefix: new HarmonyMethod(typeof(DemolitionGuard), nameof(BeforeDestroyStructure))
        );
    }

    /// <summary>拆除门禁:罗宾菜单选点养殖场时,非空则拦下并提示(锁获取之前,体验最干净)。
    /// 蓝图预览建筑(未放置、无父地点)不拦 —— 预览实例的 buildingType 也是 xiepe.AnimalBarn。</summary>
    private static bool BeforeCanDemolish(Building building, ref bool __result)
    {
        if (building == null || Barn == null) return true;
        if (building.buildingType.Value != BarnManager.BuildingId) return true;
        if (building.GetParentLocation() == null) return true;   // 蓝图预览不是真放置的
        var state = Barn.GetOrCreate(building);
        if (!IsNonEmpty(state)) return true;                     // 空养殖场可拆
        Game1.showRedMessage(BuildBlockMessage(state));
        __result = false;
        return false;                                            // 阻止原版(菜单内 destroyed=null 静默返回)
    }

    /// <summary>硬兜底:任何路径把养殖场从地图移除前,非空一律拦下(防止数据随建筑删除)。
    /// 只拦主机 —— 拆除由主机结算,客户端拦住反而会本地降级为"点不动",交给主机逻辑处理。</summary>
    private static bool BeforeDestroyStructure(Building building, ref bool __result)
    {
        if (building == null || Barn == null) return true;
        if (building.buildingType.Value != BarnManager.BuildingId) return true;
        if (!Game1.IsMasterGame) return true;
        var state = Barn.GetOrCreate(building);
        if (!IsNonEmpty(state)) return true;
        Game1.showRedMessage(BuildBlockMessage(state));
        __result = false;
        return false;                                            // 跳过原版:不移除建筑、不触发 performActionOnDemolition
    }

    /// <summary>非空判定:任一房间有动物或产品(台账计数 + 栈兜底,防计数与栈不一致的数据漂移),
    /// 或全局产品栈非空,或干草库存 &gt; 0。</summary>
    private static bool IsNonEmpty(BarnSaveData state)
    {
        foreach (var room in state.Rooms.Values)
        {
            if (room.Animals.Count > 0) return true;
            if (room.ProduceCount > 0) return true;
            foreach (var n in room.ProduceStacks.Values)
                if (n > 0) return true;
        }
        foreach (var n in state.GlobalProduceStacks.Values)
            if (n > 0) return true;
        return state.HayStock > 0;
    }

    /// <summary>汇总各明细,生成拦截提示。产品数取 max(计数, 栈和) 以免漂移时提示为 0 却拦截。</summary>
    private static string BuildBlockMessage(BarnSaveData state)
    {
        int animals = 0, produce = 0;
        foreach (var room in state.Rooms.Values)
        {
            animals += room.Animals.Count;
            produce += Math.Max(room.ProduceCount, SumStacks(room.ProduceStacks));
        }
        produce += SumStacks(state.GlobalProduceStacks);
        return $"养殖场内还有 {animals} 只动物、{produce} 件产品、{state.HayStock} 份干草,无法拆除。请先清空养殖场后再拆。";
    }

    private static int SumStacks(Dictionary<string, int> stacks)
    {
        int sum = 0;
        foreach (var n in stacks.Values)
            sum += Math.Max(0, n);
        return sum;
    }
}
