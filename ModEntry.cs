using StardewModdingAPI;

namespace AnimalBarn;

public class ModEntry : Mod
{
    internal static ModEntry Instance = null!;

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        this.Monitor.Log("AnimalBarn loaded.", LogLevel.Info);
    }
}
