using System.Collections.Generic;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;

namespace AnimalBarn;

/// <summary>
/// 联机同步层(2026-08-03 新增):解决"访客看中枢状态与房主不同步 / 访客进房间黑屏"。
///
/// 原版机制(Building.cs 反编译确认):Building.modData 是 NetField → 主机写入后自动同步到
/// 所有访客。所以正确姿势 = 主机权威 + modData 实时落盘,访客直接读 modData(不缓存)。
///
/// 1. 主机每次状态变更后调 <see cref="CommitState"/> 立即写 modData → NetField 推给访客。
/// 2. 访客 GetOrCreate 直接 Load(modData),看到的永远是主机最新状态。
/// 3. 访客写操作(购买/升级/取货/干草):GuardHostOnly 放行访客 → 访客本地扣钱 →
///    发消息给主机 → 主机改状态(不扣钱)并落盘。
/// 4. 访客请求进房间:发消息给主机 → 主机创建房间(加入 Game1.locations)→ 回 ack →
///    访客 warp(主机已有该地点,同步成功,不再黑屏)。
/// </summary>
public static class MultiplayerSync
{
    public const string MsgRequestState = "ab_request_state";
    public const string MsgWriteOp = "ab_write_op";
    public const string MsgEnterRoom = "ab_enter_room";
    public const string MsgEnterRoomAck = "ab_enter_room_ack";

    public enum WriteOp
    {
        UpgradeOverall,
        UpgradeRoom,
        BuyAnimal,
        BuyHay,
        DepositHay,
        WithdrawHay,
        TakeProduce,
        TakeAll
    }

    public class WritePayload
    {
        public WriteOp Op;
        public string BuildingId = "";          // building.id (Guid.ToString)
        public string Arg = "";                 // 房间类型 / 物品id|星级 / 干草数量等
        public int Quantity;
    }

    public class EnterRoomPayload
    {
        public string BuildingId = "";
        public string Room = "";
        public string RoomName = "";   // ack 时带主机实际创建的房间名(随机后缀,访客无法预知)
    }

    private static IModHelper? _helper;
    private static IMonitor? _monitor;
    private static BarnManager? _barn;

    public static void Init(IModHelper helper, IMonitor monitor, BarnManager barn)
    {
        _helper = helper;
        _monitor = monitor;
        _barn = barn;
        helper.Events.Multiplayer.ModMessageReceived += OnMessageReceived;
    }

    /*********
    ** 主机侧
    *********/

    /// <summary>主机:状态变更后立即落盘 modData(NetField 自动同步给访客)。</summary>
    public static void CommitState(Building building)
    {
        if (!Game1.IsMasterGame || _barn == null || building == null) return;
        var state = _barn.GetOrCreate(building);
        SaveSerializer.Save(building, state);
    }

    private static void OnMessageReceived(object? sender, ModMessageReceivedEventArgs e)
    {
        if (_helper == null || _monitor == null || _barn == null) return;
        if (e.FromModID != ModEntry.Instance.ModManifest.UniqueID) return;
        if (!Context.IsWorldReady) return;
        try
        {
            switch (e.Type)
            {
                case MsgRequestState:
                    // 访客打开菜单前请求状态:主机把状态写回 modData(NetField 同步),访客随后读。
                    if (Game1.IsMasterGame)
                        foreach (var b in _barn.FindAllBarns())
                            CommitState(b);
                    break;

                case MsgWriteOp:
                    if (Game1.IsMasterGame)
                        HandleWriteOp(e.ReadAs<WritePayload>(), e.FromPlayerID);
                    break;

                case MsgEnterRoom:
                    if (Game1.IsMasterGame)
                        HandleEnterRoom(e.ReadAs<EnterRoomPayload>(), e.FromPlayerID);
                    break;

                case MsgEnterRoomAck:
                    // 访客收到 ack:主机已创建房间 → 访客 warp 进去(主机有该地点,同步成功,不黑屏)。
                    if (!Game1.IsMasterGame)
                    {
                        var payload = e.ReadAs<EnterRoomPayload>();
                        if (!string.IsNullOrEmpty(payload.RoomName))
                        {
                            Game1.warpFarmer(payload.RoomName, RoomMapBuilder.DoorX, 3, 2);
                        }
                    }
                    break;
            }
        }
        catch (System.Exception ex)
        {
            _monitor.Log($"[ab_sync] 联机消息处理失败: {ex}", LogLevel.Error);
        }
    }

