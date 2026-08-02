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
        }
        catch (Exception ex)
        {
            results.Add("FAIL exception: " + ex);
        }

        File.WriteAllLines(Path.Combine(ModEntry.Instance.Helper.DirectoryPath, "autotest_result.txt"), results);
        ModEntry.Instance.Monitor.Log("AnimalBarn integration test done: " + results.Count(r => r.StartsWith("PASS")) + " passed", LogLevel.Info);
    }
}
