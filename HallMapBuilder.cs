using StardewModdingAPI;
using StardewModdingAPI.Events;
using xTile;
using xTile.Dimensions;
using xTile.Layers;
using xTile.Tiles;

namespace AnimalBarn;

/// <summary>代码生成【门厅】地图:统一入口 —— 玩家从大堂进门厅,点房间选择终端,弹菜单选房间直接进。
/// 门厅小(11x9):中央一台终端(运行时对象,点它弹 RoomSelectMenu),南出口回大堂。
/// 玩家不再走 8 扇容易踩空/锁死的门 —— 一次选择 UI 直达,彻底解决门系统问题。</summary>
public static class HallMapBuilder
{
    public const string MapAssetName = "xiepe.AnimalBarn.Hall";
    public const int Width = 11;
    public const int Height = 9;

    private const int FloorWood = 12;
    private const int FloorWalkway = 46;

    /// <summary>南出口门洞(回大堂)。</summary>
    public const int DoorX = Width / 2;
    public const int DoorY = Height - 1;

    /// <summary>房间选择终端位置(门厅中央,玩家点击 → RoomSelectMenu)。</summary>
    public static readonly Microsoft.Xna.Framework.Point TerminalTile = new(5, 4);

    /// <summary>是否终端 tile(玩家点击弹房间选择菜单)。</summary>
    public static bool IsTerminalTile(int x, int y) => x == TerminalTile.X && y == TerminalTile.Y;

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

        BarnMapRecipe.FillFloor(back, sheet, Width, Height, FloorWood);

        // 顶部 3 行墙体 + 窗户点缀(无北门)
        int[] windows = { 2, 5, 8 };
        BarnMapRecipe.BuildWalls(back, buildings, sheet, Width, Height, northDoorXs: null, windows);
        BarnMapRecipe.AddWallDecor(buildings, sheet, Width, cobweb: true, hook: true);

        // 左右两列整列封死(防穿墙)
        BarnMapRecipe.SealSides(buildings, sheet, Width, Height);

        // 底行封底,只留中央 1 格出口门洞(x=DoorX) + 门框柱收口
        int[] southDoors = { DoorX };
        BarnMapRecipe.BuildBoundary(buildings, sheet, Width, Height, southDoors);
        BarnMapRecipe.PlaceWallPost(buildings, sheet, DoorX - 1, DoorY, westFacing: true);
        BarnMapRecipe.PlaceWallPost(buildings, sheet, DoorX + 1, DoorY, westFacing: false);

        // 出口→终端走道(浅色板)
        for (int y = DoorY - 1; y >= TerminalTile.Y; y--)
            back.Tiles[DoorX, y] = new StaticTile(back, sheet, BlendMode.Alpha, FloorWalkway);
        // 终端周围走道(3 格宽)
        for (int x = TerminalTile.X - 1; x <= TerminalTile.X + 1; x++)
            back.Tiles[x, TerminalTile.Y] = new StaticTile(back, sheet, BlendMode.Alpha, FloorWalkway);
        back.Tiles[DoorX, DoorY] = new StaticTile(back, sheet, BlendMode.Alpha, FloorWalkway);

        // AutoFeed 属性(无动物,统一加无妨)
        map.Properties["AutoFeed"] = "T";

        // 门厅标记(识别为门厅,不是大堂/房间)
        AnimalBarnLocations.MarkHall(map);

        // 出口 warp → Farm(会被 updateInteriorWarps 改写? 门厅不是建筑室内,
        // 用 RunManager 创建时把 warp 目标改写为大堂/或保持。这里先写回大堂门厅门? 不——
        // 门厅是自建地点,不挂建筑室内序列,ParentBuilding 是楼。改写逻辑在 RoomManager.GetOrCreateHall。
        map.Properties["Warp"] = $"{DoorX} {DoorY} Farm 0 0";

        return map;
    }
}
