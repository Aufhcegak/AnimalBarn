using xTile;
using xTile.Dimensions;
using xTile.Layers;
using xTile.Tiles;

namespace AnimalBarn;

/// <summary>大堂/房间共用的地图构建配方。照搬原版畜棚(Maps/Barn)的 coopTiles 墙体结构,
/// 只改地板纹理与装饰,保证渲染干净(此前错用 townInterior 家具表导致满屏乱码)。
/// 所有图层 tile 尺寸必须 64x64(static 共享,游戏按 64 算碰撞边界);出口 warp 由调用方写入。</summary>
internal static class BarnMapRecipe
{
    // coopTiles (Maps\coopTiles, 4 列) 结构索引 —— 与原版 Barn 完全一致:
    private const int CornerTL = 0;   // 顶墙左上角
    private const int WallTopH = 1;   // 顶墙横条
    private const int CornerTR = 2;   // 顶墙右上角
    private const int WallLeft = 4;   // 左竖墙
    private const int WallRight = 6;  // 右竖墙
    private const int CornerBL = 8;   // 左下角(墙裙收口)
    private const int Baseboard = 48; // 底横墙(封底/防穿墙)
    private const int WoodPanel = 16; // 木镶板墙(墙带)
    private const int Wainscot = 20;  // 墙裙
    private const int Window = 24;    // 窗户(北墙装饰)
    private const int DecoCobweb = 60;// 墙角蛛网(原版 Barn 装饰)
    private const int DecoHook = 61;  // 墙上挂钉

    /// <summary>建 5 层(缺一崩)+ 加 coopTiles 贴图集。返回 (map, back, buildings, front, paths, alwaysFront, sheet)。</summary>
    public static (Map map, Layer back, Layer buildings, Layer front, Layer paths, Layer alwaysFront, TileSheet sheet)
        CreateMapShell(int width, int height)
    {
        var map = new Map();
        var back = new Layer("Back", map, new Size(width, height), new Size(64, 64));
        var buildings = new Layer("Buildings", map, new Size(width, height), new Size(64, 64));
        var front = new Layer("Front", map, new Size(width, height), new Size(64, 64));
        var paths = new Layer("Paths", map, new Size(width, height), new Size(64, 64));
        var alwaysFront = new Layer("AlwaysFront", map, new Size(width, height), new Size(64, 64));
        map.AddLayer(back);
        map.AddLayer(buildings);
        map.AddLayer(front);
        map.AddLayer(paths);
        map.AddLayer(alwaysFront);
        var sheet = new TileSheet("barn", map, "Maps\\coopTiles", new Size(4, 19), new Size(16, 16));
        map.AddTileSheet(sheet);
        return (map, back, buildings, front, paths, alwaysFront, sheet);
    }

    /// <summary>全图铺地板(Back 层)。floorTile 为地板索引(原版木地板 = 12)。</summary>
    public static void FillFloor(Layer back, TileSheet sheet, int width, int height, int floorTile)
    {
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                back.Tiles[x, y] = new StaticTile(back, sheet, BlendMode.Alpha, floorTile);
    }

    /// <summary>顶部 3 行墙体结构(与原版 Barn 一致):
    /// y0 Buildings = 顶墙框(角 0/2 + 横 1); y1 Buildings = 墙带(16,可间插窗 24/蛛网 60/挂钉 61);
    /// y2 Buildings = 墙裙(20); 左右竖墙(4/6)沿全高。门窗缺口由 doorXs 指定(y1 留空)。</summary>
    public static void BuildWalls(Layer back, Layer buildings, TileSheet sheet, int width, int height,
        int[]? northDoorXs = null, int[]? windows = null)
    {
        northDoorXs ??= System.Array.Empty<int>();
        windows ??= System.Array.Empty<int>();

        // 顶墙框(Buildings y0):角 + 横条
        buildings.Tiles[0, 0] = new StaticTile(buildings, sheet, BlendMode.Alpha, CornerTL);
        for (int x = 1; x < width - 1; x++)
            buildings.Tiles[x, 0] = new StaticTile(buildings, sheet, BlendMode.Alpha, WallTopH);
        buildings.Tiles[width - 1, 0] = new StaticTile(buildings, sheet, BlendMode.Alpha, CornerTR);

        // 墙带(Buildings y1):木镶板,间插窗户/蛛网/挂钉;门洞 x 留空
        for (int x = 1; x < width - 1; x++)
        {
            if (System.Array.IndexOf(northDoorXs, x) >= 0) continue; // 门洞
            int tile = WoodPanel;
            if (System.Array.IndexOf(windows, x) >= 0) tile = Window;
            buildings.Tiles[x, 1] = new StaticTile(buildings, sheet, BlendMode.Alpha, tile);
        }

        // 墙裙(Buildings y2):全宽
        for (int x = 1; x < width - 1; x++)
            buildings.Tiles[x, 2] = new StaticTile(buildings, sheet, BlendMode.Alpha, Wainscot);

        // 左右竖墙(Buildings 全高,y0/y2 已有则由顶墙/墙裙覆盖,这里补 y1..height-1)
        for (int y = 1; y < height; y++)
        {
            if (buildings.Tiles[0, y] == null)
                buildings.Tiles[0, y] = new StaticTile(buildings, sheet, BlendMode.Alpha, WallLeft);
            if (buildings.Tiles[width - 1, y] == null)
                buildings.Tiles[width - 1, y] = new StaticTile(buildings, sheet, BlendMode.Alpha, WallRight);
        }

        // 北墙 Back 层底座(原版 Barn:Back y1=16, y2=20 沿墙;左右竖墙在 Back 也铺)—— 让墙有厚度感。
        back.Tiles[0, 1] = new StaticTile(back, sheet, BlendMode.Alpha, WoodPanel);
        back.Tiles[width - 1, 1] = new StaticTile(back, sheet, BlendMode.Alpha, WoodPanel);
        back.Tiles[0, 2] = new StaticTile(back, sheet, BlendMode.Alpha, Wainscot);
        back.Tiles[width - 1, 2] = new StaticTile(back, sheet, BlendMode.Alpha, Wainscot);
    }

