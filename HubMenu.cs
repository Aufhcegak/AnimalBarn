using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Menus;

namespace AnimalBarn;

/// <summary>中枢操作台菜单:4 页签(状态/升级/商店/仓库)。
/// 只传快照的构造仅做展示(测试用);带 BarnManager/建筑的构造支持购买/取货等真实操作。</summary>
public class HubMenu : IClickableMenu
{
    /// <summary>页签枚举。</summary>
    public enum Tab { Status, Upgrade, Shop, Warehouse }

    private const int MenuWidth = 800;
    private const int MenuHeight = 500;
    internal const int TabWidth = 120;
    internal const int TabHeight = 40;
    internal const int TabGap = 8;

    // 内容布局(相对菜单顶):行区固定高度,底部固定操作区,操作消息在最下
    private const int RowTop = 124;          // 行区顶部(动物/产品行)
    private const int RowHeight = 30;        // 行高
    private const int RowAreaHeight = 270;   // 可视行区高度(9 行)
    private const int FooterY = 400;         // 底部固定区(干草购买/全部取走)
    private const int NoticeY = 468;         // 操作结果消息行

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
        int x = xPositionOnScreen + 24;
        for (int i = 0; i < 4; i++)
        {
            _tabRects.Add(new Rectangle(x, yPositionOnScreen + 48, TabWidth, TabHeight));
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
        base.draw(b);
        drawBackground(b);

        // 标题
        b.DrawString(Game1.dialogueFont, "动物养殖场中枢",
            new Vector2(xPositionOnScreen + 24, yPositionOnScreen + 14), Game1.textColor);

        // 页签(当前页签高亮:亮底 + 白字;未激活:半透明底 + 灰字)
        string[] names = { "状态", "升级", "商店", "仓库" };
        for (int i = 0; i < _tabRects.Count; i++)
        {
            Rectangle r = _tabRects[i];
            bool active = i == (int)_tab;
            // 高亮条:当前页签加一条亮色顶边(视觉锚点,不改布局)
            if (active)
                b.Draw(Game1.mouseCursors, new Rectangle(r.X, r.Y - 3, r.Width, 3), new Rectangle(64, 256, 64, 64), Color.Gold);
            b.Draw(Game1.mouseCursors, r, new Rectangle(64, 256, 64, 64), active ? Color.White : Color.White * 0.55f);
            b.DrawString(Game1.smallFont, names[i],
                new Vector2(r.X + (r.Width - Game1.smallFont.MeasureString(names[i]).X) / 2f,
                            r.Y + (r.Height - Game1.smallFont.MeasureString(names[i]).Y) / 2f),
                active ? Game1.textColor : new Color(120, 120, 120));
        }

        // 内容区背景板(半透明深色,让文字有层级,不浮在边框上)
        b.Draw(Game1.mouseCursors,
            new Rectangle(xPositionOnScreen + 20, yPositionOnScreen + 88, MenuWidth - 40, MenuHeight - 120),
            new Rectangle(64, 256, 64, 64), Color.Black * 0.25f);

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
        b.Draw(Game1.mouseCursors,
            new Rectangle(xPositionOnScreen + 40, yPositionOnScreen + NoticeY - 4, 600, 26),
            new Rectangle(64, 256, 64, 64), Color.Black * 0.35f);
        b.DrawString(Game1.smallFont, _notice,
            new Vector2(xPositionOnScreen + 48, yPositionOnScreen + NoticeY), Color.Red);
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

    // ============================ 按钮与布局 ============================

    /// <summary>重建当前页签的按钮(页签切换/滚动时)。不重置 _scroll。</summary>
    private void RebuildButtons()
    {
        _buttons.Clear();

        if (_tab == Tab.Shop)
        {
            for (int i = 0; i < FarmAnimalCatalog.All.Length; i++)
            {
                _buttons.Add(new ClickableComponent(
                    new Rectangle(xPositionOnScreen + 604, yPositionOnScreen + RowTop + i * RowHeight, 150, 24),
                    $"buyAnimal:{FarmAnimalCatalog.All[i].Room}"));
            }
            for (int i = 0; i < HayQuantities.Length; i++)
            {
                _buttons.Add(new ClickableComponent(
                    new Rectangle(xPositionOnScreen + 220 + i * 140, yPositionOnScreen + FooterY + 24, 132, 24),
                    $"buyHay:{HayQuantities[i]}"));
            }
        }
        else if (_tab == Tab.Warehouse && HasLiveState())
        {
            var stacks = GetAggregatedStacks();
            for (int i = 0; i < stacks.Count; i++)
            {
                int rowY = yPositionOnScreen + RowTop + i * RowHeight - _scroll;
                if (rowY < yPositionOnScreen + RowTop || rowY > yPositionOnScreen + RowTop + RowAreaHeight - 24)
                    continue;  // 可视区外的行没有按钮
                _buttons.Add(new ClickableComponent(
                    new Rectangle(xPositionOnScreen + 604, rowY, 150, 24),
                    $"takeOne:{i}"));
            }
            _buttons.Add(new ClickableComponent(
                new Rectangle(xPositionOnScreen + 220, yPositionOnScreen + FooterY + 24, 180, 24),
                "takeAllProduce"));
            if (GetHayStock() > 0)
            {
                _buttons.Add(new ClickableComponent(
                    new Rectangle(xPositionOnScreen + 420, yPositionOnScreen + FooterY + 24, 180, 24),
                    "withdrawHay"));
            }
        }
    }

    /// <summary>绘制一个操作按钮(底图 + 文字;悬停时高亮)。</summary>
    private void DrawButton(SpriteBatch b, Rectangle rect, string text)
    {
        bool hovered = rect.Contains(Game1.getMouseX(), Game1.getMouseY());
        b.Draw(Game1.mouseCursors, rect, new Rectangle(64, 256, 64, 64), hovered ? Color.White : Color.White * 0.85f);
        Vector2 size = Game1.smallFont.MeasureString(text);
        b.DrawString(Game1.smallFont, text,
            new Vector2(rect.X + (rect.Width - size.X) / 2f, rect.Y + (rect.Height - size.Y) / 2f),
            hovered ? Color.White : Game1.textColor);
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
            var snap = _snapshot.Rooms.FirstOrDefault(r => r.Room == info.Room);
            string name = AnimalDisplayNames.TryGetValue(info.TypeKey, out var cn) ? cn : info.TypeKey;
            string note = snap == null
                ? ""
                : snap.Unlocked
                    ? $" ({snap.Count}/{snap.Capacity})"
                    : " (未解锁)";
            b.DrawString(Game1.smallFont, $"{name}{note} · {info.BuyPrice}g (原价 {info.VanillaPrice}g)",
                new Vector2(xPositionOnScreen + 40, rowY + 4), Game1.textColor);
            DrawButton(b, new Rectangle(xPositionOnScreen + 604, rowY, 150, 24), "购买 +1");
        }

        // 底部固定区:干草购买
        b.DrawString(Game1.smallFont,
            $"干草(每份 {HaySystem.DiscountPrice}g, 库存 {_snapshot.HayStock})",
            new Vector2(xPositionOnScreen + 40, yPositionOnScreen + FooterY), Game1.textColor);
        for (int i = 0; i < HayQuantities.Length; i++)
        {
            DrawButton(b,
                new Rectangle(xPositionOnScreen + 220 + i * 140, yPositionOnScreen + FooterY + 24, 132, 24),
                $"{HayQuantities[i]} 份 ({HayQuantities[i] * HaySystem.DiscountPrice}g)");
        }

        DrawNotice(b);
    }

