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
            // 1. 建筑数据存在
            Check("buildingData has xiepe.AnimalBarn",
                Game1.buildingData.TryGetValue("xiepe.AnimalBarn", out var data) && data.Builder == "Robin");

            // 2. 建筑可实例化
            var b = Building.CreateInstanceFromId("xiepe.AnimalBarn", new Vector2(0, 0));
            Check("building instantiable", b != null && b.buildingType.Value == "xiepe.AnimalBarn");

            // 3. 大堂地图可加载(地图资产注入在 Task 1.4,此时应已注册)
            var lobby = new AnimalBarnRoom("Maps\\xiepe.AnimalBarn.Lobby", "xiepe.AnimalBarn.Lobby");
            Check("lobby map loads", lobby.map != null && lobby.map.Layers.Count >= 5);

            // 4. 大堂可进(Back 层有地板,门洞处无阻挡)
            Check("lobby floor ok", lobby.map.GetLayer("Back").Tiles[6, 4] != null);

            // 5. 大堂出口 warp 存在(指向 Farm)
            Check("lobby has farm warp", lobby.warps.Any(w => w.TargetName == "Farm"));

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

            // 7. 8 个房间地图全部可加载且要素齐全(干草槽/自动喂食/生产区/出口)
            foreach (var def in RoomDefinitions.All)
            {
                var room = new AnimalBarnRoom("Maps\\" + def.MapName, def.MapName);
                var map = room.map;
                Check($"room {def.Room} loads", map != null && map.Layers.Count >= 5);
                Check($"room {def.Room} AutoFeed", map != null && map.Properties.ContainsKey("AutoFeed"));
                Check($"room {def.Room} ProduceArea", map != null && map.Properties.ContainsKey("ProduceArea"));
                Check($"room {def.Room} Trough", map?.GetLayer("Back").Tiles[5, 2]?.Properties.ContainsKey("Trough") == true);
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
}
