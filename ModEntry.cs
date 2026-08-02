using StardewModdingAPI;

namespace AnimalBarn;

public class ModEntry : Mod
{
    internal static ModEntry Instance = null!;

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        helper.Events.Content.AssetRequested += BuildDataInjection.OnAssetRequested;
        helper.Events.Content.AssetRequested += LobbyMapBuilder.OnAssetRequested;
        IntegrationTest.Pending = File.Exists(Path.Combine(helper.DirectoryPath, "autotest.txt"));
        helper.Events.GameLoop.UpdateTicked += IntegrationTest.OnUpdateTicked;
        this.Monitor.Log("AnimalBarn loaded.", LogLevel.Info);
    }
}
