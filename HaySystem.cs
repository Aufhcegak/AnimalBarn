using StardewValley;

namespace AnimalBarn;

/// <summary>干草系统:全局库存 + 进货(9折) + 手动存取。
/// 干草库存 = BarnSaveData.HayStock;进货/手动存都进这里,每日结算从这扣。</summary>
public static class HaySystem
{
    public const int HayItemId = 178;
    public const int VanillaHayPrice = 50;
    public const int DiscountPrice = 45;  // 9 折

    /// <summary>进货:从玩家钱包扣款,干草进全局库存。返回实际购买数量。</summary>
    public static int BuyHay(BarnSaveData state, Farmer farmer, int quantity)
    {
        if (quantity <= 0) return 0;
        int cost = quantity * DiscountPrice;
        if (farmer.Money < cost) return 0;
        farmer.Money -= cost;
        state.HayStock += quantity;
        return quantity;
    }

    /// <summary>手动存入:背包里的干草放进去。返回存入数量。</summary>
    public static int DepositHay(BarnSaveData state, Farmer farmer, int quantity)
    {
        if (quantity <= 0) return 0;
        var hay = farmer.Items.FirstOrDefault(i => i?.QualifiedItemId == "(O)178" && i.Stack > 0);
        if (hay == null) return 0;
        int take = Math.Min(quantity, hay.Stack);
        hay.Stack -= take;
        if (hay.Stack <= 0) farmer.removeItemFromInventory(hay);
        state.HayStock += take;
        return take;
    }

    /// <summary>手动取出:从全局库存拿干草进背包。返回取出数量。
    /// 实际放入量 = 数背包前后差(干草 (O)178 同 ID 堆叠,数前后精确)。
    /// ⚠️ 不能靠 item.Stack 差值:原版 addItemToInventory 放进【空格】时返回的 item.Stack 不减
    /// (Farmer.cs:4318)——背包里没有干草时 Stack 差值 = 0,会误判"背包满"且干草白进背包不扣库存。
    /// 数背包前后差:纯合并/合并+放空格/纯空格/放不下全部精确(取多少扣多少,绝不凭空造干草)。</summary>
    public static int WithdrawHay(BarnSaveData state, Farmer farmer, int quantity)
    {
        if (quantity <= 0) return 0;
        int take = Math.Min(quantity, state.HayStock);
        if (take <= 0) return 0;
        var item = ItemRegistry.Create("(O)178", take);
        int before = CountHay(farmer);
        farmer.addItemToInventoryBool(item);
        int actual = CountHay(farmer) - before;
        if (actual > 0)
        {
            state.HayStock -= actual;
            return actual;
        }
        return 0;  // 背包满则失败
    }

    /// <summary>数背包里干草 (O)178 总数(含各堆叠)。</summary>
    private static int CountHay(Farmer farmer)
    {
        int n = 0;
        foreach (var i in farmer.Items)
            if (i?.QualifiedItemId == "(O)178")
                n += i.Stack;
        return n;
    }

    /// <summary>当前库存。</summary>
    public static int GetStock(BarnSaveData state) => state.HayStock;

    /// <summary>今天有多少动物需要喂(显示用)。</summary>
    public static int DailyHayNeeded(BarnSaveData state)
    {
        int total = 0;
        foreach (var roomState in state.Rooms.Values)
            total += roomState.Animals.Count(a => a.IsAdult && a.Fullness <= 0);
        return total;
    }
}
