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

    /// <summary>门厅门(唯一):西墙(凹进,门后是墙,杜绝踩空) x=1, y=4。
    /// 玩家走到这格 → 触发 warp 进【门厅】(LobbyDoors.TryEnterDoor)。</summary>
    public static readonly Point HallDoor = new(1, 4);

    /// <summary>中枢操作台位置(大堂中央,玩家点击打开中枢菜单)。</summary>
    public static readonly Point HubTile = new(6, 4);

    /// <summary>是否中枢台 tile(玩家点击打开中枢菜单)。</summary>
    public static bool IsHubTile(int x, int y) => x == HubTile.X && y == HubTile.Y;

    /// <summary>是否门厅门 tile(玩家站上 → 进门厅)。</summary>
    public static bool IsHallDoorTile(int x, int y) => x == HallDoor.X && y == HallDoor.Y;

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

        // 顶部 3 行墙体(顶墙框 + 墙带 + 墙裙),北墙无门(统一入口在门厅,这里只有窗+装饰)
        int[] windows = { 3, 6, 10 };   // 墙带窗
        BarnMapRecipe.BuildWalls(back, buildings, sheet, Width, Height, northDoorXs: null, windows);
        BarnMapRecipe.AddWallDecor(buildings, sheet, Width, cobweb: true, hook: true);

        // 门厅门:西墙凹进门(x=1, y=4),门后 x=0 是实心墙(杜绝踩空)。
        // 整列封死 + 单格挖开。
        BarnMapRecipe.SealSides(buildings, sheet, Width, Height);
        buildings.Tiles[1, 4] = null;   // 门洞(玩家站上 → warp 进门厅)

        // 边界环:底行封底,只留中央 1 格出口门洞(x=DoorX),两侧门框柱收口。
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

        // 门洞视觉:南出口+门厅门口 Back 层保留木地板(门口),避免看起来像破洞。
        back.Tiles[DoorX, DoorY] = new StaticTile(back, sheet, BlendMode.Alpha, FloorWalkway);
        back.Tiles[HallDoor.X, HallDoor.Y] = new StaticTile(back, sheet, BlendMode.Alpha, FloorWalkway);

        // 门厅门口挂一块小牌(鸡鸭兔恐龙鸵鸟猪山羊牛 8 合一? 挂"门厅"牌 → 用 0 号牌占位,终端在门厅内)。
        // NOTE:门厅内终端会列全部房间,门口只留一个指示牌(挂钉样式)。

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
