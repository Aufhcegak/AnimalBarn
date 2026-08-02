using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;

namespace AnimalBarn;

/// <summary>产物拦截:动物产物掉地前截获,转入房间产品仓库(避免满地蛋奶)。
/// 补丁点:Utility.spawnObjectAround —— FarmAnimal.dayUpdate 里产物最终走这里掉地
/// (含 DigUpProduce 的松露)。只拦养殖场房间内的动物产物;干草/其他掉落物放行原版。
/// 原版先试 AutoGrabber((BC)165,chest),失败才走 spawnObjectAround —— 本补丁只接
/// 住"没有 AutoGrabber"的产物,相当于无限 AutoGrabber。</summary>
public static class AutoGrabberInterceptor
{
    /// <summary>由协调者接线时注入(BarnManager 实例)。与 SettlementService 解耦,
    /// 避免任务 5.1 的 SettlementService 尚未完成时编译/运行依赖。</summary>
    public static BarnManager? Barn;

    /// <summary>注册 Harmony 补丁(由协调者最后接入 ModEntry)。</summary>
    public static void Register(Harmony harmony)
    {
        harmony.Patch(
            original: AccessTools.Method(typeof(Utility), nameof(Utility.spawnObjectAround),
                new[] { typeof(Vector2), typeof(StardewValley.Object), typeof(GameLocation), typeof(bool), typeof(Action<StardewValley.Object>) }),
            prefix: new HarmonyMethod(typeof(AutoGrabberInterceptor), nameof(BeforeSpawnObjectAround))
        );
    }

    private static bool BeforeSpawnObjectAround(Vector2 tileLocation, StardewValley.Object o, GameLocation l, bool playSound, Action<StardewValley.Object> modifyObject, ref bool __result)
    {
        if (!Game1.IsMasterGame) return true;   // 多人为安全:dayUpdate 只在主机结算,补丁也只应跑在主机
        if (Barn == null) return true;          // 协调者未接线:放行原版
        if (o == null || l == null) return true;
        if (!AnimalBarnLocations.TryGetRoomType(l, out var roomType)) return true;  // 非养殖场房间放行原版
        if (l.ParentBuilding == null) return true;
        if (!IsProduceItem(o)) return true;     // 非动物产品(干草/其他掉落物)放行

        var state = Barn.GetOrCreate(l.ParentBuilding);
        var roomState = state.GetRoom(roomType);
        // 按星级分桶存储:key = "物品ID|星级"(0=普通 1=银 2=金 4=铱),蛋/奶按星级分开
        string key = ProduceKey(o.QualifiedItemId, o.Quality);
        roomState.ProduceStacks.TryGetValue(key, out int n);
        roomState.ProduceStacks[key] = n + o.Stack;
        roomState.ProduceCount += o.Stack;
        if (playSound) l.playSound("coin");
        __result = true;
        return false;  // 跳过原版掉地
    }

    /// <summary>产品分桶 key:物品ID|星级。</summary>
    public static string ProduceKey(string qualifiedId, int quality)
        => qualifiedId + "|" + quality;
    /// <summary>动物产品的 ID 集合(FarmAnimalCatalog 的 ProduceId + 大号/deluxe 变体,
    /// 原版在高好感/幸运下产出这些变体 ID)。其它模组动物的产物不在集合内,
    /// 会落回原版掉地 —— 可接受。</summary>
    private static bool IsProduceItem(StardewValley.Object o) => o.QualifiedItemId is
        "(O)176" or "(O)174" or "(O)180" or "(O)182" or "(O)184" or "(O)186"    // 鸡蛋/大鸡蛋(白/棕)
        or "(O)442" or "(O)444"   // 鸭蛋/鸭毛
        or "(O)446"               // 兔脚
        or "(O)107" or "(O)289"   // 恐龙蛋/鸵鸟蛋
        or "(O)430"               // 松露
        or "(O)436" or "(O)438"   // 羊奶/大羊奶
        or "(O)440";              // 羊毛
}