    // ============================ 仓库页 ============================

    /// <summary>仓库页:各产品堆叠(图标+名称+数量) + 全部取走 + 取干草。</summary>
    private void DrawWarehouse(SpriteBatch b)
    {
        int headerY = yPositionOnScreen + RowTop;
        b.DrawString(Game1.smallFont, "产品仓库",
            new Vector2(xPositionOnScreen + 40, headerY - 28), Game1.textColor);

        var stacks = GetAggregatedStacks();
        int offset = Math.Min(_scroll, Math.Max(0, stacks.Count * RowHeight - RowAreaHeight));

        if (stacks.Count == 0)
        {
            b.DrawString(Game1.smallFont, "(仓库为空)",
                new Vector2(xPositionOnScreen + 40, yPositionOnScreen + RowTop + 4), new Color(120, 120, 120));
        }
        else
        {
            for (int i = 0; i < stacks.Count; i++)
            {
                int rowY = yPositionOnScreen + RowTop + i * RowHeight - offset;
                if (rowY < yPositionOnScreen + RowTop) continue;
                if (rowY > yPositionOnScreen + RowTop + RowAreaHeight - 4) break;

                var (id, count) = stacks[i];
                var item = ItemRegistry.Create(id);
                item.drawInMenu(b, new Vector2(xPositionOnScreen + 40, rowY - 2), 0.75f, 1f, 0.9f);
                b.DrawString(Game1.smallFont, $"{item.DisplayName} × {count}",
                    new Vector2(xPositionOnScreen + 66, rowY + 5), Game1.textColor);
                DrawButton(b, new Rectangle(xPositionOnScreen + 604, rowY, 150, 24), "取走");
            }
        }

        // 底部固定区:全部取走 + 取干草
        int footerY = yPositionOnScreen + FooterY + 24;
        if (HasLiveState())
        {
            DrawButton(b, new Rectangle(xPositionOnScreen + 220, footerY, 180, 24), "全部取走");
            int hay = GetHayStock();
            if (hay > 0)
            {
                DrawButton(b, new Rectangle(xPositionOnScreen + 420, footerY, 180, 24), $"取干草 ×{hay}");
            }
        }
        else
        {
            b.DrawString(Game1.smallFont, "(仅展示模式)",
                new Vector2(xPositionOnScreen + 40, footerY + 3), new Color(120, 120, 120));
        }

        DrawNotice(b);
    }

