using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using xTile;
using xTile.Dimensions;
using xTile.Layers;
using xTile.Tiles;

namespace AnimalBarn;

/// <summary>代码生成大堂地图。复用 MonsterArena 的 tile 配方(地板/墙/边界环防穿墙)。</summary>
/// <remarks>
/// 关键已验证事实(反编译 xTile.dll + Stardew Valley.dll,1.6.15):
/// 1. Layer.m_tileSize 是共享 static —— 运行时地图 tile 尺寸必须是 64x64,
///    否则 GameLocation.IsOutOfBounds(DisplayWidth = LayerSize * m_tileSize) 算错,玩家会穿墙(MonsterArena 教训)。
/// 2. isTilePassable() 对越界 tile 返回 null→passable,玩家能走出地图边缘进虚空。
///    对策:Buildings 层最外圈全部铺阻挡 tile。
/// 3. 出楼 warp 必须写进地图的 "Warp" 属性:updateWarps()(室内构造时经 loadObjects 调用)读取该属性,
///    随后 ParentBuilding.updateInteriorWarps() 把目标改写为建筑 HumanDoor 位置。仅留门洞不会有出口。
/// </remarks>
public static class LobbyMapBuilder
{
    public const string MapAssetName = "xiepe.AnimalBarn.Lobby";  // SMAPI asset name (Maps/ prefix added by BuildingData.IndoorMap)
    public const int Width = 13;
    public const int Height = 9;

    // FarmHouse1 木地板参考 tile 索引(MonsterArena 已验证):
    // 地板 = walls_and_floors 336/337(隔行 352/353),底座 = 32,墙 = townInterior 1/2/3(顶),64/68(侧),160/130(底角)
    private const int FloorA = 336;
    private const int FloorB = 352;
    private const int Baseboard = 32;
    private const int WallTop = 1;
    private const int WallSide = 64;
    private const int WallCorner = 160;

    /// <summary>底部门洞中心 tile X(供后续任务在入口铺地板/交互用)。</summary>
    public const int DoorX = Width / 2;
    public const int DoorY = Height - 1;

    /// <summary>8 个房间门洞(墙缺口):(房间, 门洞 tile 坐标)。玩家站在缺口 tile 上 →
    /// LobbyDoors.TryEnterDoor 触发 warp 进房间。北墙 3 门在底座行 y=1(y=0 顶墙保留 → 1 格高门洞,
    /// 与原版门一致),西/东墙各 2 门,南墙 1 门(羊场门,避开出口门洞 x=5-7)。</summary>
    public static readonly (RoomType Room, int X, int Y)[] DoorPositions =
    {
        (RoomType.Chicken,  3, 1),   // 北墙(底座行)
        (RoomType.Duck,     6, 1),
        (RoomType.Rabbit,   9, 1),
        (RoomType.Dinosaur, 0, 4),   // 西墙
        (RoomType.Ostrich,  12, 4),  // 东墙
        (RoomType.Pig,      12, 7),
        (RoomType.Cow,      0, 7),
        (RoomType.Goat,     9, 8),   // 南墙(避开出口 x=5-7)
    };

    private static bool IsDoorTile(int x, int y) => DoorPositions.Any(d => d.X == x && d.Y == y);

    /// <summary>中枢操作台位置(大堂中央,玩家点击打开中枢菜单)。</summary>
    public static readonly Point HubTile = new(6, 4);

    /// <summary>是否中枢台 tile(玩家点击打开中枢菜单)。</summary>
    public static bool IsHubTile(int x, int y) => x == HubTile.X && y == HubTile.Y;

