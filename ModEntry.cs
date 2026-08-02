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
        this.Monitor.Log("AnimalBarn loaded.", LogLevel.Info);
    }
}
