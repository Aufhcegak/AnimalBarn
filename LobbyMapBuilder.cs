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
/// </remarks></summary>
public static class LobbyMapBuilder
{
    public const string MapAssetName = "xiepe.AnimalBarn.Lobby";  // SMAPI asset name (Maps/ prefix added by BuildingData.IndoorMap)
    public const int Width = 13;
    public const int Height = 9;

    // 大堂地板(coopTiles 干净木地板;与原版一致用 12,中央走道可用 46 提亮)
    private const int FloorWood = 12;
    private const int FloorWalkway = 46;   // 浅色木板,铺入口→中枢的走道

    /// <summary>底部门洞中心 tile X(供后续任务在入口铺地板/交互用)。</summary>
    public const int DoorX = Width / 2;
    public const int DoorY = Height - 1;

    /// <summary>8 个房间门(墙缺口):(房间, 门洞 tile 坐标)。玩家站到门洞格 → LobbyDoors.TryEnterDoor warp 进房间。
    /// 北墙 3 门在墙带行 y=1(y0 顶墙挡着,门外是墙,安全)。
    /// 西/东门【不再贴地图边缘】(贴边的门外一格越界 → isTilePassable(null)=passable → 一步踩空),
    /// 改成凹进来一格(x=1 / x=Width-2),门后(x=0 / x=Width-1)仍是墙 → 物理上不可能走出地图,杜绝踩空。</summary>
    public static readonly (RoomType Room, int X, int Y)[] DoorPositions =
    {
        (RoomType.Chicken,  3, 1),   // 北墙(墙带行 y=1)
        (RoomType.Duck,     6, 1),
        (RoomType.Rabbit,   10, 1),  // 北墙(避开挂钉 x=9,挪到 x=10)
        (RoomType.Dinosaur, 1, 4),   // 西墙(凹进:门后 x=0 是墙)
        (RoomType.Goat,     1, 5),   // 西墙
        (RoomType.Cow,      1, 7),   // 西墙
        (RoomType.Ostrich,  11, 4),  // 东墙(凹进:门后 x=12 是墙)
        (RoomType.Pig,      11, 7),  // 东墙
    };

    private static bool IsDoorTile(int x, int y) => DoorPositions.Any(d => d.X == x && d.Y == y);

    /// <summary>中枢操作台位置(大堂中央,玩家点击打开中枢菜单)。</summary>
    public static readonly Point HubTile = new(6, 4);

    /// <summary>是否中枢台 tile(玩家点击打开中枢菜单)。</summary>
    public static bool IsHubTile(int x, int y) => x == HubTile.X && y == HubTile.Y;

    /// <summary>房间 → plaque tilesheet 列(与 assets/AnimalBarnPlaques.png 的图标顺序一致:
    /// 0鸡 1鸭 2兔 3恐龙 4鸵鸟 5猪 6山羊 7牛)。羊场(Goat,绵羊+山羊共用)挂山羊牌。</summary>
    private static int PlaqueIndex(RoomType room) => room switch
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

        // 顶部 3 行墙体(顶墙框 + 墙带 + 墙裙),北墙 3 门洞 + 窗户点缀
        // 北墙门避开挂钉(x=2 与 x=Width-4=9 是挂钉位):北门选 x=3,6,9 → 9 被挂钉占了,改用 x=10(窗位移到 1,2,11)
        int[] northDoors = { 3, 6, 10 };
        int[] windows = { 1, 2, 11 };   // 墙带窗(避开门洞与挂钉 x=2,x=9)
        BarnMapRecipe.BuildWalls(back, buildings, sheet, Width, Height, northDoors, windows);
        foreach (int x in northDoors) BarnMapRecipe.CutNorthDoor(buildings, x);
        BarnMapRecipe.AddWallDecor(buildings, sheet, Width, cobweb: true, hook: true);

        // 西/东墙门【凹进门龛】:门洞在内列(x=1 / x=Width-2),门后(x=0 / x=Width-1)保持实心墙。
        // 这样玩家朝门走一格就 warp,绝不可能走出地图(门后是墙,无越界踩空)。左右两列整列封死。
        // 西门 y=4(恐龙),5(山羊),7(牛);东门 y=4(鸵鸟),7(猪)。
        BarnMapRecipe.SealSides(buildings, sheet, Width, Height);   // 整列封死(无门洞缺口)

        // 边界环:底行封底,只留中央 1 格出口门洞(x=DoorX)。此前 3 格缺口(x=5,6,7)只有中间格有 warp,
        // 玩家走到两侧缺口格再往下就踩空 → 收窄成 1 格(原版门宽),两侧铺门框柱收口(视觉+物理双保险)。
        int[] southDoors = { DoorX };
        BarnMapRecipe.BuildBoundary(buildings, sheet, Width, Height, southDoors);
        BarnMapRecipe.PlaceWallPost(buildings, sheet, DoorX - 1, DoorY, westFacing: true);
        BarnMapRecipe.PlaceWallPost(buildings, sheet, DoorX + 1, DoorY, westFacing: false);

        // 入口→中枢走道:浅色木板引导视线(x=DoorX 一列,从门口 y=8 到中枢 y=4)
        for (int y = DoorY - 1; y >= HubTile.Y; y--)
            back.Tiles[DoorX, y] = new StaticTile(back, sheet, BlendMode.Alpha, FloorWalkway);
        // 中枢台周围一小片走道(3 格宽)
        for (int x = HubTile.X - 1; x <= HubTile.X + 1; x++)
            back.Tiles[x, HubTile.Y] = new StaticTile(back, sheet, BlendMode.Alpha, FloorWalkway);

        // 门洞视觉:每个门洞 Back 层保留木地板(门口),避免看起来像破洞。
        foreach (var (_, x, y) in DoorPositions)
            back.Tiles[x, y] = new StaticTile(back, sheet, BlendMode.Alpha, FloorWalkway);

        // 门口动物挂牌:加 plaque tilesheet(8 个动物图标),每个门挂对应房间的牌子,一眼看出是哪个房间。
        // 挂 Front 层(前景,有立体感);北墙门(y1)挂门楣(y0);凹进的西/东门挂在门后墙(x=0 / x=Width-1)正上方。
        var plaqueSheet = BarnMapRecipe.AddExtraTileSheet(map, "plaques", "xiepe.AnimalBarn/Plaques", 8, 1);
        foreach (var (room, x, y) in DoorPositions)
        {
            int plaqueIndex = PlaqueIndex(room);          // 0..7 对应 tilesheet 列
            int plaqueX = x, plaqueY = y - 1;             // 默认:门洞上方一格
            if (y == 1) { plaqueY = 0; }                  // 北墙门 → 顶墙框行 y0
            else if (x == 1) plaqueX = 0;                 // 凹进西门 → 挂在 x=0 后墙正上方
            else if (x == Width - 2) plaqueX = Width - 1; // 凹进东门 → 挂在 x=Width-1 后墙正上方
            if (plaqueY < 0) plaqueY = 0;
            front.Tiles[plaqueX, plaqueY] = new StaticTile(front, plaqueSheet, BlendMode.Alpha, plaqueIndex);
        }

        // 中枢操作台(大堂中央):不再是干草堆。电脑桌造型由运行时对象放(RoomManager/大堂初始化),
        // 这里只保留可点击的中枢 tile(走道地板),不占 Buildings 层(否则会挡路且是乱码干草块)。
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
