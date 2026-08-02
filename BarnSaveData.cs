namespace AnimalBarn;

/// <summary>存档数据(序列化为 JSON 存 Building.ModData)。RoomType 以字符串存储(enum 名)。</summary>
public class BarnSaveData
{
    public int OverallLevel = 1;
    public int HayStock = 0;
    public int ProduceCount = 0;
    public Dictionary<string, int> GlobalProduceStacks = new();
    public Dictionary<string, RoomSaveData> Rooms = new();  // key = RoomType.ToString()

    public class RoomSaveData
    {
        public int UpgradeLevel = 0;
        public int ProduceCount = 0;
        public Dictionary<string, int> ProduceStacks = new();
        public List<LedgerAnimal> Animals = new();
    }

    public RoomSaveData GetRoom(RoomType room)
    {
        var key = room.ToString();
        if (!Rooms.TryGetValue(key, out var data))
        {
            data = new RoomSaveData();
            Rooms[key] = data;
        }
        return data;
    }

    public bool HasRoom(RoomType room) => Rooms.ContainsKey(room.ToString());
}
