using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Menus;
using SObject = StardewValley.Object;

namespace AnimalBarn;

/// <summary>中枢操作台菜单:4 页签(状态/升级/商店/仓库)。
/// 只传快照的构造仅做展示(测试用);带 BarnManager/建筑的构造支持购买/取货等真实操作。</summary>
public class HubMenu : IClickableMenu
{
    /// <summary>页签枚举。</summary>
    public enum Tab { Status, Upgrade, Shop, Warehouse }

    private const int MenuWidth = 1000;
    private const int MenuHeight = 620;
    internal const int TabWidth = 150;
    internal const int TabHeight = 52;
    internal const int TabGap = 10;

    // 内容布局(相对菜单顶):行区固定高度,底部固定操作区,操作消息在最下
    private const int ContentLeft = 48;      // 内容左缘
    private const int RowTop = 158;          // 行区顶部(动物/产品行)
    private const int RowHeight = 38;        // 行高
    private const int RowAreaHeight = 342;   // 可视行区高度(9 行)
    private const int ButtonColX = 788;      // 右侧按钮列 X(相对屏幕)
    private const int ButtonWidth = 170;     // 按钮宽
    private const int ButtonHeight = 30;     // 按钮高
    private const int FooterY = 520;         // 底部固定区(干草购买/全部取走)
    private const int NoticeY = 580;         // 操作结果消息行

    // 供集成测试对齐校验用(与上面一致,Internal 暴露)
    internal const int ButtonColXConst = ButtonColX;
    internal const int ButtonWidthConst = ButtonWidth;
    internal const int ButtonHeightConst = ButtonHeight;
    internal const int RowTopConst = RowTop;
    internal const int RowHeightConst = RowHeight;

    private readonly HubSnapshot _snapshot;
    private Tab _tab = Tab.Status;
    private readonly List<Rectangle> _tabRects = new();

    // 真实操作依赖(由中枢操作台打开时注入;纯展示构造为 null)
    private readonly BarnManager? _barn;
    private readonly Building? _building;

    /// <summary>商店/仓库页的可点击按钮(页签切换/滚动时重建)。</summary>
    private readonly List<ClickableComponent> _buttons = new();

    /// <summary>仓库行区滚动偏移。</summary>
    private int _scroll;

    /// <summary>仓库页页码(每页 9 行,右侧翻页)。</summary>
    private int _page;

    /// <summary>仓库每页行数(每页 8 行 = 2 个物品×4 星级,塞满不挤)。</summary>
    private const int PageRows = 8;

    /// <summary>最后一次操作的结果消息(内容区底部显示)。</summary>
    private string _notice = "";

    /// <summary>当前页签(供测试与外部读取)。</summary>
    public Tab CurrentTab => _tab;

    /// <summary>动物类型 -> 中文名(商店显示用;原生 FarmAnimalData 无中文名)。</summary>
    private static readonly Dictionary<string, string> AnimalDisplayNames = new()
    {
        ["White Chicken"] = "白鸡",
        ["Duck"] = "鸭",
        ["Rabbit"] = "兔",
        ["Dinosaur"] = "恐龙",
        ["Ostrich"] = "鸵鸟",
        ["Pig"] = "猪",
        ["Goat"] = "山羊",
        ["Dairy Cow"] = "奶牛",
        ["Sheep"] = "羊",
    };

    /// <summary>干草购买档位。</summary>
    private static readonly int[] HayQuantities = { 10, 100, 500, 1000 };

    /// <summary>纯展示构造(测试/无操作场景)。</summary>
    public HubMenu(HubSnapshot snapshot)
        : base((Game1.uiViewport.Width - MenuWidth) / 2, (Game1.uiViewport.Height - MenuHeight) / 2,
               MenuWidth, MenuHeight, showUpperRightCloseButton: true)
    {
        _snapshot = snapshot;

        // 4 个页签矩形(顶部横排),点击检测用 Contains
        int x = xPositionOnScreen + 32;
        for (int i = 0; i < 4; i++)
        {
            _tabRects.Add(new Rectangle(x, yPositionOnScreen + 64, TabWidth, TabHeight));
            x += TabWidth + TabGap;
        }
    }

    /// <summary>带操作能力的构造(中枢操作台打开时用)。</summary>
    public HubMenu(HubSnapshot snapshot, BarnManager barn, Building building)
        : this(snapshot)
    {
        _barn = barn;
        _building = building;
    }

    /// <summary>当前状态(外部读取用)。</summary>
    public BarnSaveData? State => _barn != null && _building != null ? _barn.GetOrCreate(_building) : null;

    /// <summary>点击处理:页签切换 / 内容按钮 / 关闭按钮。</summary>
    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        base.receiveLeftClick(x, y, playSound); // 处理右上角关闭按钮

        for (int i = 0; i < _tabRects.Count; i++)
        {
            if (_tabRects[i].Contains(x, y))
            {
                _tab = (Tab)i;
                _notice = "";
                _scroll = 0;
                RebuildButtons();
                if (playSound) Game1.playSound("smallSelect");
                return;
            }
        }

