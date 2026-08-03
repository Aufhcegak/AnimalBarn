using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace AnimalBarn;

/// <summary>游戏内自动验证 bot:进世界后自动跑业务流程验证,结果写 autotest_bot.txt。
/// 不睡天(不打扰玩家)——直接手动调结算逻辑验证(与真实 DayStarted 同路径),
/// 产物/好感/干草走真实 AnimalLedger.SettleDay + SettleAllRooms。</summary>
public static class AutoTester
{
    public static bool Pending;
    private static bool _started;
    private static bool _done;

    public static void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Pending || _done) return;
        if (!Context.IsWorldReady) return;
        if (Game1.currentLocation == null) return;   // 等真正进世界
        if (!_started)
        {
            _started = true;
            try { Run(); }
            catch (Exception ex) { Results.Add("FAIL exception: " + ex); }
            Finish();
        }
    }

    private static readonly List<string> Results = new();

    private static void Run()
    {
        Results.Add("=== AutoTester start ===");

        // 1. 找养殖场建筑
        var barns = SettlementService.Current?.FindAllBarns();
        if (barns == null || barns.Count == 0)
        {
            Results.Add("FAIL no barn found (需先在农场建养殖场)");
            return;
        }
        var building = barns[0];
        Results.Add("INFO barn found id=" + building.id.Value);

        var state = SettlementService.Current.GetOrCreate(building);
        Results.Add("INFO overallLevel=" + state.OverallLevel + " hay=" + state.HayStock);

        // 2. 买 10 只【饿的】成年鸡(Fullness=0,验证干草扣减路径)
        var roomState = state.GetRoom(RoomType.Chicken);
        int before = roomState.Animals.Count;
        var ledger = AnimalLedger.FromRoom(roomState);
        ledger.Capacity = UpgradeSystem.CapacityAt(RoomType.Chicken, roomState.UpgradeLevel);
        for (int i = 0; i < 10; i++)
        {
            ledger.TryAdd(new LedgerAnimal
            {
                Id = 80000 + i,
                Room = RoomType.Chicken,
                TypeKey = "White Chicken",
                AgeDays = 10,               // 成年
                Happiness = 255,
                Fullness = 0,               // 饿 → 结算应扣草喂食
                DaysSinceProduce = 1,       // 可产
                OwnerId = Game1.player.UniqueMultiplayerID,
            });
        }
        ledger.SaveTo(roomState);
        Results.Add("PASS added 10 hungry adult chickens (total " + roomState.Animals.Count + ")");

        // 3. 手动结算(与 DayStarted 同路径:建筑级结算所有房间)
        int hayBefore = state.HayStock;
        if (hayBefore < 10) state.HayStock = 100;   // 保证有草可扣
        SettlementService.SettleAllRooms(state, null);

        // 4. 检查结果
        var roomAfter = state.GetRoom(RoomType.Chicken);
        Results.Add("INFO after settle: chickens=" + roomAfter.Animals.Count
            + " produceCount=" + roomAfter.ProduceCount
            + " hay=" + state.HayStock + " (was " + hayBefore + ")");

        bool produced = roomAfter.ProduceCount >= 1;
        Results.Add(produced ? "PASS chickens produced (" + roomAfter.ProduceCount + " 个蛋/奶)" : "FAIL no produce");

        bool friendship = roomAfter.Animals.Count > 0 && roomAfter.Animals[0].Friendship > 0;
        Results.Add(friendship ? "PASS friendship grew (" + roomAfter.Animals[0].Friendship + ")" : "FAIL friendship not grew");

        bool hayConsumed = state.HayStock < hayBefore;
        Results.Add(hayConsumed ? "PASS hay consumed (" + (hayBefore - state.HayStock) + " 份)" : "FAIL hay not consumed(动物不饿?)");

        // 5. 仓库页聚合验证(取货路径)
        Results.Add("INFO produce stacks: " + string.Join(", ", roomAfter.ProduceStacks.Select(kv => kv.Key + "=" + kv.Value)));
    }

    private static void Finish()
    {
        _done = true;
        File.WriteAllLines(Path.Combine(ModEntry.Instance.Helper.DirectoryPath, "autotest_bot.txt"), Results);
        ModEntry.Instance.Monitor.Log("AnimalBarn AutoTester done: " + Results.Count(r => r.StartsWith("PASS")) + " passed", LogLevel.Info);
        try { File.Delete(Path.Combine(ModEntry.Instance.Helper.DirectoryPath, "autotest.txt")); } catch { }
    }
}
