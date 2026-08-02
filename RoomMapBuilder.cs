using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using xTile;
using xTile.Dimensions;
using xTile.Layers;
using xTile.Tiles;

namespace AnimalBarn;

/// <summary>代码生成 8 个动物房间地图。复用 LobbyMapBuilder 的 tile 配方(地板/墙/边界环防穿墙),
/// 并新增干草槽(Trough 属性,AnimalHouse.feedAllAnimals 扫描 Back 层该属性自动喂食)。
/// 地图尺寸 15x11,统一由 Task 3.2 门系统按 RoomDefinitions.All 逐个设为房间 IndoorMap。</summary>
public static class RoomMapBuilder
{
    public const int Width = 15;
    public const int Height = 11;

    // 与 LobbyMapBuilder 相同的已验证 tile 索引(MonsterArena/Task 1.4):
    // 地板 = walls_and_floors 336/352(隔行),底座 = 32,墙 = townInterior 1(顶)/64(侧)/160(角)
    private const int FloorA = 336;
    private const int FloorB = 352;
    private const int Baseboard = 32;
    private const int WallTop = 1;
    private const int WallSide = 64;
    private const int WallCorner = 160;

    /// <summary>门洞中心 tile X(与 LobbyMapBuilder 相同的定义;出口落点即门洞上方一格)。</summary>
    public const int DoorX = Width / 2;
    public const int DoorY = Height - 1;

    public static void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        foreach (var def in RoomDefinitions.All)
        {
            if (e.Name.IsEquivalentTo("Maps/" + def.MapName))
            {
                e.LoadFrom(() => BuildRoomMap(), AssetLoadPriority.Medium);
                return;
            }
        }
    }

    public static Map BuildRoomMap()
    {
        var map = new Map();
        // 五层:Back/Buildings/Front/Paths/AlwaysFront(游戏要求,缺一崩)
        // Layer tileSize 必须 64x64(static 共享,运行时游戏按 64 计算碰撞边界)。
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

        // 贴图集(6 参构造:id, map, imageSource, sheetSize, tileSize)
        var floorSheet = new TileSheet("walls_and_floors", map, "Maps/walls_and_floors", new Size(512, 384), new Size(16, 16));
        var interiorSheet = new TileSheet("townInterior", map, "Maps/townInterior", new Size(512, 512), new Size(16, 16));
        map.AddTileSheet(floorSheet);
        map.AddTileSheet(interiorSheet);

        // 地板:全铺 336,隔行 352(视觉交错)
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
                back.Tiles[x, y] = new StaticTile(back, floorSheet, BlendMode.Alpha, (y % 2 == 0) ? FloorA : FloorB);

        // 顶墙:WallTop 一行 + 下面 Baseboard 一行(封底)
        for (int x = 0; x < Width; x++)
        {
            buildings.Tiles[x, 0] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, WallTop);
            buildings.Tiles[x, 1] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, Baseboard);
        }
        // 两侧墙
        for (int y = 0; y < Height; y++)
        {
            buildings.Tiles[0, y] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, WallSide);
            buildings.Tiles[Width - 1, y] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, WallSide);
        }
        // 四角
        buildings.Tiles[0, 0] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, WallCorner);
        buildings.Tiles[Width - 1, 0] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, WallCorner);
        // 边界环:底行全 Baseboard(防穿墙),中央留 3 格门洞 (x=6,7,8)
        for (int x = 0; x < Width; x++)
            buildings.Tiles[x, Height - 1] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, Baseboard);
        for (int x = Width / 2 - 1; x <= Width / 2 + 1; x++)
            buildings.Tiles[x, Height - 1] = null;

        // 干草槽:Back 层 y=2 一行加 Trough 属性(AnimalHouse.feedAllAnimals 扫描 "Trough"/"Back" 属性喂食)。
        // tile 本身是地板(可通行)——动物踩上去进食;绝不能放在墙/阻挡 tile 上。
        for (int x = 3; x <= Width - 4; x++)
        {
            var tile = back.Tiles[x, 2];
            tile.Properties["Trough"] = "T";
        }

        // AutoFeed 属性(AnimalHouse 自动喂食:有 Trough 槽时直接消耗槽内干草)
        map.Properties["AutoFeed"] = "T";

        // ProduceArea 属性:动物随机站位矩形(中间 11x6 区域)
        map.Properties["ProduceArea"] = "2,3,11,6";

        // 出楼 warp:门洞中心 -> Farm。updateWarps() 读取本属性,ParentBuilding.updateInteriorWarps()
        // 把 TargetName=="Farm" 的 warp 改写为建筑 HumanDoor 绝对坐标。无此属性进入房间即崩溃
        // (Building 使用 warps[0]),所以必需。
        map.Properties["Warp"] = $"{DoorX} {DoorY} Farm 0 0";

        return map;
    }
}
