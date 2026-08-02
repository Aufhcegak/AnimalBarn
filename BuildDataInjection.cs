using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley.GameData.Buildings;

namespace AnimalBarn;

/// <summary>注入 Data/Buildings 数据行,让「动物养殖场」出现在罗宾建造菜单。</summary>
public static class BuildDataInjection
{
    public const string BuildingId = "xiepe.AnimalBarn";

    public static void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.Name.IsEquivalentTo("Data/Buildings"))
        {
            e.Edit(asset =>
            {
                var data = asset.AsDictionary<string, BuildingData>().Data;
                if (!data.ContainsKey(BuildingId))
                {
                    data[BuildingId] = new BuildingData
                    {
                        Name = "动物养殖场",
                        Description = "大型动物养殖设施。内含 8 个可独立升级的动物房间与干草房,全自动管理。",
                        Builder = "Robin",
                        BuildCost = 50000,
                        BuildMaterials = new List<BuildingMaterial>
                        {
                            new() { ItemId = "(O)388", Amount = 500 },   // 木头
                            new() { ItemId = "(O)390", Amount = 100 },   // 石头
                        },
                        BuildDays = 2,
                        Size = new Point(7, 4),       // 与原版 Barn 相同
                        HumanDoor = new Point(3, 4),
                        IndoorMap = "xiepe.AnimalBarn.Lobby",
                        IndoorMapType = "AnimalBarn.AnimalBarnRoom, AnimalBarn",
                        MaxOccupants = 30,            // 实体动物上限(每房),台账容量独立管
                        ValidOccupantTypes = new List<string>(),
                    };
                }
            });
        }
    }
}
