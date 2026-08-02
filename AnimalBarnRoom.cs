using StardewValley;

namespace AnimalBarn;

/// <summary>养殖场房间(大堂也用此类)。继承 AnimalHouse 获得自动喂食/容量/动物管理。</summary>
public class AnimalBarnRoom : StardewValley.AnimalHouse
{
    /// <summary>本房间类型。null = 大堂(只做大厅行为,不结算)。由门系统设置。</summary>
    public RoomType? RoomType;

    public AnimalBarnRoom() { }
    public AnimalBarnRoom(string mapPath, string name) : base(mapPath, name) { }

    /// <summary>每日结算:实体动物走原生结算(喂食/产/好感,含 AutoFeed 干草槽),台账动物走纯逻辑结算。</summary>
    public override void DayUpdate(int dayOfMonth)
    {
        base.DayUpdate(dayOfMonth);  // 实体动物原生结算(喂食/产/好感)
        if (Game1.IsMasterGame && RoomType != null)
        {
            SettlementService.SettleRoom(this);
        }
    }

    /// <summary>中枢操作台:玩家在大堂点击中枢台 tile → 打开中枢菜单(仅大堂有,房间内走原生交互)。</summary>
    public override bool checkAction(xTile.Dimensions.Location tileLocation, xTile.Dimensions.Rectangle viewport, Farmer who)
    {
        if (RoomType == null &&
            LobbyMapBuilder.IsHubTile(tileLocation.X, tileLocation.Y) &&
            who != null && who.IsLocalPlayer)
        {
            var barn = ModEntry.Instance.Barn;
            var building = ParentBuilding;
            if (barn != null && building != null)
            {
                var state = barn.GetOrCreate(building);
                var snapshot = HubSnapshotBuilder.Build(state);
                Game1.activeClickableMenu = new HubMenu(snapshot, barn, building);
                Game1.playSound("bigSelect");
                return true;
            }
        }
        return base.checkAction(tileLocation, viewport, who);
    }
}
