using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace AnimalBarn;

/// <summary>大堂中枢电脑台:在大堂中央(HubTile)放一个电脑桌造型的运行时对象,玩家点击它打开中枢菜单。
/// 此前中枢是一撮干草堆(tile 18),不像"数据中枢";现在换成自绘电脑桌(assets/HubComputer.png)。
/// 幂等放置(EnsurePlaced):大堂每次进入检查,缺失才补,避免重复。点击仍走 IsHubTile(BarnPatches),不变。</summary>
public static class HubConsole
{
    /// <summary>中枢台贴图资产名(由 BuildDataInjection 提供 assets/HubComputer.png)。</summary>
    public const string TextureAsset = "xiepe.AnimalBarn/HubComputer";

    /// <summary>确保大堂中枢位有电脑台对象(幂等)。玩家在大堂时每 tick 由 ModEntry 调用。
    /// 若该格是普通 Object(读档后自定义子类可能被回退成原版 Farm Computer)→ 换成我们的电脑台。
    /// 若有其他玩家放的物件 → 不覆盖(尊重玩家)。</summary>
    public static void EnsurePlaced(GameLocation lobby)
    {
        if (lobby == null || !AnimalBarnLocations.IsLobby(lobby)) return;
        var tile = new Vector2(LobbyMapBuilder.HubTile.X, LobbyMapBuilder.HubTile.Y);

        if (lobby.objects.TryGetValue(tile, out var existing))
        {
            if (existing is ComputerConsole) return;        // 已是我们的电脑台 → 不动
            if (existing is not null && existing.QualifiedItemId == "(BC)239" && existing.bigCraftable.Value)
            {
                lobby.objects[tile] = new ComputerConsole(tile);   // 读档回退的原版电脑 → 换回自定义
                return;
            }
            return;   // 玩家放的别的东西 → 不覆盖
        }

        try
        {
            lobby.objects[tile] = new ComputerConsole(tile);
        }
        catch (System.Exception ex)
        {
            ModEntry.Instance.Monitor.LogOnce("HubConsole: 放置中枢电脑台失败: " + ex.Message, StardewModdingAPI.LogLevel.Warn);
        }
    }

    /// <summary>中枢电脑台对象:自绘贴图(电脑桌 sprite),阻挡通行、不可拾取(防止玩家把它铲走)。
    /// 基底用原版 Farm Computer (BC)239 —— 真电脑大件,即使自定义贴图加载失败也退回渲染成原版电脑,
    /// 绝不会乱码/禁止符号。不进存档(大堂随建筑序列化,objects 里的自定义子类保存时被父类兜底为基础
    /// Object,读档后大堂重建、本类由 EnsurePlaced 重新放置,无残留)。</summary>
    private sealed class ComputerConsole : StardewValley.Object
    {
        /// <summary>无参构造:NetRef 反序列化需要(读档时先 new 再填字段),缺了会读档崩溃。</summary>
        public ComputerConsole() : base() { }

        public ComputerConsole(Vector2 tile) : base(tile, "(BC)239", false)   // 原版 Farm Computer(电脑)做基底
        {
            this.Name = "养殖中枢电脑";
            this.bigCraftable.Value = true;   // 占地一格、不可捡拾(像大件设备),避免被误收
        }

        /// <summary>悬停/菜单里显示的中文名。</summary>
        public override string DisplayName => "养殖中枢电脑";

        /// <summary>在世界中绘制(用自定义电脑桌贴图,而不是 springobjects 里的物品图标)。
        /// 失败(贴图未加载等)时静默退回 base 绘制,绝不抛异常打断渲染。</summary>
        public override void draw(SpriteBatch spriteBatch, int x, int y, float alpha = 1f)
        {
            try
            {
                var tex = Game1.content.Load<Texture2D>(TextureAsset);
                var pos = Game1.GlobalToLocal(Game1.viewport, new Vector2(x * 64f, y * 64f));
                float layerDepth = (y + 1) * 64f / 10000f;   // 按 tile 排序,玩家在后会被挡住
                spriteBatch.Draw(tex, pos, null, Color.White * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, layerDepth);
            }
            catch
            {
                base.draw(spriteBatch, x, y, alpha);
            }
        }

        /// <summary>不可通行(像设备一样挡路,玩家绕到正面点击)。</summary>
        public override bool isPassable() => false;

        /// <summary>不可被拾起/打掉(中枢是固定设施)。</summary>
        public override bool performToolAction(StardewValley.Tool t) => false;
    }
}