        foreach (ClickableComponent b in _buttons)
        {
            if (b.bounds.Contains(x, y) && b.name != null)
            {
                if (playSound) Game1.playSound("smallSelect");
                HandleButton(b);
                return;
            }
        }
    }

    /// <summary>是否滑动条轨道区域(选中行时)。</summary>
    /// <summary>滚轮滚动仓库行区(商店行数固定 9 行,无需滚动)。</summary>
    public override void receiveScrollWheelAction(int direction)
    {
        base.receiveScrollWheelAction(direction);
        if (_tab == Tab.Warehouse && HasLiveState())
        {
            int maxOff = Math.Max(0, GetAggregatedStacks().Count * RowHeight - RowAreaHeight);
            _scroll = Math.Clamp(_scroll - direction / 120 * RowHeight, 0, maxOff);
            RebuildButtons();
        }
    }

    /// <summary>主绘制:背景 + 标题 + 页签 + 页签内容。</summary>
    public override void draw(SpriteBatch b)
    {
        // 标准游戏菜单框(menuTexture/MenuTiles)—— 不再用 mouseCursors(那会在 src(64,256) 画出红色禁止符号)。
        drawTextureBox(b, Game1.menuTexture, MenuBoxSrc,
            xPositionOnScreen, yPositionOnScreen, MenuWidth, MenuHeight, Color.White, 1f, drawShadow: true);

        // 标题
        b.DrawString(Game1.dialogueFont, "动物养殖场中枢",
            new Vector2(xPositionOnScreen + 32, yPositionOnScreen + 20), Game1.textColor);

        // 页签(当前页签:实色框 + 金色高亮条;未激活:半透明显灰)
        string[] names = { "状态", "升级", "商店", "仓库" };
        for (int i = 0; i < _tabRects.Count; i++)
        {
            Rectangle r = _tabRects[i];
            bool active = i == (int)_tab;
            Color tint = active ? Color.White : Color.White * 0.6f;
            drawTextureBox(b, Game1.menuTexture, MenuBoxSrc, r.X, r.Y, r.Width, r.Height, tint, 1f, drawShadow: false);
            if (active)
                b.Draw(Game1.staminaRect, new Rectangle(r.X + 6, r.Y, r.Width - 12, 4), Color.Gold);
            var nameSize = Game1.smallFont.MeasureString(names[i]);
            b.DrawString(Game1.smallFont, names[i],
                new Vector2(r.X + (r.Width - nameSize.X) / 2f, r.Y + (r.Height - nameSize.Y) / 2f),
                active ? Game1.textColor : new Color(140, 110, 90));
        }

        // 内容区
        switch (_tab)
        {
            case Tab.Status: DrawStatus(b); break;
            case Tab.Upgrade: DrawUpgrade(b); break;
            case Tab.Shop: DrawShop(b); break;
            case Tab.Warehouse: DrawWarehouse(b); break;
        }

        drawMouse(b);
    }

    /// <summary>标准菜单框源矩形(与原版 IClickableMenu.drawTextureBox 一致:menuTexture 里 (0,256,60,60),
    /// 60x60 = 3x3 各 20px 的完整框,drawTextureBox 内部按 /3 九宫格切)。</summary>
    private static readonly Rectangle MenuBoxSrc = new Rectangle(0, 256, 60, 60);

    /// <summary>操作结果消息:错误为区内红色提示,成功为 HUD 气泡。</summary>
    private void Notice(string msg, bool error = false)
    {
        _notice = msg;
        if (error) Game1.playSound("cancel");
        else Game1.addHUDMessage(new HUDMessage(msg));  // 默认类型 = 常规消息气泡
    }

    /// <summary>在内容区底部绘制操作结果消息(带半透明底,保证可读)。</summary>
    private void DrawNotice(SpriteBatch b)
    {
        if (_notice == "") return;
        b.Draw(Game1.staminaRect,
            new Rectangle(xPositionOnScreen + ContentLeft, yPositionOnScreen + NoticeY - 4, MenuWidth - ContentLeft * 2, 30),
            Color.Black * 0.4f);
        b.DrawString(Game1.smallFont, _notice,
            new Vector2(xPositionOnScreen + ContentLeft + 10, yPositionOnScreen + NoticeY + 2), Color.Red);
    }

    /// <summary>状态页:每房一行 — 房间名 + (解锁? 数量/上限 : 未解锁) + 产品数。</summary>
    private void DrawStatus(SpriteBatch b)
    {
        int y = yPositionOnScreen + RowTop;
        foreach (RoomSnapshot r in _snapshot.Rooms)
        {
            string line = r.Unlocked
                ? $"{r.DisplayName}: {r.Count}/{r.Capacity} 只 · 待收产品 {r.ProduceCount}"
                : $"{r.DisplayName}: 未解锁";
            b.DrawString(Game1.smallFont, line, new Vector2(xPositionOnScreen + ContentLeft, y), Game1.textColor);
            y += RowHeight;
        }
        b.DrawString(Game1.smallFont, $"干草库存: {_snapshot.HayStock}",
            new Vector2(xPositionOnScreen + ContentLeft, yPositionOnScreen + FooterY), Game1.textColor);
    }

    /// <summary>升级页:整体升级(扣钱+木+石) + 各房间升级(容量),全部可点击操作。</summary>
    private void DrawUpgrade(SpriteBatch b)
    {
        int y = yPositionOnScreen + RowTop;
        bool live = HasLiveState();

        // 整体升级
        b.DrawString(Game1.smallFont,
            $"养殖场整体等级: {_snapshot.OverallLevel} 级", new Vector2(xPositionOnScreen + ContentLeft, y), Game1.textColor);
        y += 34;
        if (_snapshot.CanUpgradeOverall)
        {
            var next = UpgradeSystem.Overall[Math.Clamp(_snapshot.OverallLevel, 0, 4)];   // Overall[level] = 升到 level+1 的档
            b.DrawString(Game1.smallFont,
                $"升级费用: {next.Cost}g + 木{next.Wood} + 石{next.Stone} · 解锁: {next.Unlocks}",
                new Vector2(xPositionOnScreen + ContentLeft, y), Game1.textColor);
            if (live)
                DrawButton(b, new Rectangle(xPositionOnScreen + ButtonColX, y - 5, ButtonWidth, ButtonHeight), "整体升级");
        }
        else
        {
            b.DrawString(Game1.smallFont, "整体已达到最高等级", new Vector2(xPositionOnScreen + ContentLeft, y), Game1.textColor);
        }
        y += 48;

        // 各房间升级
        foreach (RoomSnapshot r in _snapshot.Rooms)
        {
            var rows = UpgradeSystem.CapacityFor(r.Room);
            bool maxed = r.UpgradeLevel >= rows.Length - 1;
            bool unlocked = r.Unlocked;
            string text = $"{r.DisplayName}: 等级 {r.UpgradeLevel} · 容量 {r.Capacity}";
            if (!unlocked) text += "未解锁";
            else if (maxed) text += " (已满级)";
            else
            {
                var nxt = rows[r.UpgradeLevel + 1];
                text += $" → {nxt.Cost}g" + (nxt.Wood > 0 ? $" + 木{nxt.Wood}" : "") + (nxt.Stone > 0 ? $" + 石{nxt.Stone}" : "") + $" → 容量 {nxt.Capacity}";
            }
            b.DrawString(Game1.smallFont, text, new Vector2(xPositionOnScreen + ContentLeft, y),
                unlocked ? Game1.textColor : new Color(140, 110, 90));
            if (live && unlocked && !maxed)
                DrawButton(b, new Rectangle(xPositionOnScreen + ButtonColX, y - 5, ButtonWidth, ButtonHeight), "升级房间");
            y += RowHeight;
        }

        DrawNotice(b);
    }

    // ============================ 按钮与布局 ============================

    /// <summary>重建当前页签的按钮(页签切换/滚动时)。不重置 _scroll。</summary>
    private void RebuildButtons()
    {
        _buttons.Clear();

        if (_tab == Tab.Upgrade && HasLiveState())
        {
            // 整体升级按钮(与 DrawUpgrade 对齐:RowTop+34 行,按钮 y-5)
            if (_snapshot.CanUpgradeOverall)
                _buttons.Add(new ClickableComponent(
                    new Rectangle(xPositionOnScreen + ButtonColX, yPositionOnScreen + RowTop + 34 - 5, ButtonWidth, ButtonHeight),
                    "upgradeOverall"));
            // 各房间升级按钮(整体块 RowTop+34+48=RowTop+82 起,每行 RowHeight)
            int y = yPositionOnScreen + RowTop + 82;
            foreach (RoomSnapshot r in _snapshot.Rooms)
            {
                var rows = UpgradeSystem.CapacityFor(r.Room);
                bool maxed = r.UpgradeLevel >= rows.Length - 1;
                if (r.Unlocked && !maxed)
                    _buttons.Add(new ClickableComponent(
                        new Rectangle(xPositionOnScreen + ButtonColX, y - 5, ButtonWidth, ButtonHeight),
                        $"upgradeRoom:{r.Room}"));
                y += RowHeight;
            }
        }
        else if (_tab == Tab.Shop)
        {
            for (int i = 0; i < FarmAnimalCatalog.All.Length; i++)
            {
                _buttons.Add(new ClickableComponent(
                    new Rectangle(xPositionOnScreen + ButtonColX, yPositionOnScreen + RowTop + i * RowHeight - 5, ButtonWidth, ButtonHeight),
                    $"buyAnimal:{FarmAnimalCatalog.All[i].Room}"));
            }
            // 干草购买:独占一排,间距拉开,不再挤
            for (int i = 0; i < HayQuantities.Length; i++)
            {
                _buttons.Add(new ClickableComponent(
                    new Rectangle(xPositionOnScreen + ContentLeft + 40 + i * 210, yPositionOnScreen + FooterY + 30, 190, ButtonHeight),
                    $"buyHay:{HayQuantities[i]}"));
            }
        }
        else if (_tab == Tab.Warehouse && HasLiveState())
        {
            // 取走按钮:按"物品分组 4 星级"分页布局(与 DrawWarehouse 一致)
            var stacks = GetAggregatedStacks();
            var groups = stacks.GroupBy(s => s.Id)
                .Select(g => new { Id = g.Key, Qualities = g.ToDictionary(x => x.Quality, x => x.Count) })
                .ToList();
            int totalRows = groups.Sum(g => 4);
            int totalPages = Math.Max(1, (totalRows + PageRows - 1) / PageRows);
            if (_page >= totalPages) _page = totalPages - 1;

            int row = 0, gi = 0;
            int startRow = _page * PageRows;
            bool started = false;
            for (int g = 0; g < groups.Count && !started; g++)
            {
                if (row + 4 > startRow) { gi = g; started = true; }
                else row += 4;
            }
            int rowInPage = startRow - row;

            int drawn = 0;
            for (int g = gi; g < groups.Count && drawn < PageRows; g++)
            {
                for (int q = rowInPage; q < 4 && drawn < PageRows; q++, rowInPage = 0)
                {
                    int quality = q switch { 0 => 0, 1 => 1, 2 => 2, 3 => 4 };
                    int count = groups[g].Qualities.TryGetValue(quality, out int n) ? n : 0;
                    if (count > 0)
                    {
                        int rowY = yPositionOnScreen + RowTop + drawn * RowHeight;
                        _buttons.Add(new ClickableComponent(
                            new Rectangle(xPositionOnScreen + ContentLeft + 640, rowY + (RowHeight - ButtonHeight) / 2 - 3, ButtonWidth, ButtonHeight),
                            $"takeOne:{groups[g].Id}|{quality}"));
                    }
                    drawn++;
                }
            }

            // 翻页按钮(与 DrawWarehouse 位置一致:FooterY 下方 + 右侧)
            int pageY = yPositionOnScreen + FooterY + 34;
            int pageX = xPositionOnScreen + ContentLeft + 640;
            if (_page > 0)
                _buttons.Add(new ClickableComponent(new Rectangle(pageX, pageY, 40, ButtonHeight), "pagePrev"));
            if (_page < totalPages - 1)
                _buttons.Add(new ClickableComponent(new Rectangle(pageX + 100, pageY, 40, ButtonHeight), "pageNext"));

            _buttons.Add(new ClickableComponent(
                new Rectangle(xPositionOnScreen + ContentLeft, yPositionOnScreen + FooterY + 30, 200, ButtonHeight),
                "takeAllProduce"));
            // NOTE:干草不能取出(用户设计:干草只能买进,不能取出/放入)。只显示库存数。
        }
    }

    /// <summary>绘制一个操作按钮(标准菜单框 + 文字;悬停时提亮)。</summary>
    private void DrawButton(SpriteBatch b, Rectangle rect, string text)
    {
        bool hovered = rect.Contains(Game1.getMouseX(), Game1.getMouseY());
        Color tint = hovered ? Color.White : Color.White * 0.88f;
        drawTextureBox(b, Game1.menuTexture, MenuBoxSrc, rect.X, rect.Y, rect.Width, rect.Height, tint, 1f, drawShadow: false);
        Vector2 size = Game1.smallFont.MeasureString(text);
        b.DrawString(Game1.smallFont, text,
            new Vector2(rect.X + (rect.Width - size.X) / 2f, rect.Y + (rect.Height - size.Y) / 2f),
            hovered ? Game1.textColor : Game1.textColor);
    }

    // ============================ 商店页 ============================

    /// <summary>商店页:9 种动物(名称·9折价/原价+购买按钮)+ 干草购买档位。</summary>
    private void DrawShop(SpriteBatch b)
    {
        // 动物行(9 行固定,正好填满行区)
        for (int i = 0; i < FarmAnimalCatalog.All.Length; i++)
        {
            var info = FarmAnimalCatalog.All[i];
            int rowY = yPositionOnScreen + RowTop + i * RowHeight;
            // 快照按【实际房间】查:羊(Sheep)与山羊共用羊场(Goat),必须 RoomFor 映射,
            // 否则羊那行查不到快照 → 没有 (N/N) 括号。
            var snap = _snapshot.Rooms.FirstOrDefault(r => r.Room == RoomDefinitions.RoomFor(info.Room));
            string name = AnimalDisplayNames.TryGetValue(info.TypeKey, out var cn) ? cn : info.TypeKey;
            string note = snap == null
                ? ""
                : snap.Unlocked
                    ? $" {snap.Count}/{snap.Capacity}"
                    : " 未解锁";
            b.DrawString(Game1.smallFont, $"{name}{note} · {info.BuyPrice}g",
                new Vector2(xPositionOnScreen + ContentLeft, rowY + 4), Game1.textColor);
            DrawButton(b, new Rectangle(xPositionOnScreen + ButtonColX, rowY - 5, ButtonWidth, ButtonHeight), "购买");
        }

        // 底部固定区:干草购买(独占一排,不挤)
        b.DrawString(Game1.smallFont,
            $"干草(每份 {HaySystem.DiscountPrice}g, 库存 {_snapshot.HayStock})",
            new Vector2(xPositionOnScreen + ContentLeft, yPositionOnScreen + FooterY), Game1.textColor);
        for (int i = 0; i < HayQuantities.Length; i++)
        {
            DrawButton(b,
                new Rectangle(xPositionOnScreen + ContentLeft + 40 + i * 210, yPositionOnScreen + FooterY + 30, 190, ButtonHeight),
                $"{HayQuantities[i]} 份 ({HayQuantities[i] * HaySystem.DiscountPrice}g)");
        }

        DrawNotice(b);
    }

    // ============================ 仓库页 ============================

    /// <summary>仓库页:各产品堆叠(图标+名称+星级+数量) + 取走(shift=5/ctrl+shift=25 批量) + 全部取走。</summary>
    private void DrawWarehouse(SpriteBatch b)
    {
        int headerY = yPositionOnScreen + RowTop;
        b.DrawString(Game1.smallFont, "产品仓库(点击行取货 · shift 取5 · Ctrl+shift 取25)",
            new Vector2(xPositionOnScreen + ContentLeft, headerY - 30), Game1.textColor);

        var stacks = GetAggregatedStacks();

        if (stacks.Count == 0)
        {
            b.DrawString(Game1.smallFont, "(仓库为空)",
                new Vector2(xPositionOnScreen + ContentLeft, yPositionOnScreen + RowTop + 4), new Color(120, 120, 120));
        }
        else
        {
            // 分页:按"物品"分组(同物品所有星级连续排,不分开页),每页 PageRows 行。
            // 每组 = 该物品 4 个星级(0/1/2/4)全显示(数量 0 也显示 ×0,最整齐)。
            var groups = stacks.GroupBy(s => s.Id)
                .Select(g => new { Id = g.Key, Qualities = g.ToDictionary(x => x.Quality, x => x.Count) })
                .ToList();
            int totalRows = groups.Sum(g => 4);   // 每组 4 行(普通/银/金/铱)
            int totalPages = Math.Max(1, (totalRows + PageRows - 1) / PageRows);
            if (_page >= totalPages) _page = totalPages - 1;

            // 按页找起始组+组内偏移
            int row = 0, gi = 0;
            int startRow = _page * PageRows;
            bool started = false;
            for (int g = 0; g < groups.Count && !started; g++)
            {
                if (row + 4 > startRow) { gi = g; started = true; }
                else row += 4;
            }
            int rowInPage = startRow - row;   // 组内起始偏移(0-3)

            int drawn = 0;
            for (int g = gi; g < groups.Count && drawn < PageRows; g++)
            {
                for (int q = rowInPage; q < 4 && drawn < PageRows; q++, rowInPage = 0)
                {
                    int quality = q switch { 0 => 0, 1 => 1, 2 => 2, 3 => 4 };
                    int count = groups[g].Qualities.TryGetValue(quality, out int n) ? n : 0;
                    int rowY = yPositionOnScreen + RowTop + drawn * RowHeight;
                    var item = ItemRegistry.Create(groups[g].Id);
                    // 贴图 0.6f ≈ 38px 高(等于行高,行内放得下不溢出不重叠),垂直居中
                    item.drawInMenu(b, new Vector2(xPositionOnScreen + ContentLeft, rowY), 0.6f, 1f, 0.9f);
                    // 星级:贴图本身带星级样式,文字黑字+括号标星级
                    string starName = quality switch { 1 => "银星", 2 => "金星", 4 => "铱星", _ => "" };
                    string namePart = starName == "" ? item.DisplayName : $"{item.DisplayName} ({starName})";
                    // 文字紧贴贴图右缘(贴图 38px + 间距 24px → x+62),垂直居中;数量固定列;按钮固定列
                    // 统一间距:贴图-文字 24px,文字-数量列固定,数量-按钮固定 → 所有行对齐
                    int textX = xPositionOnScreen + ContentLeft + 62;
                    int textY = rowY + (RowHeight - 16) / 2;   // 文字垂直居中(16px 字高)
                    int countX = xPositionOnScreen + ContentLeft + 540;
                    b.DrawString(Game1.smallFont, namePart, new Vector2(textX, textY), Game1.textColor);
                    b.DrawString(Game1.smallFont, $"× {count}",
                        new Vector2(countX - Game1.smallFont.MeasureString($"× {count}").X, textY), Game1.textColor);
                    if (count > 0)
                        DrawButton(b, new Rectangle(xPositionOnScreen + ContentLeft + 640, rowY + (RowHeight - ButtonHeight) / 2 - 3, ButtonWidth, ButtonHeight), "取走");
                    drawn++;
                }
            }

            // 右侧翻页控件:▲ 上一页 / 页码 / ▼ 下一页。
            // 位置:往下(FooterY 区,远离条目) + 往右(不与条目按钮列粘)
            int pageY = yPositionOnScreen + FooterY + 34;
            int pageX = xPositionOnScreen + ContentLeft + 640;
            DrawButton(b, new Rectangle(pageX, pageY, 40, ButtonHeight), _page > 0 ? "▲" : "");
            b.DrawString(Game1.smallFont, $"{_page + 1}/{totalPages}",
                new Vector2(pageX + 50, pageY + 5), Game1.textColor);
            DrawButton(b, new Rectangle(pageX + 100, pageY, 40, ButtonHeight), _page < totalPages - 1 ? "▼" : "");
        }

        // 底部固定区:全部取走 + 干草库存提示
        int footerY = yPositionOnScreen + FooterY + 30;
        if (HasLiveState())
        {
            DrawButton(b, new Rectangle(xPositionOnScreen + ContentLeft, footerY, 200, ButtonHeight), "全部取走");
            if (GetHayStock() > 0)
            {
                b.DrawString(Game1.smallFont, $"干草库存: {GetHayStock()} 份",
                    new Vector2(xPositionOnScreen + ContentLeft + 220, footerY + 4), new Color(120, 120, 120));
            }
        }
        else
        {
            b.DrawString(Game1.smallFont, "仅展示模式",
                new Vector2(xPositionOnScreen + ContentLeft, footerY + 4), new Color(120, 120, 120));
        }

        DrawNotice(b);
    }

    // ============================ 数据访问 ============================

    /// <summary>是否持有真实状态(带操作构造)。</summary>
    private bool HasLiveState() => _barn != null && _building != null;

    /// <summary>聚合所有房间的产品栈(key=物品ID|星级 -> 数量)。有真实状态用状态,否则用快照。</summary>
    private List<(string Id, int Quality, int Count)> GetAggregatedStacks()
    {
        var agg = new Dictionary<string, (string id, int q, int n)>();
        if (State is { } state)
        {
            foreach (var roomState in state.Rooms.Values)
                foreach (var (key, n) in roomState.ProduceStacks)
                    if (n > 0)
                    {
                        var (id, q) = ParseProduceKey(key);
                        if (!agg.TryGetValue(key, out var cur)) cur = (id, q, 0);
                        agg[key] = (id, q, cur.n + n);
                    }
        }
        else
        {
            foreach (RoomSnapshot r in _snapshot.Rooms)
                if (r.ProduceStacks != null)
                    foreach (var (key, n) in r.ProduceStacks)
                        if (n > 0)
                        {
                            var (id, q) = ParseProduceKey(key);
                            if (!agg.TryGetValue(key, out var cur)) cur = (id, q, 0);
                            agg[key] = (id, q, cur.n + n);
                        }
        }
        return agg.Values.OrderBy(v => v.id).ThenBy(v => v.q).Select(v => (v.id, v.q, v.n)).ToList();
    }

    /// <summary>解析产品分桶 key:"物品ID|星级"。旧存档无星级 → 普通(0)。</summary>
    private static (string Id, int Quality) ParseProduceKey(string key)
    {
        int pipe = key.LastIndexOf('|');
        if (pipe > 0 && int.TryParse(key[(pipe + 1)..], out int q))
            return (key[..pipe], q);
        return (key, 0);
    }

    /// <summary>星级显示名(0=空,1=银,2=金,4=铱)与颜色。</summary>
    private static (string Name, Color Color) QualityLabel(int q) => q switch
    {
        1 => ("★银", new Color(170, 190, 255)),
        2 => ("★金", new Color(255, 200, 60)),
        4 => ("★铱", new Color(180, 140, 255)),
        _ => ("", Game1.textColor),
    };

    private int GetHayStock() => State?.HayStock ?? _snapshot.HayStock;

    // ============================ 操作 ============================

    /// <summary>分发按钮动作。</summary>
    private void HandleButton(ClickableComponent button)
    {
        string? name = button.name;
        if (name == null) return;
        string kind = name;
        string arg = "";
        if (name.Split(':') is { } parts && parts.Length == 2)
        {
            kind = parts[0];
            arg = parts[1];
        }

        switch (kind)
        {
            case "buyAnimal": BuyAnimal(Enum.TryParse<RoomType>(arg, out var rt) ? rt : RoomType.Chicken); break;
            case "buyHay": BuyHay(int.Parse(arg)); break;
            case "takeOne": TakeOne(arg); break;
            case "pagePrev": _page = Math.Max(0, _page - 1); RebuildButtons(); break;
            case "pageNext": _page++; RebuildButtons(); break;
            case "takeAllProduce": TakeAllProduce(); break;
            // 干草不可取出(用户设计:只能买进),withdrawHay 已移除
            case "upgradeOverall": UpgradeOverall(); break;
            case "upgradeRoom": UpgradeRoom(Enum.TryParse<RoomType>(arg, out var ur) ? ur : RoomType.Chicken); break;
        }
    }

    /// <summary>取走单个产品(保留星级)。shift=5、ctrl+shift=25 批量(与购买一致)。
    /// arg 格式 "物品ID|星级"(分页后按钮直接传 id|quality)。</summary>
    private void TakeOne(string arg)
    {
        if (!GuardHostOnly()) return;
        var state = State;
        if (state == null) return;
        var parts = arg.Split('|');
        string id = parts[0];
        int quality = parts.Length > 1 && int.TryParse(parts[1], out int q) ? q : 0;
        // 取总数(跨房间聚合)
        int total = 0;
        if (state != null)
            foreach (var roomState in state.Rooms.Values)
                if (roomState.ProduceStacks.TryGetValue(AutoGrabberInterceptor.ProduceKey(id, quality), out int n))
                    total += n;
        if (total <= 0) return;

        // 批量数量(与购买同款:shift=5, ctrl+shift=25)
        int qty = 1;
        if (Game1.oldKBState.IsKeyDown(Keys.LeftShift) || Game1.oldKBState.IsKeyDown(Keys.RightShift))
            qty = 5;
        if (Game1.oldKBState.IsKeyDown(Keys.LeftControl) || Game1.oldKBState.IsKeyDown(Keys.RightControl))
            qty = 25;
        qty = Math.Min(qty, total);

        string key = AutoGrabberInterceptor.ProduceKey(id, quality);
        var item = ItemRegistry.Create(id, qty);
        item.Quality = quality;
        Game1.player.addItemByMenuIfNecessary(item);
        int leftover = item.Stack;
        int actuallyTook = qty - leftover;
        if (actuallyTook <= 0)
        {
            Notice("背包已满,无法取出", error: true);
            return;
        }
        TakeFromRooms(key, actuallyTook);
        Game1.playSound("coin");
        if (!Game1.IsMasterGame && _building != null)
            MultiplayerSync.ForwardWrite(MultiplayerSync.WriteOp.TakeProduce, _building, $"{id}|{quality}", actuallyTook);
        RefreshSnapshotCounts();
        RebuildButtons();
        // 显示正常物品名(不显示 (O)xxx 代码)
        string prodName = ItemRegistry.Create(id).DisplayName;
        string star = QualityLabel(quality).Name;
        Notice($"已取走 {actuallyTook} 个{star}{prodName}");
    }

    /// <summary>联机守卫:主机直接改;访客扣钱后把操作转发给主机执行(主机改状态 + modData 落盘同步)。
    /// 返回 false = 操作被拒绝(访客也应停)。</summary>
    private bool GuardHostOnly()
    {
        if (Game1.IsMasterGame) return true;
        // 访客:允许继续(扣钱/取货发生在本地),但状态变更由 MultiplayerSync.ForwardWrite 转主机
        return true;
    }

    /// <summary>数背包里某物品总数(木/石)。</summary>
    private static int CountInInventory(Farmer f, string qualifiedId)
    {
        int n = 0;
        foreach (var item in f.Items)
            if (item is SObject o && o.QualifiedItemId == qualifiedId) n += o.Stack;
        return n;
    }

    /// <summary>从背包扣 n 个某物品(逐格扣减,空槽移除)。返回是否成功扣满。</summary>
    private static bool ConsumeFromInventory(Farmer f, string qualifiedId, int amount)
    {
        if (amount <= 0) return true;
        if (CountInInventory(f, qualifiedId) < amount) return false;
        int remaining = amount;
        for (int i = f.Items.Count - 1; i >= 0 && remaining > 0; i--)
        {
            if (f.Items[i] is SObject o && o.QualifiedItemId == qualifiedId)
            {
                int take = Math.Min(remaining, o.Stack);
                o.Stack -= take;
                remaining -= take;
                if (o.Stack <= 0) f.Items[i] = null;
            }
        }
        return remaining <= 0;
    }

    /// <summary>整体升级:扣钱+木+石,提升整体等级(解锁新房间)。</summary>
    private void UpgradeOverall()
    {
        if (!GuardHostOnly()) return;
        var state = State;
        if (state == null) return;
        if (!UpgradeSystem.IsUnlocked(RoomType.Chicken, state.OverallLevel) || state.OverallLevel >= 5)
        {
            Notice("整体已达到最高等级", error: true);
            return;
        }
        var next = UpgradeSystem.Overall[Math.Clamp(state.OverallLevel, 0, 4)];
        var farmer = Game1.player;
        if (farmer.Money < next.Cost) { Notice($"金钱不足(需要 {next.Cost}g)", error: true); return; }
        if (CountInInventory(farmer, "(O)388") < next.Wood) { Notice($"木头不足(需要 {next.Wood})", error: true); return; }
        if (CountInInventory(farmer, "(O)390") < next.Stone) { Notice($"石头不足(需要 {next.Stone})", error: true); return; }

        farmer.Money -= next.Cost;
        ConsumeFromInventory(farmer, "(O)388", next.Wood);
        ConsumeFromInventory(farmer, "(O)390", next.Stone);
        state.OverallLevel++;
        Game1.playSound("reward");
        if (!Game1.IsMasterGame && _building != null)
            MultiplayerSync.ForwardWrite(MultiplayerSync.WriteOp.UpgradeOverall, _building, "", 0);

        // 完整刷新(整体等级/房间解锁/房间等级),UI 立即显示新等级 —— 修复「升级后仍显示旧等级」。
        RefreshSnapshotCounts();
        RebuildButtons();
        Notice($"养殖场升级到 {state.OverallLevel} 级!解锁: {next.Unlocks}");
    }

    /// <summary>房间升级:扣钱+木+石,提升房间容量。绵羊/山羊共用羊场(Goat) —— 升级必须落到
    /// RoomFor 后的实际房间,否则会建幽灵 "Sheep" 房间键、羊场容量永远不升。</summary>
    private void UpgradeRoom(RoomType room)
    {
        if (!GuardHostOnly()) return;
        var state = State;
        if (state == null) return;
        RoomType houseRoom = RoomDefinitions.RoomFor(room);   // Sheep→羊场(Goat);其余 1:1
        var roomState = state.GetRoom(houseRoom);
        var rows = UpgradeSystem.CapacityFor(houseRoom);
        string displayName = RoomDefinitions.Get(houseRoom).DisplayName;
        if (roomState.UpgradeLevel >= rows.Length - 1) { Notice($"「{displayName}」已满级", error: true); return; }

        var next = rows[roomState.UpgradeLevel + 1];
        var farmer = Game1.player;
        if (farmer.Money < next.Cost) { Notice($"金钱不足(需要 {next.Cost}g)", error: true); return; }
        if (next.Wood > 0 && CountInInventory(farmer, "(O)388") < next.Wood) { Notice($"木头不足(需要 {next.Wood})", error: true); return; }
        if (next.Stone > 0 && CountInInventory(farmer, "(O)390") < next.Stone) { Notice($"石头不足(需要 {next.Stone})", error: true); return; }

        farmer.Money -= next.Cost;
        if (next.Wood > 0) ConsumeFromInventory(farmer, "(O)388", next.Wood);
        if (next.Stone > 0) ConsumeFromInventory(farmer, "(O)390", next.Stone);
        roomState.UpgradeLevel++;
        Game1.playSound("reward");
        if (!Game1.IsMasterGame && _building != null)
            MultiplayerSync.ForwardWrite(MultiplayerSync.WriteOp.UpgradeRoom, _building, houseRoom.ToString(), 0);
        RefreshSnapshotCounts();
        RebuildButtons();
        Notice($"「{displayName}」升级到 {roomState.UpgradeLevel} 级,容量 {UpgradeSystem.CapacityAt(houseRoom, roomState.UpgradeLevel)}");
    }

    /// <summary>购买 1 只幼崽进房间台账(本地玩家扣钱)。绵羊/山羊共用一间「羊场」(RoomType.Goat);
    /// 台账 Room 记动物类型(结算产物/成熟天数按各自类型),实体生成/容量/解锁都落到实际房间。</summary>
    private void BuyAnimal(RoomType animalType)
    {
        if (!GuardHostOnly()) return;
        var info = FarmAnimalCatalog.Get(animalType);
        var state = State;
        if (state == null) return;
        RoomType houseRoom = RoomDefinitions.RoomFor(animalType);   // Sheep→Goat(羊场);其余 1:1
        var roomState = state.GetRoom(houseRoom);
        var farmer = Game1.player;
        int overallLevel = state.OverallLevel;
        string displayName = RoomDefinitions.Get(houseRoom).DisplayName;

        // 解锁检查(按实际房间)
        if (!UpgradeSystem.IsUnlocked(houseRoom, overallLevel))
        {
            Notice($"房间「{displayName}」尚未解锁(整体等级 {overallLevel})", error: true);
            return;
        }

        // 容量检查(按房间当前等级;与 SettlementService 一致用台账容量)
        var ledger = AnimalLedger.FromRoom(roomState);
        ledger.Capacity = UpgradeSystem.CapacityAt(houseRoom, roomState.UpgradeLevel);
        if (ledger.IsFull)
        {
            Notice($"房间「{displayName}」已满({ledger.Count}/{ledger.Capacity})", error: true);
            return;
        }

        // 批量数量(原版惯例:shift=5, ctrl+shift=25)
        int qty = 1;
        if (Game1.oldKBState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftShift) ||
            Game1.oldKBState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightShift))
            qty = 5;
        if (Game1.oldKBState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftControl) ||
            Game1.oldKBState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightControl))
            qty = 25;
        qty = Math.Min(qty, ledger.Capacity - ledger.Count);   // 装不下就少买

        // 钱包检查(按批量总价)
        int totalCost = info.BuyPrice * qty;
        if (farmer.Money < totalCost)
        {
            Notice($"金钱不足(需要 {totalCost}g 买 {qty} 只)", error: true);
            return;
        }

        // 扣钱(仅本地玩家;多人下各端钱包由网络同步)
        farmer.Money -= totalCost;

        // 批量买入
        for (int i = 0; i < qty; i++)
        {
            long id = Utility.RandomLong(Game1.random);
            ledger.TryAdd(new LedgerAnimal
            {
                Id = id,
                Room = animalType,          // 记动物类型(绵羊产物=羊毛,与山羊区分)
                TypeKey = info.TypeKey,
                AgeDays = info.MatureDays,           // 买成年动物(原版玛妮卖成年):成年才产蛋耗草,幼崽不产
                Friendship = 0,
                Happiness = 255,
                Fullness = 255,
                DaysSinceProduce = info.DaysToProduce,   // 买来即可产:满足阈值,当晚结算就产(原版玛妮卖的是能产的)
                ProduceCount = 0,
                OwnerId = farmer.UniqueMultiplayerID,
            });
        }
        ledger.SaveTo(roomState);
        if (!Game1.IsMasterGame && _building != null)
            MultiplayerSync.ForwardWrite(MultiplayerSync.WriteOp.BuyAnimal, _building, animalType.ToString(), qty);

        // 立即在房间里生成可见实体(前 30 只),玩家进门就能看到动物,不用等第二天结算。
        if (_building != null)
            RoomAnimalRenderer.SyncRoom(_building, houseRoom, roomState);

        Game1.playSound("coin");

        // 刷新内存快照(数量/等级/解锁),UI 立即更新
        RefreshSnapshotCounts();

        string name = AnimalDisplayNames.TryGetValue(info.TypeKey, out var cn) ? cn : info.TypeKey;
        Notice($"已购买 {qty} 只{name}({totalCost}g)");
    }

    /// <summary>购买干草(进全局库存)。</summary>
    private void BuyHay(int qty)
    {
        if (!GuardHostOnly()) return;
        var state = State;
        if (state == null) return;
        int actual = HaySystem.BuyHay(state, Game1.player, qty);
        if (actual <= 0)
        {
            Notice($"金钱不足(需要 {qty * HaySystem.DiscountPrice}g)", error: true);
            return;
        }
        Game1.playSound("coin");
        if (!Game1.IsMasterGame && _building != null)
            MultiplayerSync.ForwardWrite(MultiplayerSync.WriteOp.BuyHay, _building, "", actual);
        _snapshot.HayStock = state.HayStock;
        Notice($"已购入 {actual} 份干草({actual * HaySystem.DiscountPrice}g)");
    }

    /// <summary>取走单个产品堆叠(全部数量;装不下的进物品栏菜单,留在仓库)。</summary>
    /// <summary>全部取走:所有产品堆叠进背包(装不下的进物品栏菜单,留在仓库)。按星级分桶保留。</summary>
    private void TakeAllProduce()
    {
        if (!GuardHostOnly()) return;
        var state = State;
        if (state == null) return;
        var stacks = GetAggregatedStacks();
        if (stacks.Count == 0)
        {
            Notice("仓库是空的", error: true);
            return;
        }

        int totalTook = 0;
        foreach (var (id, quality, count) in stacks)
        {
            string key = AutoGrabberInterceptor.ProduceKey(id, quality);
            var item = ItemRegistry.Create(id, count);
            item.Quality = quality;
            Game1.player.addItemByMenuIfNecessary(item);
            int leftover = item.Stack;  // 没塞进去的剩余量
            int took = count - leftover;
            if (took <= 0) continue;
            TakeFromRooms(key, took);
            totalTook += took;
        }

        if (totalTook <= 0)
        {
            Notice("背包已满,无法取出", error: true);
            return;
        }
        Game1.playSound("coin");
        if (!Game1.IsMasterGame && _building != null)
            MultiplayerSync.ForwardWrite(MultiplayerSync.WriteOp.TakeAll, _building, "", 0);
        RefreshSnapshotCounts();
        RebuildButtons();
        Notice($"已取走 {totalTook} 件产品");
    }

    /// <summary>按 id 从各房间堆叠中扣减 take 个(先扣先得)。</summary>
    private void TakeFromRooms(string id, int take)
    {
        var state = State;
        if (state == null) return;
        int remaining = take;
        foreach (var roomState in state.Rooms.Values)
        {
            if (remaining <= 0) break;
            if (!roomState.ProduceStacks.TryGetValue(id, out int n) || n <= 0) continue;
            int removed = Math.Min(remaining, n);
            roomState.ProduceStacks[id] = n - removed;
            roomState.ProduceCount = Math.Max(0, roomState.ProduceCount - removed);
            remaining -= removed;
            if (roomState.ProduceStacks[id] <= 0) roomState.ProduceStacks.Remove(id);
        }
    }

    /// <summary>把真实状态完整刷回内存快照(UI 立即反映)。此前只刷数量/容量,不刷房间等级与整体等级,
    /// 导致「升级后仍显示等级 0 / 等级不变」——现在连 UpgradeLevel/Unlocked/OverallLevel 一起刷。</summary>
    private void RefreshSnapshotCounts()
    {
        if (State is not { } state) return;

        // 整体(等级/能否再升/下一档费用与解锁)
        _snapshot.OverallLevel = state.OverallLevel;
        _snapshot.CanUpgradeOverall = state.OverallLevel < 5;
        var next = state.OverallLevel < 5 ? UpgradeSystem.Overall[state.OverallLevel] : null;
        _snapshot.OverallUpgradeCost = next?.Cost ?? 0;
        _snapshot.OverallUpgradeUnlocks = next?.Unlocks ?? "";
        _snapshot.HayStock = state.HayStock;

        // 各房间(数量/容量/等级/解锁)
        foreach (var snap in _snapshot.Rooms)
        {
            var rs = state.GetRoom(snap.Room);
            snap.Count = rs.Animals.Count;
            snap.ProduceCount = rs.ProduceCount;
            snap.Capacity = UpgradeSystem.CapacityAt(snap.Room, rs.UpgradeLevel);
            snap.UpgradeLevel = rs.UpgradeLevel;
            snap.Unlocked = UpgradeSystem.IsUnlocked(snap.Room, state.OverallLevel);
        }
    }
}
