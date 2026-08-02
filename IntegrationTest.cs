using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;

namespace AnimalBarn;

/// <summary>无头集成测试:autotest.txt 存在时,标题画面自动跑并写结果到 autotest_result.txt。
/// 配方来自 JunimoTaskScheduler(Context.IsGameLaunched && !Context.IsWorldReady 且不碰存档)。</summary>
public static class IntegrationTest
{
    public static bool Pending;

    public static void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Pending) return;
        if (!Context.IsGameLaunched || Context.IsWorldReady) return;
        Pending = false;

        var results = new List<string>();
        void Check(string name, bool cond) => results.Add((cond ? "PASS " : "FAIL ") + name);

        // 0. 房间地图资产注入在本测试内自注册(不依赖 ModEntry,后续由协调者合并注册)
        ModEntry.Instance.Helper.Events.Content.AssetRequested += RoomMapBuilder.OnAssetRequested;

        try
        {
            // 0.5 原版 Barn/Coop 的门坐标参照(诊断,写进结果文件)
            foreach (var key in new[] { "Barn", "Big Barn", "Deluxe Barn", "Coop" })
                if (Game1.buildingData.TryGetValue(key, out var vd))
                    results.Add($"INFO vanilla {key}: Size={vd.Size.X}x{vd.Size.Y} HumanDoor=({vd.HumanDoor.X},{vd.HumanDoor.Y}) SrcRect={vd.SourceRect} SortTileOffset={vd.SortTileOffset} DrawOffset={vd.DrawOffset}");
            if (Game1.buildingData.TryGetValue("xiepe.AnimalBarn", out var mine))
                results.Add($"INFO mine: Size={mine.Size.X}x{mine.Size.Y} HumanDoor=({mine.HumanDoor.X},{mine.HumanDoor.Y}) SrcRect={mine.SourceRect}");

            // 1. 建筑数据存在
            Check("buildingData has xiepe.AnimalBarn",
                Game1.buildingData.TryGetValue("xiepe.AnimalBarn", out var data) && data.Builder == "Robin");

            // 2. 建筑可实例化
            var b = Building.CreateInstanceFromId("xiepe.AnimalBarn", new Vector2(0, 0));
            Check("building instantiable", b != null && b.buildingType.Value == "xiepe.AnimalBarn");

            // 3. 大堂地图可加载(地图资产注入在 Task 1.4,此时应已注册;原版 AnimalHouse 类型,存档序列化安全)
            var lobby = new StardewValley.AnimalHouse("Maps\\xiepe.AnimalBarn.Lobby", "xiepe.AnimalBarn.Lobby");
            Check("lobby map loads", lobby.map != null && lobby.map.Layers.Count >= 5);

            // 4. 大堂可进(Back 层有地板,门洞处无阻挡)
            Check("lobby floor ok", lobby.map.GetLayer("Back").Tiles[6, 4] != null);

            // 5. 大堂出口 warp 存在(指向 Farm)
            Check("lobby has farm warp", lobby.warps.Any(w => w.TargetName == "Farm"));

            // 5b. 大堂边界封死防穿墙:左右两列【整列】封死(侧门已改为凹进门龛,门后 x=0/12 是墙),
            // 底行除出口(5,6,7)外全部阻挡,顶行全阻挡。
            var lb = lobby.map.GetLayer("Buildings");
            bool sidesSealed = true;
            for (int y = 0; y < LobbyMapBuilder.Height; y++)
            {
                if (lb.Tiles[0, y] == null) sidesSealed = false;                        // 西列整列封死
                if (lb.Tiles[LobbyMapBuilder.Width - 1, y] == null) sidesSealed = false; // 东列整列封死
            }
            Check("lobby sides sealed (full columns)", sidesSealed);
            bool bottomSealed = true;
            for (int x = 0; x < LobbyMapBuilder.Width; x++)
                if (x != LobbyMapBuilder.DoorX && lb.Tiles[x, LobbyMapBuilder.Height - 1] == null) bottomSealed = false;
            Check("lobby bottom sealed", bottomSealed);
            // 顶行(y0)全阻挡
            bool topSealed = true;
            for (int x = 0; x < LobbyMapBuilder.Width; x++)
                if (lb.Tiles[x, 0] == null) topSealed = false;
            Check("lobby top sealed", topSealed);

            // 5d. 每个房间门必须(a)门洞本身可通行(无 Buildings 阻挡→玩家能站上去触发 warp);
            // (b)门后/门外一格必须是墙(绝不能越界/裸奔出图)。这是防"门进不去"和"踩空"的硬检查。
            bool allDoorsOk = true;
            foreach (var (room, dx, dy) in LobbyMapBuilder.DoorPositions)
            {
                bool doorOpen = lb.Tiles[dx, dy] == null;
                // 门后一格:西门(x==1)→(0,dy);东门(x==Width-2)→(Width-1,dy);北门(y==1)→(dx,0)
                int bx = dx, by = dy;
                if (dx == 1) bx = 0; else if (dx == LobbyMapBuilder.Width - 2) bx = LobbyMapBuilder.Width - 1; else by = 0;
                bool behindIsWall = lb.Tiles[bx, by] != null;
                if (!doorOpen || !behindIsWall) allDoorsOk = false;
                results.Add($"INFO door {room} @({dx},{dy}) open={doorOpen} behind({bx},{by})wall={behindIsWall}");
            }
            Check("all room doors open + walled behind", allDoorsOk);

            // 5c. 中枢菜单框源矩形是标准 60x60(可被 drawTextureBox 按 /3 九宫格切,不会碎)
            Check("menu box src 60x60", GetMenuBoxSrc().Width == 60 && GetMenuBoxSrc().Height == 60);

            // 6. 中枢菜单可实例化 + 切页签不崩
            var snap = new HubSnapshot(
                OverallLevel: 1, HayStock: 100, ProduceCount: 5,
                Rooms: new List<RoomSnapshot>
                {
                    new(RoomType.Chicken, "养鸡场", true, 30, 100, 0, 3),
                    new(RoomType.Pig, "养猪场", false, 0, 40, 0, 0),
                },
                CanUpgradeOverall: true, OverallUpgradeCost: 35000, OverallUpgradeUnlocks: "养猪场");
            var menu = new HubMenu(snap);
            Check("hub menu constructs", menu != null);
            Check("hub menu default tab", menu.CurrentTab == HubMenu.Tab.Status);
            menu.receiveLeftClick(menu.xPositionOnScreen + 24 + (HubMenu.TabWidth + HubMenu.TabGap) + HubMenu.TabWidth / 2, menu.yPositionOnScreen + 48 + HubMenu.TabHeight / 2, playSound: false); // 点"升级"页签
            Check("hub menu tab switch", menu.CurrentTab == HubMenu.Tab.Upgrade);
            menu.receiveLeftClick(menu.xPositionOnScreen + 24 + 2 * (HubMenu.TabWidth + HubMenu.TabGap) + HubMenu.TabWidth / 2, menu.yPositionOnScreen + 48 + HubMenu.TabHeight / 2, playSound: false); // 点"商店"页签
            Check("hub menu tab switch 2", menu.CurrentTab == HubMenu.Tab.Shop);

            // 6b. 商店页 9 个动物按钮 hitbox 与绘制对齐:每个按钮中心点应在菜单范围内,
            // 且与 DrawShop 的行 Y 一致(按钮 y = RowTop + i*RowHeight - 5)。点第 1 行按钮应触发
            // BuyAnimal 逻辑(在无 State 的展示构造下应安全返回,不崩)。
            int shopBtnX = menu.xPositionOnScreen + HubMenu.ButtonColXConst + HubMenu.ButtonWidthConst / 2;
            int row0Y = menu.yPositionOnScreen + HubMenu.RowTopConst - 5 + HubMenu.ButtonHeightConst / 2;
            bool row0InMenu = shopBtnX > menu.xPositionOnScreen && shopBtnX < menu.xPositionOnScreen + 1000
                && row0Y > menu.yPositionOnScreen && row0Y < menu.yPositionOnScreen + 620;
            Check("shop row0 button inside menu", row0InMenu);
            // 第 9 行(最后一只动物,羊)按钮也应在菜单内
            int row8Y = menu.yPositionOnScreen + HubMenu.RowTopConst + 8 * HubMenu.RowHeightConst - 5 + HubMenu.ButtonHeightConst / 2;
            Check("shop row8 (sheep) button inside menu", row8Y < menu.yPositionOnScreen + 620);
            menu.receiveLeftClick(shopBtnX, row0Y, playSound: false);   // 点"购买 +1"(无 State → 安全返回)
            Check("shop buy click no-crash (display mode)", true);

            // 7. 8 个房间地图全部可加载且要素齐全(干草槽/自动喂食/生产区/出口)
            foreach (var def in RoomDefinitions.All)
            {
                var room = new StardewValley.AnimalHouse("Maps\\" + def.MapName, def.MapName);
                var map = room.map;
                Check($"room {def.Room} loads", map != null && map.Layers.Count >= 5);
                Check($"room {def.Room} AutoFeed", map != null && map.Properties.ContainsKey("AutoFeed"));
                Check($"room {def.Room} ProduceArea", map != null && map.Properties.ContainsKey("ProduceArea"));
                Check($"room {def.Room} Trough", map?.GetLayer("Back").Tiles[5, 3]?.Properties.ContainsKey("Trough") == true);
                Check($"room {def.Room} Warp", room.warps.Any(w => w.TargetName == "Farm"));
            }

            // 8. 完整业务流程(标题画面用真实对象跑通,不碰存档):
            //    建建筑 → 拿状态 → 买动物进台账 → 结算(产产品/好感/干草)→ 取产品
            var barn = ModEntry.Instance.Barn;
            var building = Building.CreateInstanceFromId("xiepe.AnimalBarn", new Vector2(2, 2));
            Check("flow: building created", building != null);

            var state = barn.GetOrCreate(building);
            state.HayStock = 500;
            state.OverallLevel = 1;   // 1 级:养鸡场解锁

            // 买 3 只鸡进台账(模拟中枢购买)
            var roomState = state.GetRoom(RoomType.Chicken);
            for (int i = 0; i < 3; i++)
            {
                roomState.Animals.Add(new LedgerAnimal
                {
                    Id = 1000 + i, Room = RoomType.Chicken, TypeKey = "White Chicken",
                    AgeDays = 10,   // 成年
                    Friendship = 0, Happiness = 200, Fullness = 0,
                    DaysSinceProduce = 1, ProduceCount = 0, OwnerId = 1,
                });
            }
            Check("flow: 3 chickens bought", roomState.Animals.Count == 3);

            // 结算(模拟睡眠 DayUpdate → SettlementService.SettleRoom)
            var ledger = AnimalLedger.FromRoom(roomState);
            var ctx = new SettleContext(FriendshipGain: UpgradeSystem.FriendshipGainAt(state.OverallLevel), HappinessGain: 20);
            var hay = ledger.SettleDay(ctx, state.HayStock);
            state.HayStock -= hay.HayConsumed;
            ledger.SaveTo(roomState);
            Check("flow: settle produced", roomState.ProduceCount > 0);
            Check("flow: friendship grew", roomState.Animals[0].Friendship > 0);
            Check("flow: hay consumed", hay.HayConsumed > 0 && state.HayStock < 500);

            // 取产品(模拟仓库取走)
            int before = roomState.ProduceCount;
            roomState.ProduceStacks.Clear();
            roomState.ProduceCount = 0;
            Check("flow: warehouse cleared", before > 0 && roomState.ProduceCount == 0);

            // 升级解锁:2 级开养猪场
            state.OverallLevel = 2;
            Check("flow: pig room unlocks at lvl2", UpgradeSystem.IsUnlocked(RoomType.Pig, state.OverallLevel));
        }
        catch (Exception ex)
        {
            results.Add("FAIL exception: " + ex);
        }

        File.WriteAllLines(Path.Combine(ModEntry.Instance.Helper.DirectoryPath, "autotest_result.txt"), results);
        ModEntry.Instance.Monitor.Log("AnimalBarn integration test done: " + results.Count(r => r.StartsWith("PASS")) + " passed", LogLevel.Info);
    }

    /// <summary>反射读取 HubMenu 的菜单框源矩形(验证是标准 60x60,drawTextureBox 能正确九宫格切)。</summary>
    private static Microsoft.Xna.Framework.Rectangle GetMenuBoxSrc()
    {
        var f = typeof(HubMenu).GetField("MenuBoxSrc",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        return f != null ? (Microsoft.Xna.Framework.Rectangle)f.GetValue(null)! : Microsoft.Xna.Framework.Rectangle.Empty;
    }
}
