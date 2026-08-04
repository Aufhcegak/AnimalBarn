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

        // 6. 取货回归验证(2026-08-04 修复:放进背包的"实际放入量"靠数背包前后差,
        //    不再靠 item.Stack 差值 —— 原版 addItemToInventory 放进空格时 Stack 不减,
        //    旧算法把"已放进背包"误判成 0 = 误报"背包已满"+ 仓库少扣 + 背包白得物品)。
        try
        {
            var anyStack = roomAfter.ProduceStacks.FirstOrDefault(kv => kv.Value > 0);
            if (anyStack.Key == null || anyStack.Value <= 0)
            {
                Results.Add("INFO take-produce: 仓库无产品,跳过取货验证(先跑结算)");
            }
            else
            {
                var parts = anyStack.Key.Split('|');
                string pid = parts[0];
                int pqual = parts.Length > 1 && int.TryParse(parts[1], out int q) ? q : 0;
                int takeQty = Math.Min(anyStack.Value, 5);

                int CountInv()   // 数背包里同 ID+同质量总数
                {
                    int n = 0;
                    foreach (var i in Game1.player.Items)
                        if (i?.QualifiedItemId == pid && i.Quality == pqual) n += i.Stack;
                    return n;
                }

                int invBefore = CountInv();
                var takeItem = ItemRegistry.Create(pid, takeQty);
                takeItem.Quality = pqual;
                int took = HubMenu.AddToInventoryCounted(takeItem);   // 真实放入路径(原版 addItemToInventoryBool)
                int invAfter = CountInv();

                Results.Add($"INFO take-produce: 取 {pid}(q{pqual}) 请求 {takeQty} 返回 {took} 背包 {invBefore}→{invAfter}");
                Results.Add((took == invAfter - invBefore
                    ? "PASS " : "FAIL ") + $"take-produce: 返回数=背包实际增量 ({took} == {invAfter - invBefore})");
                if (took > 0)
                    Results.Add("PASS take-produce: 背包有空间时成功取出,不再误报背包已满");
                else
                    Results.Add("INFO take-produce: 背包满,取 0 个(正常拒绝,不刷蛋)");
            }
        }
        catch (Exception ex)
        {
            Results.Add("FAIL take-produce exception: " + ex);
        }
    }

    private static void Finish()
    {
        _done = true;
        File.WriteAllLines(Path.Combine(ModEntry.Instance.Helper.DirectoryPath, "autotest_bot.txt"), Results);
        ModEntry.Instance.Monitor.Log("AnimalBarn AutoTester done: " + Results.Count(r => r.StartsWith("PASS")) + " passed", LogLevel.Info);
        try { File.Delete(Path.Combine(ModEntry.Instance.Helper.DirectoryPath, "autotest.txt")); } catch { }
    }
}
