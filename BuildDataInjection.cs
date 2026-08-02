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
        // 自定义建筑外观贴图(mod assets 里的 PNG,绿顶+暖棕涂装)
        if (e.Name.IsEquivalentTo("xiepe.AnimalBarn/Building"))
        {
            e.LoadFromModFile<Microsoft.Xna.Framework.Graphics.Texture2D>("assets/Buildings_xiepe.AnimalBarn.png", AssetLoadPriority.Medium);
            return;
        }

        // 门口动物挂牌 tilesheet(8 个房间各一块:鸡/鸭/兔/恐龙/鸵鸟/猪/山羊/牛)
        if (e.Name.IsEquivalentTo("xiepe.AnimalBarn/Plaques"))
        {
            e.LoadFromModFile<Microsoft.Xna.Framework.Graphics.Texture2D>("assets/AnimalBarnPlaques.png", AssetLoadPriority.Medium);
            return;
        }

        // 中枢电脑台贴图(大堂中央的"数据中枢"造型,替代此前的干草堆)
        if (e.Name.IsEquivalentTo("xiepe.AnimalBarn/HubComputer"))
        {
            e.LoadFromModFile<Microsoft.Xna.Framework.Graphics.Texture2D>("assets/HubComputer.png", AssetLoadPriority.Medium);
            return;
        }

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
                        HumanDoor = new Point(1, 3),  // 与原版 Barn 一致(建筑左下门,Y=3 在 Size 高度 4 范围内;此前设 (3,4) 越界导致点门无反应)
                        Texture = "xiepe.AnimalBarn/Building",   // 自定义涂装外观(绿顶+暖棕),由下方 OnAssetRequested 提供 PNG
                        IndoorMap = "xiepe.AnimalBarn.Lobby",
                        IndoorMapType = "StardewValley.AnimalHouse, Stardew Valley",  // 原版类型:自定义类会存档序列化崩溃
                        MaxOccupants = 30,            // 实体动物上限(每房),台账容量独立管
                        ValidOccupantTypes = new List<string>(),
                    };
                }
            });
        }
    }
}
