using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Buildings;

namespace AnimalBarn;

/// <summary>大堂门系统:大堂北墙中央一扇【动物房门】,右键门 → 直接弹房间选择菜单(不再进门厅)。
/// 房间室内是惰性创建并缓存的独立 GameLocation(不在建筑室内序列里)。
/// 历史:门厅版(西墙/北墙门进小房间选)已废弃 —— 用户要"门做小、右键直接进"。</summary>
public static class LobbyDoors
{
    /// <summary>本 tick 已触发 warp 的大堂(防 1 tick 内重复触发)。</summary>
    private static GameLocation? _warpedLobbyThisTick;

    /// <summary>每帧末尾调用,清除一次性锁(必须接线,否则第一次进门后锁永不清 → 所有门失效)。</summary>
    internal static void OnEndOfTick() => _warpedLobbyThisTick = null;
}

/// <summary>房间室内地点管理:惰性创建 + 缓存(键 = 父建筑 Guid + RoomType)。</summary>
/// <remarks>
/// 设计:建筑只有一个 instanced 室内(大堂),房间是 mod 自建、自管的独立 GameLocation,
/// 加入 Game1.locations,按 <c>name</c> 可被 Game1.getLocationFromName 找到 → warpFarmer 可达。
/// 创建后把房间出口 warp(默认指向 Farm)改写指向大堂,返回大堂。
/// 存档:房间不进存档(只有大堂随建筑保存);读档后本缓存为空,首次进入时重建 —— 纯状态持有,
/// 实体动物挂在大堂建筑,台账在 BarnManager modData,重建无损。
/// </remarks>
public static class RoomManager
{
    private static readonly Dictionary<(Guid BuildingId, RoomType Room), GameLocation> Rooms = new();

    /// <summary>获取(或惰性创建)某建筑某房间的室内地点。lobby 为对应大堂(用于 warp 回程目标)。</summary>
    public static GameLocation? GetOrCreate(Building building, RoomType room, GameLocation? lobby)
    {
        var key = (building.id.Value, room);
        if (Rooms.TryGetValue(key, out var existing))
        {
            // 防呆:缓存与当前建筑状态不一致时重建(建筑拆除重建、读档后 id 变化)
            if (existing.ParentBuilding == building) return existing;
            Rooms.Remove(key);
        }

        var def = RoomDefinitions.Get(room);
        GameLocation loc;
        try
        {
            // 原版 AnimalHouse:有动物集合/自动喂食/DayUpdate 结算,且存档序列化安全(原版已知类型)。
            loc = new StardewValley.AnimalHouse("Maps\\" + def.MapName, def.MapName + "_" + Guid.NewGuid().ToString("N")[..6]);
        }
        catch (Exception ex)
        {
            ModEntry.Instance.Monitor.Log("RoomManager: failed to create room '" + def.MapName + "': " + ex, StardewModdingAPI.LogLevel.Error);
            return null;
        }

        loc.ParentBuilding = building;   // 公共字段,可直接赋值(已验证 1.6.15)

        // 【原版栅栏围圈】:中央走道两侧各 1 列原版木栅栏(Fence.woodFenceId),中间 y=6 原版栅栏门
        // (Fence.gateId,玩家右键开关)。视觉是原版栅栏+栅栏门(用户要求),动物在圈内。
        BuildFences(loc);

        // 房间出口 warp → 大堂【门洞正下方地板】(门洞 (DoorX,1),y=2 是墙裙【墙】不能落!
        // ⚠️ 此前落 (DoorX,2) = 墙裙里 = "复活在墙里面" 根因。正确 = 墙后 1 格地板 (DoorX,3)。
        // (中控台在 (6,4),不撞。)
        if (lobby != null && loc.warps.Count > 0)
        {
            var w = loc.warps[0];
            w.TargetName = lobby.NameOrUniqueName;
            w.TargetX = LobbyMapBuilder.DoorX;       // (6,3) 墙后 1 格地板
            w.TargetY = LobbyMapBuilder.HallDoor.Y + 2;
        }

        Game1.locations.Add(loc);
        Rooms[key] = loc;
        return loc;
    }

    /// <summary>原版栅栏围圈:中央走道两侧竖列(x=5,9,y=3..9) + 中间 y=6 栅栏门。
    /// 动物区顶部(y=2 墙裙)和底部(y=10 底封)已被 Buildings 层墙封死 → 动物唯一出口是
    /// y=6 的栅栏门!⚠️ gate 默认【开】→ 动物从门溜到走道 = "动物刷中间"根因。
    /// 必须确认 gate 关闭(用原版 Fence.toggleGate 源码参数)。</summary>
    private static void BuildFences(GameLocation loc)
    {
        int xMid = RoomMapBuilder.DoorX;   // =7
        // 两侧竖列(y=3..9,中间 y=6 留门位)
        for (int y = 3; y <= 9; y++)
        {
            if (y == 6) continue;   // 栅栏门位
            loc.objects[new Vector2(xMid - 2, y)] = new Fence(new Vector2(xMid - 2, y), Fence.woodFenceId, isGate: false);
            loc.objects[new Vector2(xMid + 2, y)] = new Fence(new Vector2(xMid + 2, y), Fence.woodFenceId, isGate: false);
        }

        // 原版栅栏门(玩家右键开关)
        var g1 = new Fence(new Vector2(xMid - 2, 6), Fence.gateId, isGate: true);
        var g2 = new Fence(new Vector2(xMid + 2, 6), Fence.gateId, isGate: true);
        loc.objects[new Vector2(xMid - 2, 6)] = g1;
        loc.objects[new Vector2(xMid + 2, 6)] = g2;
        // 关门:源码实锤 gatePosition.Value=0 是关门(第379行)、88 是开门。
        // ⚠️ 不能用 toggleGate —— 它要求 Location 非 null(刚 new 完可能 null → 直接 return 没关门)!
        // 直接设 gatePosition=0(关)最可靠,动物出不去。
        g1.gatePosition.Value = 0;
        g2.gatePosition.Value = 0;
    }

    /// <summary>读档后清理缓存(房间由首次进门时重建)。</summary>
    public static void ClearCache() => Rooms.Clear();

    /// <summary>只查缓存(不创建):房间已创建则返回,否则 null。购买后同步可见实体用。</summary>
    public static GameLocation? GetExisting(Building building, RoomType room)
        => Rooms.TryGetValue((building.id.Value, room), out var loc) ? loc : null;

    /// <summary>某房间已创建实体的 id 集合(建筑级结算用:排除已由原生结算的实体)。
    /// 房间没创建(玩家没进过门)→ 空集 → 全部台账结算。</summary>
    public static HashSet<long> GetExistingRoomAnimals(RoomType room)
    {
        var ids = new HashSet<long>();
        foreach (var (key, loc) in Rooms)
        {
            if (key.Room != room) continue;
            if (loc is not StardewValley.AnimalHouse ah) continue;
            foreach (var animal in ah.animals.Values)
                ids.Add(animal.myID.Value);
        }
        return ids;
    }

    /// <summary>测试/诊断用。</summary>
    public static int Count => Rooms.Count;
}