    /// <summary>主机执行访客的写操作(钱已在访客端扣掉,主机只改状态)。</summary>
    private static void HandleWriteOp(WritePayload payload, long fromPlayerId)
    {
        if (_barn == null) return;
        if (payload.BuildingId.Length == 0 || !System.Guid.TryParse(payload.BuildingId, out var guid)) return;
        var building = _barn.FindBuildingById(guid);
        if (building == null) return;

        var state = _barn.GetOrCreate(building);
        switch (payload.Op)
        {
            case WriteOp.UpgradeOverall:
                if (state.OverallLevel < 5)
                    state.OverallLevel++;
                break;

            case WriteOp.UpgradeRoom:
            {
                if (System.Enum.TryParse<RoomType>(payload.Arg, out var room))
                {
                    var houseRoom = RoomDefinitions.RoomFor(room);
                    var rs = state.GetRoom(houseRoom);
                    var rows = UpgradeSystem.CapacityFor(houseRoom);
                    if (rs.UpgradeLevel < rows.Length - 1)
                        rs.UpgradeLevel++;
                }
                break;
            }

            case WriteOp.BuyAnimal:
            {
                if (System.Enum.TryParse<RoomType>(payload.Arg, out var animalType))
                {
                    var houseRoom = RoomDefinitions.RoomFor(animalType);
                    var rs = state.GetRoom(houseRoom);
                    var ledger = AnimalLedger.FromRoom(rs);
                    ledger.Capacity = UpgradeSystem.CapacityAt(houseRoom, rs.UpgradeLevel);
                    for (int i = 0; i < payload.Quantity; i++)
                    {
                        if (ledger.IsFull) break;
                        long id = Utility.RandomLong(Game1.random);
                        ledger.TryAdd(new LedgerAnimal
                        {
                            Id = id,
                            Room = animalType,
                            TypeKey = FarmAnimalCatalog.Get(animalType).TypeKey,
                            AgeDays = FarmAnimalCatalog.Get(animalType).MatureDays,
                            Friendship = 0,
                            Happiness = 255,
                            Fullness = 255,
                            DaysSinceProduce = FarmAnimalCatalog.Get(animalType).DaysToProduce,
                            ProduceCount = 0,
                            OwnerId = fromPlayerId,
                        });
                    }
                    ledger.SaveTo(rs);
                    // 主机生成可见实体(动物是 Net 字段,同步给访客)
                    RoomAnimalRenderer.SyncRoom(building, houseRoom, rs);
                }
                break;
            }

            case WriteOp.BuyHay:
                state.HayStock += payload.Quantity;
                break;

            case WriteOp.DepositHay:
                state.HayStock += payload.Quantity;
                break;

            case WriteOp.WithdrawHay:
                state.HayStock = System.Math.Max(0, state.HayStock - payload.Quantity);
                break;

            case WriteOp.TakeProduce:
            {
                var parts = payload.Arg.Split('|');
                string id = parts.Length > 0 ? parts[0] : payload.Arg;
                int quality = parts.Length > 1 && int.TryParse(parts[1], out int q) ? q : 0;
                RemoveProduce(state, id, quality, payload.Quantity);
                break;
            }

            case WriteOp.TakeAll:
            {
                foreach (var roomState in state.Rooms.Values)
                {
                    roomState.ProduceStacks.Clear();
                    roomState.ProduceCount = 0;
                }
                state.GlobalProduceStacks.Clear();
                state.ProduceCount = 0;
                break;
            }
        }

        CommitState(building);
        _monitor.Log($"[ab_sync] 主机已执行访客操作 {payload.Op}({payload.Arg} x{payload.Quantity})", LogLevel.Info);
    }

