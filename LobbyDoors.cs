using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Buildings;

namespace AnimalBarn;

/// <summary>大堂门系统(统一入口版):大堂只有 1 扇门 —— 西墙【门厅门】,玩家站上门厅门 → warp 进【门厅】。
/// 门厅(LobbyMapBuilder 门厅地图)里放一台房间选择终端,点终端弹菜单选房间,选完直接进房间。
/// 房间室内是惰性创建并缓存的独立 GameLocation(不在建筑室内序列里)。
/// 接入方式:由 ModEntry.UpdateTicked 每 tick 调用 <see cref="TryEnterDoor"/>。
/// 修复:锁 _warpedLobbyThisTick 之前只设不清(OnEndOfTick 没被接线)→ 第一次进门后所有门失效。
/// 现在 OnEndOfTick 由 ModEntry 每 tick 末尾调用,锁每次只挡 1 tick。</summary>
public static class LobbyDoors
{
    /// <summary>本 tick 已触发 warp 的大堂(防 1 tick 内重复触发)。</summary>
    private static GameLocation? _warpedLobbyThisTick;

    /// <summary>玩家站在门厅门上时调用(ModEntry.UpdateTicked 每 tick 检查)。
    /// 返回 true 表示已触发 warp。</summary>
    public static bool TryEnterDoor(GameLocation lobby, Farmer who, BarnManager? barn = null)
    {
        if (lobby == null || who == null || !who.IsLocalPlayer) return false;
        if (Game1.isWarping || Game1.locationRequest != null || Game1.eventUp) return false;
        if (ReferenceEquals(_warpedLobbyThisTick, lobby)) return false;

        var tile = who.TilePoint;
        if (!LobbyMapBuilder.IsHallDoorTile(tile.X, tile.Y)) return false;

        // warp 进【门厅】。门厅由 RoomManager 惰性创建并缓存。
        var hall = RoomManager.GetOrCreateHall(lobby);
        if (hall == null) return false;

        _warpedLobbyThisTick = lobby;
        Game1.warpFarmer(hall.NameOrUniqueName, HallMapBuilder.DoorX, HallMapBuilder.DoorY - 1, 1);  // 朝右进门厅
        return true;
    }

    /// <summary>大堂从房间返回时调用(房间出口 warp 触发时)。清除本 tick 门触发锁。</summary>
    internal static void OnReturnedToLobby() => _warpedLobbyThisTick = null;

    /// <summary>每帧末尾调用,清除一次性锁(必须接线,否则第一次进门后锁永不清 → 所有门失效)。</summary>
    internal static void OnEndOfTick() => _warpedLobbyThisTick = null;
}

/// <summary>房间/门厅室内地点管理:惰性创建 + 缓存(键 = 父建筑 Guid + RoomType)。</summary>
/// <remarks>
/// 设计:建筑只有一个 instanced 室内(大堂),门厅/房间是 mod 自建、自管的独立 GameLocation,
/// 加入 Game1.locations,按 <c>name</c> 可被 Game1.getLocationFromName 找到 → warpFarmer 可达。
/// 创建后把房间出口 warp(默认指向 Farm)改写指向大堂门厅,返回门厅。
/// 存档:门厅/房间不进存档(只有大堂随建筑保存);读档后本缓存为空,首次进入时重建 —— 纯状态持有,
/// 实体动物挂在大堂建筑,台账在 BarnManager modData,重建无损。
/// </remarks>
public static class RoomManager
{
    private static readonly Dictionary<(Guid BuildingId, RoomType Room), GameLocation> Rooms = new();

    /// <summary>门厅缓存(每建筑一个)。</summary>
    private static readonly Dictionary<Guid, GameLocation> Halls = new();

