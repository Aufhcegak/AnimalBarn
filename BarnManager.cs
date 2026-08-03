using StardewValley;
using StardewValley.Buildings;

namespace AnimalBarn;

/// <summary>核心状态持有者:每个养殖场建筑一份状态(等级/干草/房间台账)。由 ModEntry 持有。</summary>
public class BarnManager
{
    public const string BuildingId = "xiepe.AnimalBarn";

    // 键用 Building.id(NetGuid):这是 1.6 建筑的身份字段(存档持久、实例唯一),本版本没有 long 型 uid。
    private readonly Dictionary<Guid, BarnSaveData> _states = new();  // building.id -> state

    /// <summary>获取(或惰性创建)建筑的状态。
    /// 联机:每次直接从 building.modData 读取(NetField 主机→访客实时同步),
    /// 不在本端长期缓存——访客打开菜单时看到的一定是主机最新状态。</summary>
    public BarnSaveData GetOrCreate(Building building)
    {
        var id = building.id.Value;
        if (_states.TryGetValue(id, out var cached))
            return cached;
        var state = SaveSerializer.Load(building) ?? new BarnSaveData();
        _states[id] = state;
        return state;
    }

    /// <summary>把所有状态写回各自建筑的 modData。</summary>
    public void SaveAll()
    {
        foreach (var (id, state) in _states)
        {
            var b = FindBuildingById(id);
            if (b != null) SaveSerializer.Save(b, state);
        }
    }

    /// <summary>清空缓存(读档后调用,让状态重新从 modData 加载)。</summary>
    public void ClearCache() => _states.Clear();

    /// <summary>访客端:应用主机广播的最新状态(覆盖本地缓存)。
    /// 联机同步:主机状态通过 Building.ModData(NetField)同步,但只在 Saving 落盘;
    /// 访客打开菜单时请求最新快照,这里写进缓存供读取。不写 modData(访客不落盘)。</summary>
    public void ImportState(Building building, BarnSaveData state)
    {
        if (building == null || state == null) return;
        _states[building.id.Value] = state;
    }

    /// <summary>按 Guid 查找养殖场建筑(公开:联机同步层用)。</summary>
    public Building? FindBuildingById(Guid id)
    {
        foreach (var loc in Game1.locations)
        {
            foreach (var b in loc.buildings)
                if (b.id.Value == id && b.buildingType.Value == BuildingId)
                    return b;
        }
        return null;
    }

    /// <summary>建筑被拆除时清理缓存。</summary>
    public void Forget(Building building) => _states.Remove(building.id.Value);

    /// <summary>给定室内地点,找到其所属建筑的状态(大堂/房间的 DayUpdate 用)。</summary>
    public BarnSaveData? GetStateForIndoors(GameLocation indoors)
    {
        var building = indoors.ParentBuilding;
        if (building == null || building.buildingType.Value != BuildingId) return null;
        return GetOrCreate(building);
    }

    /// <summary>读档后清缓存。</summary>
    public void OnSaveLoaded() => ClearCache();

    /// <summary>存档前落盘。</summary>
    public void OnSaving() => SaveAll();

    /// <summary>查找所有养殖场建筑。</summary>
    public List<Building> FindAllBarns()
    {
        var result = new List<Building>();
        foreach (var loc in Game1.locations)
            foreach (var b in loc.buildings)
                if (b.buildingType.Value == BuildingId && !b.isUnderConstruction())
                    result.Add(b);
        return result;
    }
}