    /// <summary>边界环防穿墙:底行全 Baseboard(48),门洞缺口除外。
    /// isTilePassable 对越界 tile 返回 null→passable(玩家会走出地图边进虚空),
    /// 所以边界所有非门洞 tile 都必须在 Buildings 层铺阻挡。 southDoorXs 为底部门洞(留空)。</summary>
    public static void BuildBoundary(Layer buildings, TileSheet sheet, int width, int height, int[]? southDoorXs = null)
    {
        southDoorXs ??= System.Array.Empty<int>();
        int by = height - 1;
        for (int x = 0; x < width; x++)
        {
            if (System.Array.IndexOf(southDoorXs, x) >= 0) continue; // 门洞留空(可通行)
            buildings.Tiles[x, by] = new StaticTile(buildings, sheet, BlendMode.Alpha, Baseboard);
        }
    }

    /// <summary>封死左右两列(除指定门洞 y):竖墙 tile 铺满,防止玩家从侧墙缝隙走出地图。
    /// westDoorYs/eastDoorYs 为侧墙门洞 y(留空)。</summary>
    public static void SealSides(Layer buildings, TileSheet sheet, int width, int height,
        int[]? westDoorYs = null, int[]? eastDoorYs = null)
    {
        westDoorYs ??= System.Array.Empty<int>();
        eastDoorYs ??= System.Array.Empty<int>();
        for (int y = 0; y < height; y++)
        {
            if (System.Array.IndexOf(westDoorYs, y) < 0 && buildings.Tiles[0, y] == null)
                buildings.Tiles[0, y] = new StaticTile(buildings, sheet, BlendMode.Alpha, WallLeft);
            if (System.Array.IndexOf(eastDoorYs, y) < 0 && buildings.Tiles[width - 1, y] == null)
                buildings.Tiles[width - 1, y] = new StaticTile(buildings, sheet, BlendMode.Alpha, WallRight);
        }
    }

    /// <summary>墙上装饰:蛛网/挂钉点缀北墙带(可选,原版 Barn 有)。</summary>
    public static void AddWallDecor(Layer buildings, TileSheet sheet, int width, bool cobweb, bool hook)
    {
        if (cobweb && width > 3)
            buildings.Tiles[2, 1] = new StaticTile(buildings, sheet, BlendMode.Alpha, DecoCobweb);
        if (hook && width > 6)
            buildings.Tiles[width - 4, 1] = new StaticTile(buildings, sheet, BlendMode.Alpha, DecoHook);
    }

    /// <summary>侧墙门洞(已弃用贴边方案):在西(x=0)或东(x=width-1)墙 y 处挖 1 格门(竖墙留空)。</summary>
    public static void CutSideDoor(Layer buildings, int width, int y, bool west)
    {
        int x = west ? 0 : width - 1;
        buildings.Tiles[x, y] = null;
    }

    /// <summary>在指定 tile 放一根墙柱(竖墙样式,Buildings 层 → 阻挡)。用于把凹进来的侧门洞两侧封成门龛。</summary>
    public static void PlaceWallPost(Layer buildings, TileSheet sheet, int x, int y, bool westFacing)
    {
        buildings.Tiles[x, y] = new StaticTile(buildings, sheet, BlendMode.Alpha, westFacing ? WallLeft : WallRight);
    }

    /// <summary>北墙门洞:y1 墙带留空(玩家从南向北走进门洞)。上方 y0 顶墙保留 → 1 格高门洞。</summary>
    public static void CutNorthDoor(Layer buildings, int x)
    {
        buildings.Tiles[x, 1] = null;
    }

    /// <summary>给地图追加一块 tilesheet(如原版 farm 围栏 Maps\farm,与 coopTiles 并存)。
    /// 返回新 sheet;调用方用 StaticTile(layer, newSheet, ...) 引用其索引。</summary>
    public static TileSheet AddExtraTileSheet(Map map, string id, string texturePath, int cols, int rows)
    {
        var sheet = new TileSheet(id, map, texturePath, new Size(cols, rows), new Size(16, 16));
        map.AddTileSheet(sheet);
        return sheet;
    }
}