    /// <summary>从所有房间扣减指定产品堆叠(按星级)。</summary>
    private static void RemoveProduce(BarnSaveData state, string id, int quality, int take)
    {
        string key = AutoGrabberInterceptor.ProduceKey(id, quality);
        int remaining = take;
        foreach (var roomState in state.Rooms.Values)
        {
            if (remaining <= 0) break;
            if (!roomState.ProduceStacks.TryGetValue(key, out int n) || n <= 0) continue;
            int removed = System.Math.Min(remaining, n);
            roomState.ProduceStacks[key] = n - removed;
            roomState.ProduceCount = System.Math.Max(0, roomState.ProduceCount - removed);
            remaining -= removed;
            if (roomState.ProduceStacks[key] <= 0) roomState.ProduceStacks.Remove(key);
        }
    }

    /// <summary>主机:访客请求进房间 → 确保房间在主机存在(黑屏根因:访客 warp 到主机没有的地点)。
    /// 房间地点必须加入主机 Game1.locations,访客 warp 时主机 RequireLocation 才能找到并同步。
    /// 实体动物由主机生成(Net 字段同步给访客)。</summary>
    private static void HandleEnterRoom(EnterRoomPayload payload, long fromPlayerId)
    {
        if (_barn == null || _helper == null) return;
        if (payload.BuildingId.Length == 0 || !System.Guid.TryParse(payload.BuildingId, out var guid)) return;
        if (!System.Enum.TryParse<RoomType>(payload.Room, out var room)) return;
        var building = _barn.FindBuildingById(guid);
        if (building == null) return;

        // 找大堂(房间回程目标)
        GameLocation? lobby = null;
        foreach (var loc in Game1.locations)
        {
            if (AnimalBarnLocations.IsLobby(loc) && loc.ParentBuilding == building)
            {
                lobby = loc;
                break;
            }
        }

        // 主机创建房间(照 RoomManager.GetOrCreate 逻辑;RoomManager 缓存在主机端)
        GameLocation? roomLoc = RoomManager.GetOrCreate(building, room, lobby);
        if (roomLoc == null) return;

        // 主机确保实体动物与台账一致(生成后 Net 同步给访客)
        RoomAnimalRenderer.EnsureVisibleOnEnter(roomLoc);

        // 回 ack:访客可以 warp 了(带实际房间名)
        _helper.Multiplayer.SendMessage(
            new EnterRoomPayload { BuildingId = payload.BuildingId, Room = payload.Room, RoomName = roomLoc.NameOrUniqueName },
            MsgEnterRoomAck,
            modIDs: new[] { ModEntry.Instance.ModManifest.UniqueID },
            playerIDs: new[] { fromPlayerId });
        _monitor.Log($"[ab_sync] 主机已创建房间 {room} 并回 ack", LogLevel.Info);
    }

    /*********
    ** 访客侧
    *********/

    /// <summary>访客:打开菜单前请求主机把状态落盘(NetField 同步过来后,读 modData 即最新)。</summary>
    public static void RequestStateFromHost()
    {
        if (_helper == null || Game1.IsMasterGame) return;
        _helper.Multiplayer.SendMessage(new object(), MsgRequestState, new[] { ModEntry.Instance.ModManifest.UniqueID });
    }

    /// <summary>访客:请求进房间(主机创建 + ack)。</summary>
    public static void RequestEnterRoom(Building building, RoomType room)
    {
        if (_helper == null || Game1.IsMasterGame) return;
        _helper.Multiplayer.SendMessage(
            new EnterRoomPayload { BuildingId = building.id.Value.ToString(), Room = room.ToString() },
            MsgEnterRoom,
            modIDs: new[] { ModEntry.Instance.ModManifest.UniqueID });
    }

    /// <summary>访客:写操作转发主机(钱已在访客端扣)。返回是否成功发出。</summary>
    public static bool ForwardWrite(WriteOp op, Building building, string arg, int quantity)
    {
        if (_helper == null || Game1.IsMasterGame) return false;
        _helper.Multiplayer.SendMessage(
            new WritePayload { Op = op, BuildingId = building.id.Value.ToString(), Arg = arg, Quantity = quantity },
            MsgWriteOp,
            modIDs: new[] { ModEntry.Instance.ModManifest.UniqueID });
        return true;
    }
}
