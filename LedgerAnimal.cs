namespace AnimalBarn;

/// <summary>台账中的一只动物。非实体,只记录结算所需数据。</summary>
public class LedgerAnimal
{
    public long Id;
    public RoomType Room;
    public string TypeKey = "";
    public int AgeDays;
    public int Friendship;      // 0-1000
    public int Happiness;       // 0-255
    public int Fullness;        // 0-255
    public int DaysSinceProduce;
    public int ProduceCount;    // 累计产品数
    public long OwnerId;        // 房主

    public bool IsAdult => AgeDays >= FarmAnimalCatalog.Get(Room).MatureDays;
}
