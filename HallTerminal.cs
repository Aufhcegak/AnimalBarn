using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace AnimalBarn;

/// <summary>门厅房间选择终端:放一台"选房间终端"在门厅中央(TerminalTile),玩家点击 → RoomSelectMenu。
/// 终端用原版 Furniture "TV 家具"或自绘 —— 用 ComputerConsole 同款基底(Farm Computer (BC)239),
/// 复用 HubConsole.ComputerConsole 的绘制(但那是 private nested,这里单独做个轻量版)。</summary>
public static class HallTerminal
{
    /// <summary>确保门厅中央有终端对象(幂等)。玩家在门厅时每 tick 由 ModEntry 调用。</summary>
    public static void EnsurePlaced(GameLocation hall)
    {
        if (hall == null || !AnimalBarnLocations.IsHall(hall)) return;
        var tile = new Vector2(HallMapBuilder.TerminalTile.X, HallMapBuilder.TerminalTile.Y);

        if (hall.objects.TryGetValue(tile, out var existing))
        {
            if (existing is HallSelectTerminal) return;
            if (existing is not null && existing.QualifiedItemId == "(BC)239" && existing.bigCraftable.Value)
            {
                hall.objects[tile] = new HallSelectTerminal(tile);   // 读档回退的原版电脑 → 换回
                return;
            }
            return;   // 玩家放的别的东西 → 不覆盖
        }

        try
        {
            hall.objects[tile] = new HallSelectTerminal(tile);
        }
        catch (System.Exception ex)
        {
            ModEntry.Instance.Monitor.LogOnce("HallTerminal: 放置终端失败: " + ex.Message, StardewModdingAPI.LogLevel.Warn);
        }
    }

    /// <summary>门厅终端对象:基底原版 Farm Computer (BC)239,自绘贴图沿用 HubComputer(电脑桌)风格,
    /// 阻挡通行、不可拾取。点击由 BarnPatches.BeforeCheckAction 分支处理(IsTerminalTile)。</summary>
    private sealed class HallSelectTerminal : StardewValley.Object
    {
        public HallSelectTerminal() : base() { }

        public HallSelectTerminal(Vector2 tile) : base(tile, "(BC)239", false)
        {
            this.Name = "房间选择终端";
            this.bigCraftable.Value = true;
        }

        public override string DisplayName => "房间选择终端";

        public override void draw(SpriteBatch spriteBatch, int x, int y, float alpha = 1f)
        {
            // 用与中枢电脑台相同的自绘贴图(视觉统一:终端=小电脑桌)。
            try
            {
                var tex = Game1.content.Load<Texture2D>(HubConsole.TextureAsset);
                var pos = Game1.GlobalToLocal(Game1.viewport, new Vector2(x * 64f, y * 64f));
                float layerDepth = (y + 1) * 64f / 10000f;
                spriteBatch.Draw(tex, pos, null, Color.White * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, layerDepth);
            }
            catch
            {
                base.draw(spriteBatch, x, y, alpha);
            }
        }

        public override bool isPassable() => false;

        public override bool performToolAction(StardewValley.Tool t) => false;
    }
}
