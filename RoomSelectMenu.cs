using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Menus;

namespace AnimalBarn;

/// <summary>房间选择菜单(门厅终端弹):列出 8 个房间(图标+名字+状态),点按钮直接 warp 进对应房间。
/// 这是"统一入口"的核心 —— 玩家不用走 8 扇门(踩空/锁死问题全消失),在门厅选完直达。</summary>
public class RoomSelectMenu : IClickableMenu
{
    private const int MenuWidth = 640;
    private const int MenuHeight = 460;
    private const int RowTop = 70;
    private const int RowHeight = 42;
    private const int ButtonX = 420;
    private const int ButtonWidth = 170;
    private const int ButtonHeight = 28;

    private readonly GameLocation _hall;
    private readonly Building _building;
    private readonly BarnManager _barn;
    private readonly BarnSaveData _state;
    private readonly List<ClickableComponent> _buttons = new();

    private static readonly Rectangle MenuBoxSrc = new(0, 256, 60, 60);

    /// <summary>门厅终端点击时创建(由 BarnPatches 或 ModEntry 调用)。</summary>
    public RoomSelectMenu(GameLocation hall, Building building, BarnManager barn)
        : base((Game1.uiViewport.Width - MenuWidth) / 2, (Game1.uiViewport.Height - MenuHeight) / 2,
               MenuWidth, MenuHeight, showUpperRightCloseButton: true)
    {
        _hall = hall;
        _building = building;
        _barn = barn;
        _state = barn.GetOrCreate(building);

        // 8 个房间按钮(与 RoomDefinitions.All 顺序一致)
        for (int i = 0; i < RoomDefinitions.All.Length; i++)
        {
            var def = RoomDefinitions.All[i];
            var roomState = _state.GetRoom(def.Room);
            bool unlocked = UpgradeSystem.IsUnlocked(def.Room, _state.OverallLevel);
            _buttons.Add(new ClickableComponent(
                new Rectangle(xPositionOnScreen + ButtonX, yPositionOnScreen + RowTop + i * RowHeight - 3,
                    ButtonWidth, ButtonHeight),
                "go:" + def.Room));
        }
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        base.receiveLeftClick(x, y, playSound);   // 右上角关闭

        foreach (var b in _buttons)
        {
            if (b.bounds.Contains(x, y) && b.name != null && b.name.StartsWith("go:"))
            {
                var room = Enum.Parse<RoomType>(b.name[3..]);
                var roomType = RoomDefinitions.RoomFor(room);
                if (!UpgradeSystem.IsUnlocked(roomType, _state.OverallLevel))
                {
                    Game1.showRedMessage("该房间尚未解锁(升级养殖场以解锁)");
                    return;
                }

                if (!Game1.IsMasterGame)
                {
                    // 联机黑屏修复:访客不能自己创建房间(主机 RequireLocation 找不到 = 黑屏)。
                    // 请主机创建房间并回 ack,收到后再 warp。
                    Game1.activeClickableMenu = null;
                    Game1.showRedMessage("正在请求房主准备房间…");
                    MultiplayerSync.RequestEnterRoom(_building, roomType);
                    return;
                }

                var target = RoomManager.GetOrCreate(_building, roomType, _hall);
                if (target == null) return;

                RoomAnimalRenderer.EnsureVisibleOnEnter(target);
                Game1.activeClickableMenu = null;   // 关菜单
                // 入口在北墙中央:落点在北门下方(DoorX, 3),玩家朝下进门。
                Game1.warpFarmer(target.NameOrUniqueName, RoomMapBuilder.DoorX, 3, 2);
                Game1.playSound("smallSelect");
                return;
            }
        }
    }

    public override void draw(SpriteBatch b)
    {
        drawTextureBox(b, Game1.menuTexture, MenuBoxSrc,
            xPositionOnScreen, yPositionOnScreen, MenuWidth, MenuHeight, Color.White, 1f, drawShadow: true);

        b.DrawString(Game1.dialogueFont, "选择房间",
            new Vector2(xPositionOnScreen + 24, yPositionOnScreen + 16), Game1.textColor);

        for (int i = 0; i < RoomDefinitions.All.Length; i++)
        {
            var def = RoomDefinitions.All[i];
            var roomState = _state.GetRoom(def.Room);
            bool unlocked = UpgradeSystem.IsUnlocked(def.Room, _state.OverallLevel);
            int rowY = yPositionOnScreen + RowTop + i * RowHeight;

            // 名字 + 状态(数量/容量 或 未解锁)
            string status = unlocked
                ? $"{roomState.Animals.Count}/{UpgradeSystem.CapacityAt(def.Room, roomState.UpgradeLevel)} 只"
                : "未解锁";
            b.DrawString(Game1.smallFont, $"{def.DisplayName}  {status}",
                new Vector2(xPositionOnScreen + 40, rowY + 4),
                unlocked ? Game1.textColor : new Color(140, 110, 90));

            // 按钮
            if (unlocked)
                DrawButton(b, new Rectangle(xPositionOnScreen + ButtonX, rowY - 3, ButtonWidth, ButtonHeight), "进入");
        }

        drawMouse(b);
    }

    private void DrawButton(SpriteBatch b, Rectangle rect, string text)
    {
        bool hovered = rect.Contains(Game1.getMouseX(), Game1.getMouseY());
        Color tint = hovered ? Color.White : Color.White * 0.88f;
        drawTextureBox(b, Game1.menuTexture, MenuBoxSrc, rect.X, rect.Y, rect.Width, rect.Height, tint, 1f, drawShadow: false);
        Vector2 size = Game1.smallFont.MeasureString(text);
        b.DrawString(Game1.smallFont, text,
            new Vector2(rect.X + (rect.Width - size.X) / 2f, rect.Y + (rect.Height - size.Y) / 2f), Game1.textColor);
    }
}