    /// <summary>获取(或惰性创建)某建筑的门厅。玩家从大堂门厅门进入。</summary>
    public static GameLocation? GetOrCreateHall(GameLocation lobby)
    {
        var building = lobby.ParentBuilding;
        if (building == null) return null;

        if (Halls.TryGetValue(building.id.Value, out var existing))
        {
            if (existing.ParentBuilding == building) return existing;
            Halls.Remove(building.id.Value);
        }

        GameLocation hall;
        try
        {
            hall = new StardewValley.AnimalHouse("Maps\\" + HallMapBuilder.MapAssetName, "xiepe.AnimalBarn.Hall_" + Guid.NewGuid().ToString("N")[..6]);
        }
        catch (Exception ex)
        {
            ModEntry.Instance.Monitor.Log("RoomManager: 门厅创建失败: " + ex, StardewModdingAPI.LogLevel.Error);
            return null;
        }
        hall.ParentBuilding = building;

        // 门厅出口 → 大堂(落点在大堂门厅门内侧 (1,5):实心地板、安全)。
        if (lobby != null && hall.warps.Count > 0)
        {
            var w = hall.warps[0];
            w.TargetName = lobby.NameOrUniqueName;
            w.TargetX = LobbyMapBuilder.HallDoor.X;
            w.TargetY = LobbyMapBuilder.HallDoor.Y + 1;
        }

        Game1.locations.Add(hall);
        Halls[building.id.Value] = hall;
        return hall;
    }

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

        // 圈围栏:左右两个动物圈 + 中间过道(按用户要求:动物只呆圈里,中间是人的过道)。
        BuildPenFences(loc);

        // 房间出口 warp → 门厅(玩家从房间返回门厅,门厅再回大堂)。
        var hall = Halls.TryGetValue(building.id.Value, out var h) ? h : null;
        if (hall != null && loc.warps.Count > 0)
        {
            var w = loc.warps[0];
            w.TargetName = hall.NameOrUniqueName;
            w.TargetX = HallMapBuilder.DoorX;
            w.TargetY = HallMapBuilder.DoorY - 1;
        }
        else if (lobby != null && loc.warps.Count > 0)
        {
            var w = loc.warps[0];
            w.TargetName = lobby.NameOrUniqueName;
            w.TargetX = LobbyMapBuilder.DoorX;
            w.TargetY = LobbyMapBuilder.DoorY - 1;
        }

        Game1.locations.Add(loc);
        Rooms[key] = loc;
        return loc;
    }

    /// <summary>读档后清理缓存(房间由首次进门时重建)。</summary>
    public static void ClearCache() { Rooms.Clear(); Halls.Clear(); }

    /// <summary>只查缓存(不创建):房间已创建则返回,否则 null。购买后同步可见实体用。</summary>
    public static GameLocation? GetExisting(Building building, RoomType room)
        => Rooms.TryGetValue((building.id.Value, room), out var loc) ? loc : null;

    /// <summary>测试/诊断用。</summary>
    public static int Count => Rooms.Count + Halls.Count;

    /// <summary>在房间铺围栏:中央竖走道 x=DoorX(北入口→南出口,人走)两侧各 1 列栅栏
    /// (x=DoorX-1 / x=DoorX+1),栅栏外侧是左右两个大动物区。动物在动物区里,玩家走中央走道。
    /// 栅栏竖向(y=3..9),北入口行(y=2?)不留 —— 栅栏从槽行 y=3 到门口 y=9。</summary>
    private static void BuildPenFences(GameLocation loc)
    {
        int xMid = RoomMapBuilder.DoorX;
        // 中央走道两侧各 1 列栅栏
        for (int y = 3; y <= 9; y++)
        {
            loc.objects[new Vector2(xMid - 1, y)] = new Fence(new Vector2(xMid - 1, y), Fence.woodFenceId, isGate: false);
            loc.objects[new Vector2(xMid + 1, y)] = new Fence(new Vector2(xMid + 1, y), Fence.woodFenceId, isGate: false);
        }
        // NOTE:动物区 x=1..xMid-2 / xMid+2..13 是动物自由区;玩家走中央走道 xMid 上下贯通。
    }
}
