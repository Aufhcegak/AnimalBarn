using StardewValley;

namespace AnimalBarn;

/// <summary>养殖场地点识别:大堂/房间都用原版类型(AnimalHouse/GameLocation),
/// 靠地图属性标记区分(地图属性不随存档序列化,读档后重建,干净可靠)。
/// 所有需要识别养殖场地点的代码都走这里,禁止直接类型判断。</summary>
public static class AnimalBarnLocations
{
    /// <summary>地图属性名:养殖场大堂标记。</summary>
    public const string PropBarn = "xiepe.AnimalBarn";
    public const string PropLobby = "xiepe.AnimalBarn.Lobby";
    public const string PropHall = "xiepe.AnimalBarn.Hall";
    public const string PropRoomType = "xiepe.AnimalBarn.RoomType";

    /// <summary>是否养殖场地点(大堂或房间,查地图属性)。</summary>
    public static bool IsBarnLocation(GameLocation loc)
        => loc?.map?.Properties?.ContainsKey(PropBarn) == true;

    /// <summary>是否养殖场大堂。</summary>
    public static bool IsLobby(GameLocation loc)
        => loc?.map?.Properties?.ContainsKey(PropLobby) == true;

    /// <summary>是否养殖场门厅(统一入口,终端选房间)。</summary>
    public static bool IsHall(GameLocation loc)
        => loc?.map?.Properties?.ContainsKey(PropHall) == true;

    /// <summary>是否养殖场动物房间(查 RoomType 属性)。</summary>
    public static bool TryGetRoomType(GameLocation loc, out RoomType roomType)
    {
        roomType = default;
        if (loc?.map?.Properties?.TryGetValue(PropRoomType, out var val) != true)
            return false;
        return Enum.TryParse(val, out roomType);
    }

    /// <summary>给大堂地图打标记。</summary>
    public static void MarkLobby(xTile.Map map)
    {
        map.Properties[PropBarn] = "T";
        map.Properties[PropLobby] = "T";
    }

    /// <summary>给门厅地图打标记(统一入口,终端选房间)。</summary>
    public static void MarkHall(xTile.Map map)
    {
        map.Properties[PropBarn] = "T";
        map.Properties[PropHall] = "T";
    }

    /// <summary>给房间地图打标记。</summary>
    public static void MarkRoom(xTile.Map map, RoomType roomType)
    {
        map.Properties[PropBarn] = "T";
        map.Properties[PropRoomType] = roomType.ToString();
    }
}
