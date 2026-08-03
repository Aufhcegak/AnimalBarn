using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using xTile;
using xTile.Dimensions;
using xTile.Layers;
using xTile.Tiles;

namespace AnimalBarn;

/// <summary>代码生成 8 个动物房间地图。墙体照搬原版畜棚(coopTiles)配方,地板/干草每房差异化,
/// 并保留关键功能:干草槽(Trough 属性,feedAllAnimals 扫 Back 层该属性自动喂食)、
/// AutoFeed、ProduceArea、出口 Warp。地图尺寸 15x11,由 Task 3.2 门系统按 RoomDefinitions.All 设为房间 IndoorMap。</summary>
public static class RoomMapBuilder
{
    public const int Width = 15;
    public const int Height = 11;

    /// <summary>门洞中心 tile X(出口落点即门洞上方一格)。</summary>
    public const int DoorX = Width / 2;
    public const int DoorY = Height - 1;

    // coopTiles 平铺地板纹理(每房差异化;都是干净平板,铺在 Back 层,动物可站)
    private const int FloorWood = 12;      // 经典平木板(鸡/兔)
    private const int FloorLight = 46;     // 浅色平板(鸭/羊)
    private const int FloorMid = 56;       // 中色平板(鸵鸟/猪)
    private const int FloorWarm = 54;      // 暖色平板(恐龙/牛)

    // coopTiles 平铺干草堆(Back 层装饰,无框、平贴地面,营造畜棚氛围;不挡路,动物可踩)
    private static readonly int[] HayScatter = { 13, 14, 15, 17, 18, 21, 22 };

    // 每房主题:地板 + 干草种子(让同类房间每次生成一致)
    private static (int floor, int seed) Theme(RoomType room) => room switch
    {
        RoomType.Chicken => (FloorWood, 101),
        RoomType.Duck => (FloorLight, 102),
        RoomType.Rabbit => (FloorWood, 103),
        RoomType.Dinosaur => (FloorWarm, 104),
        RoomType.Ostrich => (FloorMid, 105),
        RoomType.Pig => (FloorMid, 106),
        RoomType.Goat => (FloorLight, 107),
        RoomType.Cow => (FloorWarm, 108),
        _ => (FloorWood, 100),
    };

    public static void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        foreach (var def in RoomDefinitions.All)
        {
            if (e.Name.IsEquivalentTo("Maps/" + def.MapName))
            {
                e.LoadFrom(() => BuildRoomMap(def.Room), AssetLoadPriority.Medium);
                return;
            }
        }
    }

    public static Map BuildRoomMap(RoomType roomType = RoomType.Chicken)
    {
        var (map, back, buildings, front, paths, alwaysFront, sheet) =
            BarnMapRecipe.CreateMapShell(Width, Height);
        var (floorTile, seed) = Theme(roomType);

        // 地板:全铺该房主题地板
        BarnMapRecipe.FillFloor(back, sheet, Width, Height, floorTile);

        // 顶部 3 行墙体(顶墙框 + 墙带 + 墙裙),北墙中央入口(1 格门洞,DoorX)
        int[] windows = { 3, 7, 11 };
        int[] northDoors = { DoorX };
        BarnMapRecipe.BuildWalls(back, buildings, sheet, Width, Height, northDoors, windows);
        BarnMapRecipe.CutNorthDoor(buildings, DoorX);
        BarnMapRecipe.AddWallDecor(buildings, sheet, Width, cobweb: true, hook: true);

        // 封死左右两列(房间无侧门;防穿墙:isTilePassable 对越界/null tile 当 passable)
        BarnMapRecipe.SealSides(buildings, sheet, Width, Height);

        // 边界环:底行封底,只留中央 1 格出口门洞(x=DoorX,南墙底边出口)。门框柱收口。
        int[] southDoors = { DoorX };
        BarnMapRecipe.BuildBoundary(buildings, sheet, Width, Height, southDoors);
        BarnMapRecipe.PlaceWallPost(buildings, sheet, DoorX - 1, DoorY, westFacing: true);
        BarnMapRecipe.PlaceWallPost(buildings, sheet, DoorX + 1, DoorY, westFacing: false);

        // 干草槽:Back 层 y=3(活动区第一行,墙裙 y2 之下,无遮挡)加 Trough 属性
        // (AnimalHouse.feedAllAnimals 扫描 "Trough"/"Back" 属性喂食),并铺干草块(18)做视觉标识。
        // 全宽一排(动物区就在中间大片区域),留出中央走道 x=6..8。
        for (int x = 2; x <= Width - 3; x++)
        {
            if (x >= 6 && x <= 8) continue;   // 中央走道(3列)留空
            back.Tiles[x, 3] = new StaticTile(back, sheet, BlendMode.Alpha, 18);
            back.Tiles[x, 3].Properties["Trough"] = "T";
        }
        // 中央走道【统一地板】(不铺浅色板 —— 用户要求地板统一,不留不同颜色)。
        // 走道就是主题地板(已全铺),北入口→南出口自然贯通。

        // NOTE:栅栏用【原版 Fence 对象】(RoomManager.BuildFences 建房间时放,原版栅栏+栅栏门视觉),
        // 不在地图铺自定义栅栏 tile —— 保持原版栅栏外观(用户要求)。

        // 干草点缀:在中央大片区域(y>=4)撒几撮干草,营造畜棚氛围(每房种子固定)。
        var rng = new System.Random(seed);
        int scatterCount = 5;
        for (int i = 0; i < scatterCount; i++)
        {
            int x = rng.Next(2, Width - 2);
            int y = rng.Next(4, Height - 2);
            if (x >= 6 && x <= 8) continue;   // 中央走道不留
            int hay = HayScatter[rng.Next(HayScatter.Length)];
            back.Tiles[x, y] = new StaticTile(back, sheet, BlendMode.Alpha, hay);
        }

        // AutoFeed 属性(AnimalHouse 自动喂食:有 Trough 槽时直接消耗槽内干草)
        map.Properties["AutoFeed"] = "T";

        // 养殖场房间标记(原版类型 + 地图属性识别,不用自定义类避免存档序列化崩溃)
        AnimalBarnLocations.MarkRoom(map, roomType);

        // ProduceArea 属性:动物随机站位矩形(原版格式:空格分隔 "x y w h")。
        // 注:1.6 游戏已不用此属性(全程序集无 ProduceArea 字面量),保留只为兼容旧版/其他模组读取。
        map.Properties["ProduceArea"] = "2 3 11 6";

        // 出楼 warp:门洞中心 -> Farm。updateWarps() 读取本属性,ParentBuilding.updateInteriorWarps()
        // 把 TargetName=="Farm" 的 warp 改写为建筑 HumanDoor 绝对坐标。无此属性进入房间即崩溃。
        // NOTE:原版 Warp 属性【单格触发】,多 warp 用换行分隔(不能用 |,会把整个属性解析坏 → 无出口)。
        map.Properties["Warp"] = $"{DoorX} {DoorY} Farm 0 0";

        return map;
    }
}
