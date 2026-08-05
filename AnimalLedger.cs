namespace AnimalBarn;

/// <summary>干草消费结果(供调用方决定扣多少库存)。</summary>
public record SettleHayResult(int HayConsumed, int HungryAdults);

/// <summary>每日结算上下文。</summary>
public record SettleContext(
    int FriendshipGain,   // 每日自动护理好感增量(随整体等级 6-12)
    int HappinessGain,    // 每日自动护理心情增量
    int DaySeed = 0       // 每日确定性种子(主机传 Game1.stats.DaysPlayed;同一天所有端一致 → 星级判定联机同步)
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

    /// <summary>从房间存档状态构建台账(容量由调用方按房间等级设置)。</summary>
    public static AnimalLedger FromRoom(BarnSaveData.RoomSaveData roomState)
    {
        var ledger = new AnimalLedger();
        ledger.Animals.AddRange(roomState.Animals);
        foreach (var (k, v) in roomState.ProduceStacks) ledger.ProduceStacks[k] = v;
        ledger.ProduceCount = roomState.ProduceCount;
        return ledger;
    }

    /// <summary>台账写回房间存档状态(结算后调用)。</summary>
    public void SaveTo(BarnSaveData.RoomSaveData roomState)
    {
        roomState.Animals.Clear();
        roomState.Animals.AddRange(Animals);
        roomState.ProduceStacks.Clear();
        foreach (var (k, v) in ProduceStacks) roomState.ProduceStacks[k] = v;
        roomState.ProduceCount = ProduceCount;
    }

    public bool TryAdd(LedgerAnimal a)
    {
        if (IsFull) return false;
        Animals.Add(a);
        return true;
    }

    /// <summary>取可渲染的实体动物(前 30 只,按 ID 稳定)。</summary>
    public List<LedgerAnimal> GetVisible() => Animals.Take(MaxVisible).ToList();

    /// <summary>每日结算(纯逻辑,由房间 DayUpdate 调用):喂食→成长→产产品→好感/心情。
    /// <paramref name="excludeIds"/> = 有实体的动物 id(该批已由原版 base.DayUpdate 结算:
    /// 喂食走 AutoFeed 免费、产蛋走原生规则、好感走 dayUpdate) —— 台账跳过它们,避免双结算
    /// (双份产物/双扣草/双好感)。只结算纯台账动物。
    /// 干草规则:每只饥饿的成年动物(Fullness &lt;= 0)需要 1 份干草;已有饱食度的动物不耗草。
    /// 饥饿的动物按台账顺序依次喂,干草不足时排在后面的挨饿。
    /// 返回实际消耗的干草数和结算后仍饥饿的成年数(供调用方扣库存/提示)。</summary>
    public SettleHayResult SettleDay(SettleContext ctx, int hayAvailable, HashSet<long>? excludeIds = null)
    {
        // ⚠️ 判"饿"用 fullness < 200(原版 FarmAnimal.dayUpdate 源码: fullness<200 → 心情-100/好感-20)
        int hungryAdults = Animals.Count(a => a.IsAdult && a.Fullness < 200 && !IsExcluded(a, excludeIds));
        int hayUsed = Math.Min(hayAvailable, hungryAdults);
        int hayConsumed = 0;

        foreach (var a in Animals)
        {
            if (IsExcluded(a, excludeIds)) continue;   // 实体动物:原版已结算,台账不碰
            if (a.IsAdult)
            {
                if (a.Fullness >= 200 || hayUsed > 0)
                {
                    if (a.Fullness < 200)
                    {
                        hayUsed--;
                        hayConsumed++;
                    }
                    a.Fullness = 255;
                    a.Happiness = Math.Min(255, a.Happiness + ctx.HappinessGain);
                    a.Friendship = Math.Min(1000, a.Friendship + ctx.FriendshipGain);
                    TryProduce(a, ctx.DaySeed);
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

    private static bool IsExcluded(LedgerAnimal a, HashSet<long>? excludeIds)
        => excludeIds != null && excludeIds.Contains(a.Id);

    public void AddProduce(string qualifiedId)
    {
        // 台账动物默认产普通(0 星);星级产品只来自实体动物(拦截器按星级分桶)
        AddProduceWithQuality(qualifiedId, 0);
    }

    /// <summary>按星级入桶:key = "物品ID|星级"。星级产品(实体动物)与普通(台账)分开存。</summary>
    public void AddProduceWithQuality(string qualifiedId, int quality)
    {
        ProduceCount++;
        string key = qualifiedId + "|" + quality;
        ProduceStacks.TryGetValue(key, out int n);
        ProduceStacks[key] = n + 1;
    }

    /// <summary>生产判定 + 星级判定(照抄原版 FarmAnimal.dayUpdate 的公式,FarmAnimal.cs:1016-1053):
    /// 1) 生产:结算前的 DaysSinceProduce 达阈值(产出的那天算新周期第 1 天)。
    /// 2) 星级:num4 = 好感/1000 - (1 - 心情/225);好感满+心情满 = 0.65。
    ///    按 num4 顺序掷:铱(>=0.95 且 rnd < num4/2) → 金(rnd < num4/2) → 银(rnd < num4) → 普通。
    /// 3) 随机用"动物ID/2 + 天种子"做种子 → 同一天所有端判定一致(联机同步,确定可复现);
    ///    DaySeed=0(旧调用)时退回纯 ID 种子(每只动物结果稳定,测试友好)。</summary>
    private void TryProduce(LedgerAnimal a, int daySeed = 0)
    {
        var info = FarmAnimalCatalog.Get(a.Room);
        if (a.Happiness < 70 || a.DaysSinceProduce < info.DaysToProduce || info.ProduceId == null) return;

        a.DaysSinceProduce = 0;
        a.ProduceCount++;
        AddProduceWithQuality(info.ProduceId, RollQuality(a, daySeed));
    }

    /// <summary>原版星级公式(确定性):同一天同一只动物结算多次,结果一致(双结算防刷星/防测试抖动)。
    /// 用纯整数混合做种子(不用 HashCode.Combine —— 它在 .NET 里每进程随机化,跨进程不可复现)。</summary>
    private static int RollQuality(LedgerAnimal a, int daySeed)
    {
        // 原版:num4 = 好感/1000 - (1 - 心情/225);好感满 1000 + 心情满 255 → 0.65
        double num4 = (double)a.Friendship / 1000.0 - (1.0 - (double)a.Happiness / 225.0);
        unchecked
        {
            int seed = (int)(a.Id * 1000003L) ^ (int)((long)daySeed * 2654435761L);   // 纯整数混合:跨进程/跨端可复现
            var rnd = new Random(seed);
            double roll = rnd.NextDouble();
            if (num4 >= 0.95 && roll < num4 / 2.0) return 4;          // 铱
            if (roll < num4 / 2.0) return 2;                          // 金
            if (roll < num4) return 1;                                // 银
        }
        return 0;                                                     // 普通
    }
}
