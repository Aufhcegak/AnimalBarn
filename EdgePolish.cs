using StardewValley;
using StardewValley.Buildings;

namespace AnimalBarn;

/// <summary>防御性边界处理工具箱(Task 6.2):空房间/满上限/无存档/多玩家/缺建筑等异常状态下的
/// 兜底,防崩溃。全部为静态纯防御函数,不持有状态、不互相依赖,可被任意路径安全调用;
/// 未接线的调用方(如未调用 CanModifyState 的客机菜单)也不会因此类本身崩溃。
/// 边界审计结论见各函数注释与代码库其余文件的既有保护。</summary>
public static class EdgePolish
{
    // ── 多玩家:主机权威 ─────────────────────────────────────────────────────────

    /// <summary>只有主机能修改养殖场状态(结算/购买/升级/取产品)。
    /// 理由:状态存 Building.ModData,由游戏在联机时从主机单向同步到客机;
    /// 客机若直接改本地 modData 副本,会被主机覆盖成陈旧值(仅"看起来成功",实为丢档/丢钱),
    /// 且客机的内存缓存(_states)与主机永久失步。因此客机一律只读。
    /// 用法:HubMenu 等菜单在购买/升级/存取动作前调用,客机应拒绝并提示。
    /// 注:DayUpdate 结算已由 AnimalBarnRoom.DayUpdate 的 IsMasterGame 分支隔离,不受本函数影响。</summary>
    public static bool CanModifyState() => Game1.IsMasterGame;

    // ── 状态安全获取 ───────────────────────────────────────────────────────────

    /// <summary>安全拿建筑状态:null 输入或 GetOrCreate 内部异常(如损坏存档在构建时抛错)
    /// 都给全新默认状态,绝不让调用方撞 null。BarnManager.GetOrCreate 内部已用
    /// SaveSerializer.Load(building) ?? new BarnSaveData() 兜底损坏 JSON,本函数是最后防线。</summary>
    public static BarnSaveData SafeGetState(BarnManager? barn, Building? building)
    {
        if (barn == null || building == null) return new BarnSaveData();
        try { return barn.GetOrCreate(building); }
        catch { return new BarnSaveData(); }
    }

    /// <summary>安全拿房间状态:null 状态或 GetRoom 异常给全新默认房间数据。
    /// BarnSaveData.GetRoom 内部已对缺失房间做惰性创建,本函数只防 null 输入与意外异常。</summary>
    public static BarnSaveData.RoomSaveData SafeGetRoom(BarnSaveData? state, RoomType room)
    {
        if (state == null) return new BarnSaveData.RoomSaveData();
        try { return state.GetRoom(room); }
        catch { return new BarnSaveData.RoomSaveData(); }
    }

    // ── 容量保护 ───────────────────────────────────────────────────────────────

    /// <summary>尝试加入动物:房间已满(Capacity &lt;= 0 或 Animals.Count &gt;= Capacity 都视为满)
    /// 则拒绝并可选提示。备注:AnimalLedger.TryAdd 本身返回 false 不抛异常,本函数补一层
    /// 用户提示,并把"容量 0 的异常房间"也挡在门外。</summary>
    public static bool TryAddWithGuard(AnimalLedger? ledger, LedgerAnimal animal, bool showMsg)
    {
        if (ledger == null)
        {
            if (showMsg) Game1.showRedMessage("动物房间数据异常,无法放入动物。");
            return false;
        }
        if (ledger.Capacity <= 0 || ledger.IsFull)
        {
            if (showMsg) Game1.showRedMessage("该房间已满,无法继续放入动物。");
            return false;
        }
        return ledger.TryAdd(animal);
    }

    // ── 房间出口 warp 保护 ─────────────────────────────────────────────────────

    /// <summary>安全取房间出口 warp(防 warps 为空导致索引越界)。
    /// 注:RoomMapBuilder 已给每个房间地图写 "Warp" 属性、游戏 updateWarps() 负责填充,
    /// 正常不会为空;本函数供房间/门系统(3.2)在极端情况下(地图损坏、warp 被清除)兜底。</summary>
    public static Warp? SafeGetExitWarp(GameLocation? loc)
    {
        if (loc == null || loc.warps == null || loc.warps.Count == 0) return null;
        return loc.warps[0];
    }

    // ── 数值保护 ───────────────────────────────────────────────────────────────

    /// <summary>非负钳制:负数归零。用于干草/产品/好感等所有不允许为负的计数入口。</summary>
    public static int ClampNonNegative(int v) => Math.Max(0, v);
}
