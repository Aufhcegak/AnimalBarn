using StardewValley;

namespace AnimalBarn;

/// <summary>养殖场房间(大堂也用此类)。继承 AnimalHouse 获得自动喂食/容量/动物管理。</summary>
public class AnimalBarnRoom : StardewValley.AnimalHouse
{
    public AnimalBarnRoom() { }
    public AnimalBarnRoom(string mapPath, string name) : base(mapPath, name) { }
}