    public static void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.Name.IsEquivalentTo("Maps/" + MapAssetName))
        {
            e.LoadFrom(() => BuildMap(), AssetLoadPriority.Medium);
        }
    }

    public static Map BuildMap()
    {
        var map = new Map();
        // 五层:Back/Buildings/Front/Paths/AlwaysFront(游戏要求,缺一崩)
        // 注意 Layer 构造的 tileSize 必须 64x64:该字段是 static 共享的,运行时游戏按 64 计算碰撞边界。
        var back = new Layer("Back", map, new Size(Width, Height), new Size(64, 64));
        var buildings = new Layer("Buildings", map, new Size(Width, Height), new Size(64, 64));
        var front = new Layer("Front", map, new Size(Width, Height), new Size(64, 64));
        var paths = new Layer("Paths", map, new Size(Width, Height), new Size(64, 64));
        var alwaysFront = new Layer("AlwaysFront", map, new Size(Width, Height), new Size(64, 64));
        map.AddLayer(back);
        map.AddLayer(buildings);
        map.AddLayer(front);
        map.AddLayer(paths);
        map.AddLayer(alwaysFront);

        // 贴图集(构造签名:id, map, imageSource, sheetSize, tileSize —— 与 MonsterArena 一致)
        var floorSheet = new TileSheet("walls_and_floors", map, "Maps/walls_and_floors", new Size(512, 384), new Size(16, 16));
        var interiorSheet = new TileSheet("townInterior", map, "Maps/townInterior", new Size(512, 512), new Size(16, 16));
        map.AddTileSheet(floorSheet);
        map.AddTileSheet(interiorSheet);

        // 地板:全铺 336,隔行 352(视觉交错)
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
                back.Tiles[x, y] = new StaticTile(back, floorSheet, BlendMode.Alpha, (y % 2 == 0) ? FloorA : FloorB);

        // 顶墙:WallTop 一行 + 下面 Baseboard 一行(封底)。
        // 北墙 3 扇房间门开在底座行 y=1(门洞上方 y=0 顶墙保留 → 1 格高门洞,与原版门一致)。
        for (int x = 0; x < Width; x++)
        {
            buildings.Tiles[x, 0] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, WallTop);
            if (!IsDoorTile(x, 1))
                buildings.Tiles[x, 1] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, Baseboard);
        }
        // 两侧墙(西墙门:恐龙 y=4、牛 y=7;东墙门:鸵鸟 y=4、猪 y=7 —— 门洞位置留空)
        for (int y = 0; y < Height; y++)
        {
            if (!IsDoorTile(0, y))
                buildings.Tiles[0, y] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, WallSide);
            if (!IsDoorTile(Width - 1, y))
                buildings.Tiles[Width - 1, y] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, WallSide);
        }
        // 四角
        buildings.Tiles[0, 0] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, WallCorner);
        buildings.Tiles[Width - 1, 0] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, WallCorner);
        // 边界环:底行全 Baseboard(防穿墙),中央留 3 格出口门洞 (x=5,6,7) + 羊场门洞 (9,8)
        for (int x = 0; x < Width; x++)
            if (x < Width / 2 - 1 || x > Width / 2 + 1)   // 跳过出口门洞 x=5,6,7
                if (!IsDoorTile(x, Height - 1))
                    buildings.Tiles[x, Height - 1] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, Baseboard);

        // 门洞视觉:每个门洞在 Back 层放一块门垫地板(336),让缺口看起来是"门口"而不是破洞。
        foreach (var (_, x, y) in DoorPositions)
            back.Tiles[x, y] = new StaticTile(back, floorSheet, BlendMode.Alpha, FloorA);

        // 中枢操作台(大堂中央):一张桌子 + 一侧椅子,玩家点击打开中枢菜单。
        // townInterior 桌子 tile 参考索引:桌 179,椅 150(视觉点缀,不挡路)。
        const int TableTile = 179;
        const int ChairTile = 150;
        buildings.Tiles[HubTile.X, HubTile.Y] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, TableTile);
        buildings.Tiles[HubTile.X + 1, HubTile.Y] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, ChairTile);

        // AutoFeed 属性(AnimalHouse 自动喂食用;大堂无动物,但统一加无妨)
        map.Properties["AutoFeed"] = "T";

        // 出楼 warp:门洞中心 -> 楼外 HumanDoor 处。updateWarps()(室内构造时经 loadObjects 调用)读取本属性,
        // 然后 ParentBuilding.updateInteriorWarps() 会把 TargetName=="Farm" 的 warp 改写为 HumanDoor 绝对坐标。
        // toX/toY 用占位(0 0)即可 —— 会被改写。此 warp 同时是建筑入口的落点:Building 点门进入时
        // Game1.warpFarmer(interior, warps[0].X, warps[0].Y - 1),即玩家出现在门洞上方一格 (6, 7)。
        map.Properties["Warp"] = $"{DoorX} {DoorY} Farm 0 0";

        return map;
    }
}
