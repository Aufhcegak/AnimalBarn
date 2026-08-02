namespace AnimalBarn;

/// <summary>干草消费结果(供调用方决定扣多少库存)。</summary>
public record SettleHayResult(int HayConsumed, int HungryAdults);

/// <summary>每日结算上下文。</summary>
public record SettleContext(
    int FriendshipGain,   // 每日自动护理好感增量(随整体等级 6-12)
    int HappinessGain     // 每日自动护理心情增量
);

/// <summary>每个房间的动物台账:全量数据 + 最多 30 只实体(渲染)。纯逻辑,可测试。</summary>
public class AnimalLedger
{
    public const int MaxVisible = 30;

    public readonly List<LedgerAnimal> Animals = new();
    public int Capacity = 100;         // 房间当前容量(由房间等级决定)
    public int ProduceCount;           // 未取产品总数
    public Dictionary<string, int> ProduceStacks = new();  // QualifiedId -> 数量

    public bool IsFull => Animals.Count >= Capacity;
    public int Count => Animals.Count;

    public bool TryAdd(LedgerAnimal a)
    {
        if (IsFull) return false;
        Animals.Add(a);
        return true;
    }

    /// <summary>取可渲染的实体动物(前 30 只,按 ID 稳定)。</summary>
    public List<LedgerAnimal> GetVisible() => Animals.Take(MaxVisible).ToList();

    /// <summary>每日结算(纯逻辑,由房间 DayUpdate 调用):喂食→成长→产产品→好感/心情。
    /// 干草规则:每只饥饿的成年动物(Fullness &lt;= 0)需要 1 份干草;已有饱食度的动物不耗草。
    /// 饥饿的动物按台账顺序依次喂,干草不足时排在后面的挨饿。
    /// 返回实际消耗的干草数和结算后仍饥饿的成年数(供调用方扣库存/提示)。</summary>
    public SettleHayResult SettleDay(SettleContext ctx, int hayAvailable)
    {
        int hungryAdults = Animals.Count(a => a.IsAdult && a.Fullness <= 0);
        int hayUsed = Math.Min(hayAvailable, hungryAdults);
        int hayConsumed = 0;

        foreach (var a in Animals)
        {
            if (a.IsAdult)
            {
                if (a.Fullness > 0 || hayUsed > 0)
                {
                    if (a.Fullness <= 0)
                    {
                        hayUsed--;
                        hayConsumed++;
                    }
                    a.Fullness = 255;
                    a.Happiness = Math.Min(255, a.Happiness + ctx.HappinessGain);
                    a.Friendship = Math.Min(1000, a.Friendship + ctx.FriendshipGain);
                    TryProduce(a);
                    a.DaysSinceProduce++;  // 结算日计入生产周期
                }
                else
                {
                    a.Happiness = Math.Max(0, a.Happiness - 100);
                    a.Friendship = Math.Max(0, a.Friendship - 20);
                    a.Fullness = 0;
                }
            }
            else
            {
                a.AgeDays++;  // 幼崽只成长,不耗草不产
            }
        }
        return new SettleHayResult(hayConsumed, hungryAdults - hayConsumed);
    }

    public void AddProduce(string qualifiedId)
    {
        ProduceCount++;
        ProduceStacks.TryGetValue(qualifiedId, out int n);
        ProduceStacks[qualifiedId] = n + 1;
    }

    /// <summary>生产判定:用结算前的 DaysSinceProduce 判阈值(产出的那天算新周期第 1 天),
    /// 保证 DaysToProduce 天后才产出(鸡=1 天、鸭=2 天)。
    /// 心情低于 70 时不产也不清零,间隔继续累积(心情恢复后补产)。</summary>
    private void TryProduce(LedgerAnimal a)
    {
        var info = FarmAnimalCatalog.Get(a.Room);
        if (a.Happiness >= 70 && a.DaysSinceProduce >= info.DaysToProduce && info.ProduceId != null)
        {
            a.DaysSinceProduce = 0;
            a.ProduceCount++;
            AddProduce(info.ProduceId);
        }
    }
}
