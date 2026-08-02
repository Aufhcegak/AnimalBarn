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
}
