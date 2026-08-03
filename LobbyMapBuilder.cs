using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using xTile;
using xTile.Dimensions;
using xTile.Layers;
using xTile.Tiles;

namespace AnimalBarn;

/// <summary>代码生成大堂地图。墙体照搬原版畜棚(coopTiles)配方,地板/装饰全新布置,渲染干净。
/// <remarks>
/// 关键已验证事实(反编译 xTile.dll + Stardew Valley.dll,1.6.15):
/// 1. Layer.m_tileSize 是共享 static —— 运行时地图 tile 尺寸必须是 64x64,否则碰撞边界算错穿墙。
/// 2. isTilePassable() 对越界 tile 返回 null→passable → 对策:Buildings 层最外圈全部铺阻挡 tile。
/// 3. 出楼 warp 必须写进地图 "Warp" 属性:updateWarps() 读取后 ParentBuilding.updateInteriorWarps()
///    把目标改写为建筑 HumanDoor 位置。仅留门洞不会有出口。
/// 4. 墙/地板 tilesheet 是 Maps\coopTiles(原版畜棚用),不是 townInterior(那是纯家具表,用了必乱码)。
/// 5. 用户实测门系统反复失效(锁永不清) → 改成"统一入口":大堂只有 1 扇门进【门厅】,
///    门厅放一台终端,点终端弹房间选择菜单,选房间直接进。杜绝 8 门踩空/锁死问题。
/// </remarks></summary>
public static class LobbyMapBuilder
{
    public const string MapAssetName = "xiepe.AnimalBarn.Lobby";  // SMAPI asset name (Maps/ prefix added by BuildingData.IndoorMap)
    public const int Width = 13;
    public const int Height = 9;

    // 大堂地板(coopTiles 干净木地板;与原版一致用 12,中央走道可用 46 提亮)
    private const int FloorWood = 12;
    private const int FloorWalkway = 46;   // 浅色木板,铺入口→中枢的走道

    /// <summary>底部门洞中心 tile X(南出口,回农场)。</summary>
    public const int DoorX = Width / 2;
    public const int DoorY = Height - 1;

    /// <summary>动物房门(统一入口):北墙中央 (x=DoorX, y=1 墙带行),门洞上方 y=0 顶墙保留。
    /// 玩家右键门 → 直接弹【房间选择菜单】选房间进(统一门形态,不要 9 扇门)。
    /// 门洞挖到底(y1+y2 贯通),门贴图+挡人由运行时门对象处理。</summary>
    public static readonly Point HallDoor = new(DoorX, 1);

    /// <summary>中枢操作台位置(大堂中央,玩家点击打开中枢菜单)。</summary>
    public static readonly Point HubTile = new(6, 4);

    /// <summary>中枢交互区:电脑台 (6,4) 及周围一圈(3x3)。玩家点电脑台本身、或被电脑挡住
    /// 只能点到旁边时,都能打开中枢菜单 —— 避免"点了没反应"。</summary>
    public static bool IsHubTile(int x, int y)
        => Math.Abs(x - HubTile.X) <= 1 && Math.Abs(y - HubTile.Y) <= 1;

    /// <summary>门厅门触发区(已废弃门厅版):北墙中央门洞 (DoorX,1)。右键门 → 选房间菜单。
    /// 玩家往上走向门,走到门口附近就有反应(不用精确踩门洞格)。</summary>
    public static bool IsHallDoorTile(int x, int y)
        => x == HallDoor.X && (y == HallDoor.Y || y == HallDoor.Y + 1 || y == HallDoor.Y + 2);

    /// <summary>房间 → plaque tilesheet 列(与 assets/AnimalBarnPlaques.png 的图标顺序一致:
    /// 0鸡 1鸭 2兔 3恐龙 4鸵鸟 5猪 6山羊 7牛)。羊场(Goat,绵羊+山羊共用)挂山羊牌。</summary>
    public static int PlaqueIndex(RoomType room) => room switch
    {
        RoomType.Chicken => 0,
        RoomType.Duck => 1,
        RoomType.Rabbit => 2,
        RoomType.Dinosaur => 3,
        RoomType.Ostrich => 4,
        RoomType.Pig => 5,
        RoomType.Goat => 6,
        RoomType.Cow => 7,
        _ => 0,
    };

