using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Buildings;

namespace AnimalBarn;

/// <summary>大堂门系统:玩家站到门洞 tile → warp 进对应房间;未解锁房间的门被墙堵着(建筑层未打洞),
/// 玩家走到墙前会收到提示。房间室内是惰性创建并缓存的独立 GameLocation(不在建筑室内序列里)。
/// 接入方式:由 ModEntry.UpdateTicked 每 tick 调用 <see cref="TryEnterDoor"/>。</summary>
public static class LobbyDoors
{
    /// <summary>本 tick 已触发 warp 的大堂(防 1 tick 内重复触发)。</summary>
    private static GameLocation? _warpedLobbyThisTick;

    /// <summary>玩家站在门洞上时调用(ModEntry.UpdateTicked 每 tick 检查)。
    /// 返回 true 表示已触发 warp。barn 可传 null → 退回 SettlementService.Current。</summary>
    public static bool TryEnterDoor(GameLocation lobby, Farmer who, BarnManager? barn = null)
    {
        if (lobby == null || who == null || !who.IsLocalPlayer) return false;
        if (Game1.isWarping || Game1.locationRequest != null || Game1.eventUp) return false;
        if (ReferenceEquals(_warpedLobbyThisTick, lobby)) return false;

        var tile = who.TilePoint;
        foreach (var (room, x, y) in LobbyMapBuilder.DoorPositions)
        {
            if (tile.X != x || tile.Y != y) continue;

            // 未解锁:门还堵着(墙 tile 未打洞,玩家本就走不进来);若已站到门洞说明已解锁。
            // 给玩家与"解锁后进门"一致的朝向提示(仅在墙外相邻时)。
            var barn0 = barn ?? SettlementService.Current;
            int level = barn0 != null && lobby.ParentBuilding != null
                ? (barn0.GetOrCreate(lobby.ParentBuilding).OverallLevel)
                : 1;
            if (!UpgradeSystem.IsUnlocked(room, level))
            {
                if (IsAdjacentToWall(lobby, tile))
                    Game1.showRedMessage("该房间尚未解锁(升级养殖场以解锁)");
                return false;
            }

            // 已解锁:warp 进房间。房间惰性创建并缓存(键 = 父建筑 id + RoomType)。
            var target = RoomManager.GetOrCreate(lobby, room);
            if (target == null) return false;

            // 进门可见动物:把台账前 30 只补成实体(此前只写台账不生成实体 → 房间里看不见动物)。
            RoomAnimalRenderer.EnsureVisibleOnEnter(target);

            _warpedLobbyThisTick = lobby;
            Game1.warpFarmer(target.NameOrUniqueName, RoomMapBuilder.DoorX, RoomMapBuilder.DoorY - 1, FacingIntoRoom(room));
            return true;
        }
        return false;
    }

    /// <summary>门洞朝向:北/南门朝上(0),西门朝右(1),东门朝左(3)。
    /// 侧门是凹进门龛(门洞 x=1 西 / x=Width-2 东),按"门靠哪边墙"判朝向。</summary>
    private static int FacingIntoRoom(RoomType room)
    {
        foreach (var (r, x, y) in LobbyMapBuilder.DoorPositions)
        {
            if (r != room) continue;
            if (x == 1) return 1;                    // 西墙凹进门 → 朝右
            if (x == LobbyMapBuilder.Width - 2) return 3;  // 东墙凹进门 → 朝左
            return 0;                                // 北墙门 → 朝上
        }
        return 0;
    }

