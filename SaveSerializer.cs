using System.Text.Json;
using StardewValley.Buildings;

namespace AnimalBarn;

public static class SaveSerializer
{
    private const string Key = "xiepe.AnimalBarn.Data";

    // IncludeFields:本 mod 的数据类(BarnSaveData/LedgerAnimal)均用公共字段(与代码库其余部分一致),
    // System.Text.Json 默认只序列化属性,必须显式开启字段。
    private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };

    public static BarnSaveData? Load(Building building)
    {
        if (building.modData.TryGetValue(Key, out var json))
        {
            try { return JsonSerializer.Deserialize<BarnSaveData>(json, Options); }
            catch { return null; }
        }
        return null;
    }

    public static void Save(Building building, BarnSaveData data)
    {
        building.modData[Key] = JsonSerializer.Serialize(data, Options);
    }
}
