using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace AnimalBarn;

public class ModEntry : Mod
{
    internal static ModEntry Instance = null!;
    internal BarnManager Barn = null!;

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        this.Barn = new BarnManager();

        // 资产注入:建筑数据 + 大堂/门厅/房间地图
        helper.Events.Content.AssetRequested += BuildDataInjection.OnAssetRequested;
        helper.Events.Content.AssetRequested += LobbyMapBuilder.OnAssetRequested;
        helper.Events.Content.AssetRequested += HallMapBuilder.OnAssetRequested;
        helper.Events.Content.AssetRequested += RoomMapBuilder.OnAssetRequested;

        // Harmony 补丁:产物拦截 + 拆除保护 + 每日结算 + 中枢操作台
        var harmony = new Harmony(this.ModManifest.UniqueID);
        AutoGrabberInterceptor.Register(harmony);
        DemolitionGuard.Register(harmony);
        BarnPatches.Register(harmony);

        // 静态注入点:各模块共享同一个 BarnManager
        SettlementService.Current = this.Barn;
        AutoGrabberInterceptor.Barn = this.Barn;
        DemolitionGuard.Barn = this.Barn;

        // 游戏循环:大堂门检测 + 存档生命周期 + 建筑级每日结算
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.GameLoop.DayStarted += BarnPatches.OnDayStarted;   // 所有房间台账每日结算(修复:没进门的房间不产蛋)
        helper.Events.GameLoop.SaveLoaded += (_, _) => this.Barn.OnSaveLoaded();
        helper.Events.GameLoop.Saving += (_, _) => this.Barn.OnSaving();

        // 集成测试钩子:autotest.txt 触发【游戏内自动验证 bot】(更真实:真实 DayUpdate/保存)。
        // 无头 autotest(静态检查)仅在无头模式(IsHeadless)跑,避免与 bot 抢结果文件。
        bool trigger = File.Exists(Path.Combine(helper.DirectoryPath, "autotest.txt"));
        IntegrationTest.Pending = trigger && !Environment.UserInteractive;   // 无头(非交互)才跑静态 autotest
        AutoTester.Pending = trigger;
        helper.Events.GameLoop.UpdateTicked += IntegrationTest.OnUpdateTicked;
        helper.Events.GameLoop.UpdateTicked += AutoTester.OnUpdateTicked;

        this.Monitor.Log("AnimalBarn loaded.", LogLevel.Info);
    }

    /// <summary>每 tick:大堂中枢电脑台放置(幂等);锁清除。
    /// ⚠️ 不做动物归位:源码实锤(Character.cs isCollidingPosition + FarmAnimal.cs:861 setRandomPosition
    /// 避开 objects)原版 Fence 完全挡动物,动物不会跑到过道 —— 每 10 tick 强制改位置是多余的自造轮子,
    /// 且和原版移动逻辑冲突造成卡顿。已删除。</summary>
    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady) return;
        if (Game1.currentLocation is not GameLocation cur) return;
        if (AnimalBarnLocations.IsLobby(cur))
        {
            HubConsole.EnsurePlaced(cur);                    // 中枢电脑台(幂等,缺失才补放)
        }
        LobbyDoors.OnEndOfTick();                            // 清一次性锁(必须:否则第一次进门后锁永不清,所有门失效)
    }
}
