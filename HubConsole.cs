using Microsoft.Xna.Framework;
using StardewValley;

namespace AnimalBarn;

/// <summary>大堂中枢电脑台:放一个【纯原版 Farm Computer (BC)239】对象(bigCraftable=true)——
/// ① LookupAnything 认识原版物品 → 对着电脑按 F1 有描述(不尴尬);
/// ② 原版渲染 = 农场电脑贴图(不是坏贴图,之前坏是因为没设 bigCraftable);
/// ③ 存档安全(原版类型,不卡保存);
/// ④ 点击中枢台(IsHubTile 3x3)由 BarnPatches 弹中枢菜单。
/// 本类还管:动物房门(doors 字典,挡人+动画)。</summary>
public static class HubConsole
{
    /// <summary>中枢台贴图资产名(由 BuildDataInjection 提供 assets/HubComputer.png)。</summary>
    public const string TextureAsset = "xiepe.AnimalBarn/HubComputer";

    /// <summary>大堂初始化:电脑台对象(纯原版 Farm Computer)+ 动物房门。</summary>
    public static void EnsurePlaced(GameLocation lobby)
    {
        if (lobby == null || !AnimalBarnLocations.IsLobby(lobby)) return;
        EnsureComputer(lobby);
        EnsureDoor(lobby);
    }

    /// <summary>放中枢电脑台:自定义大件(ID = xiepe.AnimalBarn.Hub,由 Data/BigCraftables 注入)。
    /// ⚠️ 幂等:已有正确的自定义电脑就不动(避免每 tick 重建 = 卡顿!)。只有旧档坏对象/玩家误放才替换。
    /// 存档安全(原版 Object 类型),F1 显示"养殖场中枢电脑"(不跟农场电脑重名)。</summary>
    private static void EnsureComputer(GameLocation lobby)
    {
        var tile = new Vector2(LobbyMapBuilder.HubTile.X, LobbyMapBuilder.HubTile.Y);

        // 已有正确的自定义电脑 → 不动(关键:每 tick 调用,必须幂等)
        if (lobby.objects.TryGetValue(tile, out var existing) && existing is not null
            && existing.QualifiedItemId == "(BC)" + BuildDataInjection.HubItemId && existing.bigCraftable.Value)
            return;

        try
        {
            var console = new StardewValley.Object(tile, BuildDataInjection.HubItemId, false);   // 自定义 ID(无前缀)
            console.bigCraftable.Value = true;
            lobby.objects[tile] = console;   // 替换(旧档坏对象/玩家误放)
        }
        catch (System.Exception ex)
        {
            ModEntry.Instance.Monitor.LogOnce("HubConsole: 放置中枢电脑台失败: " + ex.Message, StardewModdingAPI.LogLevel.Warn);
        }
    }

    private static void EnsureDoor(GameLocation lobby)
    {
        var doorTile = new Point(LobbyMapBuilder.HallDoor.X, LobbyMapBuilder.HallDoor.Y);
        if (lobby.doors.ContainsKey(doorTile)) return;   // 已有门 → 不动

        try
        {
            // 门名任意,用于识别"这是养殖场动物房门"(checkAction 时按门名触发选房间菜单)。
            lobby.doors[doorTile] = "AnimalBarnDoor";
        }
        catch (System.Exception ex)
        {
            ModEntry.Instance.Monitor.LogOnce("HubConsole: 注册动物房门失败: " + ex.Message, StardewModdingAPI.LogLevel.Warn);
        }
    }
}
