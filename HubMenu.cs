using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace AnimalBarn;

/// <summary>中枢操作台菜单:4 页签(状态/升级/商店/仓库)。
/// 本类只渲染快照、处理页签切换;购买/升级/取货等操作按钮在 Task 4.2 加入。</summary>
public class HubMenu : IClickableMenu
{
    /// <summary>页签枚举。</summary>
    public enum Tab { Status, Upgrade, Shop, Warehouse }

    private const int MenuWidth = 800;
    private const int MenuHeight = 500;
    internal const int TabWidth = 120;
    internal const int TabHeight = 40;
    internal const int TabGap = 8;

    private readonly HubSnapshot _snapshot;
    private Tab _tab = Tab.Status;
    private readonly List<Rectangle> _tabRects = new();

    /// <summary>当前页签(供测试与外部读取)。</summary>
    public Tab CurrentTab => _tab;

    public HubMenu(HubSnapshot snapshot)
        : base((Game1.uiViewport.Width - MenuWidth) / 2, (Game1.uiViewport.Height - MenuHeight) / 2,
               MenuWidth, MenuHeight, showUpperRightCloseButton: true)
    {
        _snapshot = snapshot;

        // 4 个页签矩形(顶部横排),点击检测用 Contains
        int x = xPositionOnScreen + 24;
        for (int i = 0; i < 4; i++)
        {
            _tabRects.Add(new Rectangle(x, yPositionOnScreen + 48, TabWidth, TabHeight));
            x += TabWidth + TabGap;
        }
    }

    /// <summary>点击处理:页签切换 / 关闭按钮。</summary>
    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        base.receiveLeftClick(x, y, playSound); // 处理右上角关闭按钮

        for (int i = 0; i < _tabRects.Count; i++)
        {
            if (_tabRects[i].Contains(x, y))
            {
                _tab = (Tab)i;
                if (playSound) Game1.playSound("smallSelect");
                return;
            }
        }
    }

    /// <summary>主绘制:背景 + 标题 + 页签 + 页签内容。</summary>
    public override void draw(SpriteBatch b)
    {
        base.draw(b);
        drawBackground(b);

        // 标题
        b.DrawString(Game1.dialogueFont, "动物养殖场中枢",
            new Vector2(xPositionOnScreen + 24, yPositionOnScreen + 14), Game1.textColor);

        // 页签
        string[] names = { "状态", "升级", "商店", "仓库" };
        for (int i = 0; i < _tabRects.Count; i++)
        {
            Rectangle r = _tabRects[i];
            b.Draw(Game1.mouseCursors, r, new Rectangle(64, 256, 64, 64), Color.White * 0.9f);
            b.DrawString(Game1.smallFont, names[i],
                new Vector2(r.X + (r.Width - Game1.smallFont.MeasureString(names[i]).X) / 2f,
                            r.Y + (r.Height - Game1.smallFont.MeasureString(names[i]).Y) / 2f),
                i == (int)_tab ? Game1.textColor : new Color(120, 120, 120));
        }

        // 内容区
        switch (_tab)
        {
            case Tab.Status: DrawStatus(b); break;
            case Tab.Upgrade: DrawUpgrade(b); break;
            case Tab.Shop: DrawPlaceholder(b, "商店内容将在下一阶段实现"); break;
            case Tab.Warehouse: DrawPlaceholder(b, "仓库内容将在下一阶段实现"); break;
        }

        drawMouse(b);
    }

    /// <summary>状态页:每房一行 — 房间名 + (解锁? 数量/上限 : 未解锁) + 产品数。</summary>
    private void DrawStatus(SpriteBatch b)
    {
        int y = yPositionOnScreen + 100;
        foreach (RoomSnapshot r in _snapshot.Rooms)
        {
            string line = r.Unlocked
                ? $"{r.DisplayName}: {r.Count}/{r.Capacity} 只 · 待收产品 {r.ProduceCount}"
                : $"{r.DisplayName}: 未解锁";
            b.DrawString(Game1.smallFont, line, new Vector2(xPositionOnScreen + 40, y), Game1.textColor);
            y += 30;
        }
        b.DrawString(Game1.smallFont, $"干草库存: {_snapshot.HayStock}",
            new Vector2(xPositionOnScreen + 40, yPositionOnScreen + 430), Game1.textColor);
    }

    /// <summary>升级页:整体等级 + 升级费用 + 解锁内容;各房升级等级 + 容量。</summary>
    private void DrawUpgrade(SpriteBatch b)
    {
        int y = yPositionOnScreen + 100;
        b.DrawString(Game1.smallFont,
            $"养殖场整体等级: {_snapshot.OverallLevel} 级", new Vector2(xPositionOnScreen + 40, y), Game1.textColor);
        y += 30;
        b.DrawString(Game1.smallFont,
            _snapshot.CanUpgradeOverall
                ? $"升级费用: {_snapshot.OverallUpgradeCost} g · 解锁: {_snapshot.OverallUpgradeUnlocks}"
                : "整体已达到最高等级",
            new Vector2(xPositionOnScreen + 40, y), Game1.textColor);
        y += 34;
        foreach (RoomSnapshot r in _snapshot.Rooms)
        {
            b.DrawString(Game1.smallFont,
                $"{r.DisplayName}: 等级 {r.UpgradeLevel} · 容量 {r.Capacity}",
                new Vector2(xPositionOnScreen + 40, y), Game1.textColor);
            y += 30;
        }
    }

    /// <summary>占位页:居中提示文字。</summary>
    private void DrawPlaceholder(SpriteBatch b, string text)
    {
        Vector2 size = Game1.smallFont.MeasureString(text);
        b.DrawString(Game1.smallFont, text,
            new Vector2(xPositionOnScreen + (MenuWidth - size.X) / 2f, yPositionOnScreen + (MenuHeight - size.Y) / 2f),
            Game1.textColor);
    }
}