    /// <summary>玩家是否站在某门洞墙外侧(紧邻墙的室内 tile) —— 用于未解锁时贴墙提示。</summary>
    private static bool IsAdjacentToWall(GameLocation lobby, Point tile)
    {
        foreach (var (_, x, y) in LobbyMapBuilder.DoorPositions)
        {
            if (Math.Abs(tile.X - x) + Math.Abs(tile.Y - y) == 1 &&
                lobby.map.GetLayer("Buildings")?.Tiles[x, y] != null)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>大堂从房间返回时调用(房间出口 warp 触发时)。清除本 tick 门触发锁。</summary>
    internal static void OnReturnedToLobby() => _warpedLobbyThisTick = null;

    /// <summary>每帧末尾调用,清除一次性锁。</summary>
    internal static void OnEndOfTick() => _warpedLobbyThisTick = null;
}

/// <summary>房间室内地点管理:惰性创建 + 缓存(键 = 父建筑 Guid + RoomType)。</summary>
/// <remarks>
/// 设计:建筑只有一个 instanced 室内(大堂),8 个房间是 mod 自建、自管的独立 GameLocation,
/// 加入 Game1.locations,按 <c>name</c> 可被 Game1.getLocationFromName 找到 → warpFarmer 可达。
/// 创建后把房间出口 warp(默认指向 Farm)改写指向大堂门洞,返回大堂。
/// 存档:房间不进存档(只有大堂随建筑保存);读档后本缓存为空,首次进门时重建 —— 房间纯状态持有,
/// 实体动物挂在大堂建筑,台账在 BarnManager modData,重建无损。
/// </remarks>
public static class RoomManager
{
    private static readonly Dictionary<(Guid BuildingId, RoomType Room), GameLocation> Rooms = new();

    /// <summary>获取(或惰性创建)某建筑某房间的室内地点。已缓存直接返回。</summary>
    public static GameLocation? GetOrCreate(GameLocation lobby, RoomType room)
    {
        var building = lobby.ParentBuilding;
        if (building == null) return null;
        return GetOrCreate(building, room, lobby);
    }

    /// <summary>获取(或惰性创建)某建筑某房间的室内地点。lobby 为对应大堂(用于 warp 回程目标)。
    /// 公开给 IntegrationTest/协调者直接调用(传真实 Building 或最小桩)。</summary>
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
            // 房间类型由地图属性识别(AnimalBarnLocations.TryGetRoomType),不用自定义类。
            loc = new StardewValley.AnimalHouse("Maps\\" + def.MapName, def.MapName + "_" + Guid.NewGuid().ToString("N")[..6]);
        }
        catch (Exception ex)
        {
            ModEntry.Instance.Monitor.Log("RoomManager: failed to create room '" + def.MapName + "': " + ex, StardewModdingAPI.LogLevel.Error);
            return null;
        }

        loc.ParentBuilding = building;   // 公共字段,可直接赋值(已验证 1.6.15)

        // 圈一道矮围栏,把"动物圈"(上半,干草槽一带)和"玩家通道"(下半,门口)隔开,有养殖场的样子。
        // 中央靠门留 3 格缺口供进出圈。围栏是运行时 Fence 对象(StardewValley.Object),原版地道圈法,
        // 不进存档(房间不序列化),读档后随房间重建。 y=5 在槽行(y3)之下、门口(h-1)之上。
        BuildPenFence(loc);

        // 房间出口 warp(地图属性里指向 Farm)→ 改写指向大堂门洞,玩家从房间返回大堂。
        // 注意:房间不是建筑的 instanced indoors,updateInteriorWarps 不会触碰它;
        // updateWarps 只在 mapPath 变化时重解析 —— 本地点构造后不会再变,改写安全。
        if (lobby != null && loc.warps.Count > 0)
        {
            var w = loc.warps[0];
            w.TargetName = lobby.NameOrUniqueName;
            // 落点在大堂出口门洞"上方一格"(6,7):实心地板、安全,不会再往外一步走出大堂。
            // (此前落在门洞 tile (6,8) 本身——底边缺口,玩家容易直接走出大堂进虚空 = 踩空。)
            w.TargetX = LobbyMapBuilder.DoorX;
            w.TargetY = LobbyMapBuilder.DoorY - 1;
        }
        else
        {
            ModEntry.Instance.Monitor.Log($"RoomManager: room '{loc.NameOrUniqueName}' created without lobby warp retarget (warps={loc.warps.Count})", StardewModdingAPI.LogLevel.Warn);
        }

        Game1.locations.Add(loc);
        Rooms[key] = loc;
        return loc;
    }

    /// <summary>在房间 y=5 拉一道木围栏,中央靠门留 3 格缺口(x=DoorX-1..DoorX+1)。
    /// 围栏放 loc.objects(Fence 是 Object),原版畜棚圈法;阻挡通行,动物/玩家都绕缺口走。</summary>
    private static void BuildPenFence(GameLocation loc)
    {
        const int fenceY = 5;
        int w = RoomMapBuilder.Width;
        for (int x = 1; x < w - 1; x++)
        {
            if (x >= RoomMapBuilder.DoorX - 1 && x <= RoomMapBuilder.DoorX + 1) continue;  // 缺口(通道)
            var tile = new Vector2(x, fenceY);
            loc.objects[tile] = new Fence(tile, Fence.woodFenceId, isGate: false);
        }
    }

    /// <summary>读档后清理缓存(房间由首次进门时重建)。</summary>
    public static void ClearCache() => Rooms.Clear();

    /// <summary>只查缓存(不创建):房间已创建则返回,否则 null。购买后同步可见实体用 ——
    /// 房间还没进过就不预建,等首次进门 EnsureVisibleOnEnter 再生成,避免无谓实例化。</summary>
    public static GameLocation? GetExisting(Building building, RoomType room)
        => Rooms.TryGetValue((building.id.Value, room), out var loc) ? loc : null;

    /// <summary>测试/诊断用。</summary>
    public static int Count => Rooms.Count;
}