    // ============================ 数据访问 ============================

    /// <summary>是否持有真实状态(带操作构造)。</summary>
    private bool HasLiveState() => _barn != null && _building != null;

    /// <summary>聚合所有房间的产品栈(QualifiedId -> 数量)。有真实状态用状态,否则用快照。</summary>
    private List<(string Id, int Count)> GetAggregatedStacks()
    {
        var agg = new Dictionary<string, int>();
        if (State is { } state)
        {
            foreach (var roomState in state.Rooms.Values)
                foreach (var (id, n) in roomState.ProduceStacks)
                    if (n > 0)
                    {
                        agg.TryGetValue(id, out int cur);
                        agg[id] = cur + n;
                    }
        }
        else
        {
            foreach (RoomSnapshot r in _snapshot.Rooms)
                if (r.ProduceStacks != null)
                    foreach (var (id, n) in r.ProduceStacks)
                        if (n > 0)
                        {
                            agg.TryGetValue(id, out int cur);
                            agg[id] = cur + n;
                        }
        }
        return agg.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)).ToList();
    }

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
            case "takeOne": TakeOne(int.Parse(arg)); break;
            case "takeAllProduce": TakeAllProduce(); break;
            case "withdrawHay": WithdrawHay(); break;
        }
    }

    /// <summary>购买 1 只幼崽进房间台账(本地玩家扣钱)。</summary>
    private void BuyAnimal(RoomType room)
    {
        var info = FarmAnimalCatalog.Get(room);
        var state = State;
        if (state == null) return;
        var roomState = state.GetRoom(room);
        var farmer = Game1.player;
        int overallLevel = state.OverallLevel;
        string displayName = RoomDefinitions.Get(room).DisplayName;

        // 解锁检查
        if (!UpgradeSystem.IsUnlocked(room, overallLevel))
        {
            Notice($"房间「{displayName}」尚未解锁(整体等级 {overallLevel})", error: true);
            return;
        }

        // 容量检查(按房间当前等级;与 SettlementService 一致用台账容量)
        var ledger = AnimalLedger.FromRoom(roomState);
        ledger.Capacity = UpgradeSystem.CapacityAt(room, roomState.UpgradeLevel);
        if (ledger.IsFull)
        {
            Notice($"房间「{displayName}」已满({ledger.Count}/{ledger.Capacity})", error: true);
            return;
        }

        // 钱包检查
        if (farmer.Money < info.BuyPrice)
        {
            Notice($"金钱不足(需要 {info.BuyPrice}g)", error: true);
            return;
        }

        // 扣钱(仅本地玩家;多人下各端钱包由网络同步)
        farmer.Money -= info.BuyPrice;

        // 唯一 ID:与原生一致用 Utility.RandomLong(64 位随机,本局碰撞概率可忽略)
        long id = Utility.RandomLong(Game1.random);
        ledger.TryAdd(new LedgerAnimal
        {
            Id = id,
            Room = room,
            TypeKey = info.TypeKey,
            AgeDays = 0,
            Friendship = 0,
            Happiness = 255,
            Fullness = 255,
            DaysSinceProduce = 0,
            ProduceCount = 0,
            OwnerId = farmer.UniqueMultiplayerID,
        });
        ledger.SaveTo(roomState);

        Game1.playSound("coin");

        // 刷新内存快照(数量),UI 立即更新
        RefreshSnapshotCounts();

        string name = AnimalDisplayNames.TryGetValue(info.TypeKey, out var cn) ? cn : info.TypeKey;
        Notice($"已购买 1 只{name}({info.BuyPrice}g)");
    }

    /// <summary>购买干草(进全局库存)。</summary>
    private void BuyHay(int qty)
    {
        var state = State;
        if (state == null) return;
        int actual = HaySystem.BuyHay(state, Game1.player, qty);
        if (actual <= 0)
        {
            Notice($"金钱不足(需要 {qty * HaySystem.DiscountPrice}g)", error: true);
            return;
        }
        Game1.playSound("coin");
        _snapshot.HayStock = state.HayStock;
        Notice($"已购入 {actual} 份干草({actual * HaySystem.DiscountPrice}g)");
    }

    /// <summary>取走单个产品堆叠(全部数量;装不下的进物品栏菜单,留在仓库)。</summary>
    private void TakeOne(int index)
    {
        var state = State;
        if (state == null) return;
        var stacks = GetAggregatedStacks();
        if (index < 0 || index >= stacks.Count) return;
        var (id, count) = stacks[index];

        var item = ItemRegistry.Create(id, count);
        Game1.player.addItemByMenuIfNecessary(item);
        int leftover = item.Stack;  // 没塞进去的剩余量(可能排队进物品栏菜单)
        int actuallyTook = count - leftover;
        if (actuallyTook <= 0)
        {
            Notice("背包已满,无法取出", error: true);
            return;
        }
        TakeFromRooms(id, actuallyTook);
        Game1.playSound("coin");
        RefreshSnapshotCounts();
        RebuildButtons();
        Notice($"已取走 {actuallyTook} 个");
    }

    /// <summary>全部取走:所有产品堆叠进背包(装不下的进物品栏菜单,留在仓库)。</summary>
    private void TakeAllProduce()
    {
        var state = State;
        if (state == null) return;
        var stacks = GetAggregatedStacks();
        if (stacks.Count == 0)
        {
            Notice("仓库是空的", error: true);
            return;
        }

        int totalTook = 0;
        foreach (var (id, count) in stacks)
        {
            var item = ItemRegistry.Create(id, count);
            Game1.player.addItemByMenuIfNecessary(item);
            int leftover = item.Stack;  // 没塞进去的剩余量
            int took = count - leftover;
            if (took <= 0) continue;
            TakeFromRooms(id, took);
            totalTook += took;
        }

        if (totalTook <= 0)
        {
            Notice("背包已满,无法取出", error: true);
            return;
        }
        Game1.playSound("coin");
        RefreshSnapshotCounts();
        RebuildButtons();
        Notice($"已取走 {totalTook} 件产品");
    }

    /// <summary>取干草进背包(装不下的部分进物品栏菜单,留在库存)。</summary>
    private void WithdrawHay()
    {
        var state = State;
        if (state == null) return;
        if (state.HayStock <= 0)
        {
            Notice("干草库存是空的", error: true);
            return;
        }
        int actual = HaySystem.WithdrawHay(state, Game1.player, state.HayStock);
        if (actual <= 0)
        {
            Notice("背包已满,无法取出干草", error: true);
            return;
        }
        Game1.playSound("coin");
        _snapshot.HayStock = state.HayStock;
        RebuildButtons();
        Notice($"已取出 {actual} 份干草");
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

    /// <summary>把真实状态的数量刷回内存快照(UI 立即反映)。</summary>
    private void RefreshSnapshotCounts()
    {
        if (State is not { } state) return;
        foreach (var snap in _snapshot.Rooms)
        {
            var rs = state.GetRoom(snap.Room);
            snap.Count = rs.Animals.Count;
            snap.ProduceCount = rs.ProduceCount;
            snap.Capacity = UpgradeSystem.CapacityAt(snap.Room, rs.UpgradeLevel);
        }
        _snapshot.HayStock = state.HayStock;
    }
}
