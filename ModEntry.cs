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

        // 游戏循环:大堂门检测 + 存档生命周期
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.GameLoop.SaveLoaded += (_, _) => this.Barn.OnSaveLoaded();
        helper.Events.GameLoop.Saving += (_, _) => this.Barn.OnSaving();

        // 集成测试钩子
        IntegrationTest.Pending = File.Exists(Path.Combine(helper.DirectoryPath, "autotest.txt"));
        helper.Events.GameLoop.UpdateTicked += IntegrationTest.OnUpdateTicked;

        this.Monitor.Log("AnimalBarn loaded.", LogLevel.Info);
    }

    /// <summary>每 tick:检测玩家站在大堂门厅门 → warp 进门厅;门厅终端放置(幂等);锁清除。</summary>
    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady) return;
        if (Game1.currentLocation is not GameLocation cur) return;
        if (AnimalBarnLocations.IsLobby(cur))
        {
            HubConsole.EnsurePlaced(cur);                    // 中枢电脑台(幂等,缺失才补放)
            var who = Game1.player;
            if (who != null && who.IsLocalPlayer)
                LobbyDoors.TryEnterDoor(cur, who, this.Barn);   // 门厅门检测
        }
        else if (AnimalBarnLocations.IsHall(cur))
        {
            HallTerminal.EnsurePlaced(cur);                  // 门厅终端(幂等)
        }
        LobbyDoors.OnEndOfTick();                            // 清一次性锁(必须:否则第一次进门后锁永不清,所有门失效)
    }
}
