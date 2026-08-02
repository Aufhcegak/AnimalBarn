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

    // 内容区边界(页签下方到干草行上方)
    private const int ContentTop = 96;
    private const int ContentBottom = 434;

    private readonly HubSnapshot _snapshot;
    private Tab _tab = Tab.Status;
    private readonly List<Rectangle> _tabRects = new();

    // 真实操作依赖(由中枢操作台打开时注入;纯展示构造为 null)
    private readonly BarnManager? _barn;
    private readonly Building? _building;

    /// <summary>商店/仓库页的可点击按钮(页签切换时重建)。</summary>
    private readonly List<ClickableComponent> _buttons = new();

    /// <summary>列表滚动偏移(商店/仓库页)。</summary>
    private int _scroll;

    /// <summary>最后一次操作的结果消息(在内容区顶部显示)。</summary>
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

    /// <summary>滚轮滚动列表(商店/仓库页)。</summary>
    public override void receiveScrollWheelAction(int direction)
    {
        base.receiveScrollWheelAction(direction);
        if (_tab is Tab.Shop or Tab.Warehouse)
        {
            _scroll = Math.Max(0, _scroll - direction / 120 * 24);  // 上滚(负)减偏移
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
            case Tab.Shop: DrawShop(b); break;
            case Tab.Warehouse: DrawWarehouse(b); break;
        }

        drawMouse(b);
    }

    /// <summary>操作结果消息(绿色 HUD 提示 / 区内红色提示)。</summary>
    private void Notice(string msg, bool error = false)
    {
        if (error)
        {
            _notice = msg;
            Game1.playSound("cancel");
        }
        else
        {
            _notice = msg;
            Game1.addHUDMessage(new HUDMessage(msg, 4));  // HUDMessage(string, int type) 绿色类型 4
        }
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

    // ============================ 商店页 ============================

    /// <summary>重建按钮列表(页签/滚动变化时)。返回按钮所属行的起始 y(供绘制时对齐)。</summary>
    private void RebuildButtons()
    {
        _buttons.Clear();
        _scroll = 0;

        if (_tab == Tab.Shop)
        {
            int y = yPositionOnScreen + ContentTop + 28;
            for (int i = 0; i < FarmAnimalCatalog.All.Length; i++)
            {
                var info = FarmAnimalCatalog.All[i];
                _buttons.Add(new ClickableComponent(
                    new Rectangle(xPositionOnScreen + 604, y, 150, 24),
                    $"buyAnimal:{info.Room}"));
                y += 34;
            }
            y += 8;
            for (int i = 0; i < HayQuantities.Length; i++)
            {
                _buttons.Add(new ClickableComponent(
                    new Rectangle(xPositionOnScreen + 220 + i * 140, y, 132, 24),
                    $"buyHay:{HayQuantities[i]}"));
            }
        }
        else if (_tab == Tab.Warehouse)
        {
            int y = yPositionOnScreen + ContentTop + 28;
            if (HasLiveState())
            {
                var stacks = GetAggregatedStacks();
                for (int i = 0; i < stacks.Count; i++)
                {
                    _buttons.Add(new ClickableComponent(
                        new Rectangle(xPositionOnScreen + 604, y, 150, 24),
                        $"takeOne:{i}"));
                    y += 30;
                }
                y += 6;
                _buttons.Add(new ClickableComponent(
                    new Rectangle(xPositionOnScreen + 220, y, 180, 24),
                    "takeAllProduce"));
                if (GetHayStock() > 0)
                {
                    _buttons.Add(new ClickableComponent(
                        new Rectangle(xPositionOnScreen + 420, y, 180, 24),
                        "withdrawHay"));
                }
            }
        }
    }

    /// <summary>商店页:9 种动物(名称·9折价/原价+购买按钮)+ 干草购买档位。</summary>
    private void DrawShop(SpriteBatch b)
    {
        int y = yPositionOnScreen + ContentTop;
        b.DrawString(Game1.smallFont, "动物幼崽(九折)",
            new Vector2(xPositionOnScreen + 40, y), Game1.textColor);
        y += 28;

        foreach (var info in FarmAnimalCatalog.All)
        {
            var snap = _snapshot.Rooms.FirstOrDefault(r => r.Room == info.Room);
            string name = AnimalDisplayNames.TryGetValue(info.TypeKey, out var cn) ? cn : info.TypeKey;
            string note = snap == null
                ? ""
                : snap.Unlocked
                    ? $" ({snap.Count}/{snap.Capacity})"
                    : " (未解锁)";
            b.DrawString(Game1.smallFont, $"{name}{note} · {info.BuyPrice}g (原价 {info.VanillaPrice}g)",
                new Vector2(xPositionOnScreen + 40, y), Game1.textColor);
            b.Draw(Game1.mouseCursors,
                new Rectangle(xPositionOnScreen + 604, y - 2, 150, 26),
                new Rectangle(64, 256, 64, 64), Color.White * 0.9f);
            b.DrawString(Game1.smallFont, "购买 +1",
                new Vector2(xPositionOnScreen + 636, y + 6), Game1.textColor);
            y += 34;
        }

        y += 8;
        b.DrawString(Game1.smallFont, $"干草(每份 {HaySystem.DiscountPrice}g, 库存 {_snapshot.HayStock})",
            new Vector2(xPositionOnScreen + 40, y), Game1.textColor);
        y += 26;
        for (int i = 0; i < HayQuantities.Length; i++)
        {
            b.Draw(Game1.mouseCursors,
                new Rectangle(xPositionOnScreen + 220 + i * 140, y - 2, 132, 26),
                new Rectangle(64, 256, 64, 64), Color.White * 0.9f);
            b.DrawString(Game1.smallFont, $"{HayQuantities[i]} 份 ({HayQuantities[i] * HaySystem.DiscountPrice}g)",
                new Vector2(xPositionOnScreen + 234 + i * 140, y + 6), Game1.textColor);
        }

        if (_notice != "")
        {
            b.DrawString(Game1.smallFont, _notice,
                new Vector2(xPositionOnScreen + 40, yPositionOnScreen + ContentBottom - 22),
                Color.Red);
        }
    }

    // ============================ 仓库页 ============================

    /// <summary>仓库页:各产品堆叠(图标+名称+数量) + 全部取走 + 取干草。</summary>
    private void DrawWarehouse(SpriteBatch b)
    {
        int y = yPositionOnScreen + ContentTop;
        if (!HasLiveState())
        {
            b.DrawString(Game1.smallFont, "仓库(仅展示模式,无操作)",
                new Vector2(xPositionOnScreen + 40, y), new Color(120, 120, 120));
            return;
        }

        var stacks = GetAggregatedStacks();
        b.DrawString(Game1.smallFont, $"产品仓库(共 {stacks.Count} 种)",
            new Vector2(xPositionOnScreen + 40, y), Game1.textColor);
        y += 28;

        if (stacks.Count == 0)
        {
            b.DrawString(Game1.smallFont, "(仓库为空)",
                new Vector2(xPositionOnScreen + 40, y), new Color(120, 120, 120));
            y += 26;
        }
        else
        {
            int offset = Math.Min(_scroll, Math.Max(0, stacks.Count * 30 - (ContentBottom - 36 - y)));
            for (int i = 0; i < stacks.Count; i++)
            {
                var (id, count) = stacks[i];
                int rowY = y + i * 30 - offset;
                if (rowY < yPositionOnScreen + ContentTop) continue;
                if (rowY > yPositionOnScreen + ContentBottom - 6) break;

                var item = ItemRegistry.Create(id);
                item.drawInMenu(b, new Vector2(xPositionOnScreen + 40, rowY - 2), 0.75f, 1f, 0.9f);
                string name = item.DisplayName;
                b.DrawString(Game1.smallFont, $"{name} × {count}",
                    new Vector2(xPositionOnScreen + 66, rowY + 6), Game1.textColor);

                b.Draw(Game1.mouseCursors,
                    new Rectangle(xPositionOnScreen + 604, rowY, 150, 24),
                    new Rectangle(64, 256, 64, 64), Color.White * 0.9f);
                b.DrawString(Game1.smallFont, "取走",
                    new Vector2(xPositionOnScreen + 650, rowY + 5), Game1.textColor);
            }
            y += stacks.Count * 30 - offset + 4;
        }

        b.Draw(Game1.mouseCursors,
            new Rectangle(xPositionOnScreen + 220, y, 180, 24),
            new Rectangle(64, 256, 64, 64), Color.White * 0.9f);
        b.DrawString(Game1.smallFont, "全部取走",
            new Vector2(xPositionOnScreen + 262, y + 5), Game1.textColor);

        int hay = GetHayStock();
        if (hay > 0)
        {
            b.Draw(Game1.mouseCursors,
                new Rectangle(xPositionOnScreen + 420, y, 180, 24),
                new Rectangle(64, 256, 64, 64), Color.White * 0.9f);
            b.DrawString(Game1.smallFont, $"取干草 ×{hay}",
                new Vector2(xPositionOnScreen + 470, y + 5), Game1.textColor);
        }

        if (_notice != "")
        {
            b.DrawString(Game1.smallFont, _notice,
                new Vector2(xPositionOnScreen + 40, yPositionOnScreen + ContentBottom - 22),
                Color.Red);
        }
    }

    // ============================ 数据访问 ============================

    /// <summary>是否持有真实状态(带操作构造)。</summary>
    private bool HasLiveState() => _barn != null && _building != null;

    /// <summary>聚合所有房间的产品栈(QualifiedId -> 数量)。</summary>
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
            // 纯展示模式:从快照的房间产品栈聚合
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
        var (kind, arg) = button.name.Split(':') is { } parts && parts.Length == 2
            ? (parts[0], parts[1])
            : (button.name, "");

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

        // 解锁检查
        if (!UpgradeSystem.IsUnlocked(room, overallLevel))
        {
            Notice($"房间「{RoomDefinitions.Get(room).DisplayName}」尚未解锁(整体等级 {overallLevel})", error: true);
            return;
        }

        // 容量检查(按房间当前等级)
        int capacity = UpgradeSystem.CapacityAt(room, roomState.UpgradeLevel);
        if (roomState.Animals.Count >= capacity)
        {
            Notice($"房间「{RoomDefinitions.Get(room).DisplayName}」已满({roomState.Animals.Count}/{capacity})", error: true);
            return;
        }

        // 钱包检查
        if (farmer.Money < info.BuyPrice)
        {
            Notice($"金钱不足(需要 {info.BuyPrice}g)", error: true);
            return;
        }

        // 扣钱(仅本地玩家;多人下主机/客机的钱包由网络同步)
        farmer.Money -= info.BuyPrice;

        // 唯一 ID:时间戳哈希 + 随机数(Utility.RandomLong),保证本局唯一
        long id = unchecked((long)DateTime.UtcNow.Ticks ^ (long)Utility.NewUniqueIdForThisGame()) ^ Utility.RandomLong(Game1.random);
        roomState.Animals.Add(new LedgerAnimal
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

    /// <summary>取走单个产品堆叠(全部数量)。</summary>
    private void TakeOne(int index)
    {
        var state = State;
        if (state == null) return;
        var stacks = GetAggregatedStacks();
        if (index < 0 || index >= stacks.Count) return;
        var (id, count) = stacks[index];

        // 从各房间的堆叠中扣减并移入背包(背包放不下的部分进物品栏菜单,不扣减)
        int remaining = count;
        foreach (var roomState in state.Rooms.Values)
        {
            if (remaining <= 0) break;
            if (!roomState.ProduceStacks.TryGetValue(id, out int n) || n <= 0) continue;
            int take = Math.Min(remaining, n);
            remaining -= take;
        }
        // 上面只算"应扣量";以 addItemByMenuIfNecessary 实际入包数为准
        int before = remaining;
        var item = ItemRegistry.Create(id, count);
        Game1.player.addItemByMenuIfNecessary(item);
        int leftover = item.Stack;  // 没塞进去的剩余量(可能走物品栏菜单)
        int actuallyTook = count - leftover;
        if (actuallyTook <= 0)
        {
            Notice("背包已满,无法取出", error: true);
            return;
        }
        TakeFromRooms(id, actuallyTook);
        Game1.playSound("coin");
        RefreshSnapshotCounts();
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
        int before = state.HayStock;
        int actual = HaySystem.WithdrawHay(state, Game1.player, state.HayStock);
        if (actual <= 0)
        {
            Notice("背包已满,无法取出干草", error: true);
            return;
        }
        Game1.playSound("coin");
        _snapshot.HayStock = state.HayStock;
        if (state.HayStock <= 0) RebuildButtons();
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