    public static void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.Name.IsEquivalentTo("Maps/" + MapAssetName))
        {
            e.LoadFrom(() => BuildMap(), AssetLoadPriority.Medium);
        }
    }

    public static Map BuildMap()
    {
        var (map, back, buildings, front, paths, alwaysFront, sheet) =
            BarnMapRecipe.CreateMapShell(Width, Height);

        // 地板:全铺干净木地板
        BarnMapRecipe.FillFloor(back, sheet, Width, Height, FloorWood);

        // 顶部 3 行墙体(顶墙框 + 墙带 + 墙裙),北墙中央动物房门 + 窗户点缀
        int[] northDoors = { DoorX };
        int[] windows = { 2, 4, 8, 10 };
        BarnMapRecipe.BuildWalls(back, buildings, sheet, Width, Height, northDoors, windows);
        BarnMapRecipe.AddWallDecor(buildings, sheet, Width, cobweb: true, hook: true);

        // 动物房门:门洞 1 格(y1 挖空,y2 墙裙保留=物理墙)。
        // 门贴图 = townInterior tile 165(原版农舍门板,反射 dump 确认 FarmHouse Front 层门洞用 165)。
        // 铺 Front 层门洞格(门板)。doors 字典负责挡人+动画(EnsureDoor)。
        BarnMapRecipe.CutNorthDoor(buildings, DoorX);
        back.Tiles[HallDoor.X, HallDoor.Y] = new StaticTile(back, sheet, BlendMode.Alpha, FloorWalkway);   // 门洞地板
        // 门板贴图:Front 层门洞格(像农舍门板)
        var townSheet = BarnMapRecipe.AddExtraTileSheet(map, "townDoor", "Maps\\townInterior", 32, 68);
        front.Tiles[HallDoor.X, HallDoor.Y] = new StaticTile(front, townSheet, BlendMode.Alpha, 165);   // 原版门板

        // 左右两列整列封死(防穿墙)
        BarnMapRecipe.SealSides(buildings, sheet, Width, Height);

        // 边界环:底行封底,只留中央 1 格出口门洞(x=DoorX,回农场),两侧门框柱收口。
        int[] southDoors = { DoorX };
        BarnMapRecipe.BuildBoundary(buildings, sheet, Width, Height, southDoors);
        BarnMapRecipe.PlaceWallPost(buildings, sheet, DoorX - 1, DoorY, westFacing: true);
        BarnMapRecipe.PlaceWallPost(buildings, sheet, DoorX + 1, DoorY, westFacing: false);

        // 地板统一:全铺木地板(FillFloor 已做),不留走道浅色板(用户要求统一)。
        // 中枢台位置由 IsHubTile 交互识别,不需要视觉区分。

        // 中枢电脑台【视觉 = 运行时对象】(HubConsole.EnsureComputer 放原版 Farm Computer,
        // 用 "239" 无前缀 ID 构造 → 正常原版电脑贴图 + 挡路 + LookupAnything F1 可查)。
        // 这里不铺地图贴图(避免双层)。点击由 IsHubTile 3x3 触发(BarnPatches)。

        // 南出口门口地板
        back.Tiles[DoorX, DoorY] = new StaticTile(back, sheet, BlendMode.Alpha, FloorWalkway);

        // 中枢操作台(大堂中央):电脑桌造型由运行时对象放(HubConsole.EnsurePlaced),
        // 这里只保留可点击的中枢 tile(走道地板),不占 Buildings 层。
        // IsHubTile(x,y) 仍是打开中枢菜单的触发点(BarnPatches.BeforeCheckAction)。

        // AutoFeed 属性(AnimalHouse 自动喂食用;大堂无动物,但统一加无妨)
        map.Properties["AutoFeed"] = "T";

        // 养殖场标记(原版类型 + 地图属性识别,不用自定义类避免存档序列化崩溃)
        AnimalBarnLocations.MarkLobby(map);

        // 出楼 warp:门洞中心 -> 楼外 HumanDoor 处。updateWarps() 读取本属性,
        // 然后 ParentBuilding.updateInteriorWarps() 把 TargetName=="Farm" 的 warp 改写为 HumanDoor 绝对坐标。
        // toX/toY 用占位(0 0)即可 —— 会被改写。此 warp 同时是建筑入口的落点。
        map.Properties["Warp"] = $"{DoorX} {DoorY} Farm 0 0";

        return map;
    }
}
