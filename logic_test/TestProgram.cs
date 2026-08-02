using AnimalBarn;

int failures = 0;
void Check(string name, bool cond)
{
    Console.WriteLine((cond ? "PASS " : "FAIL ") + name);
    if (!cond) failures++;
}

// --- AnimalLedger 测试 ---
var ledger = new AnimalLedger { Capacity = 100 };

// 1. 容量上限
for (int i = 0; i < 100; i++)
    Check($"add {i}", ledger.TryAdd(new LedgerAnimal { Room = RoomType.Chicken, TypeKey = "White Chicken" }));
Check("full at 100", ledger.IsFull);
Check("101st rejected", !ledger.TryAdd(new LedgerAnimal { Room = RoomType.Chicken }));

// 2. 可见数量封顶 30
Check("visible capped at 30", ledger.GetVisible().Count == 30);

// 3. 结算:成年动物产产品,幼崽只成长
var adult = new LedgerAnimal { Room = RoomType.Chicken, AgeDays = 10, Happiness = 200, Fullness = 0, DaysSinceProduce = 1 };
var baby = new LedgerAnimal { Room = RoomType.Chicken, AgeDays = 1 };
var l2 = new AnimalLedger { Capacity = 10 };
l2.TryAdd(adult);
l2.TryAdd(baby);
var hay = l2.SettleDay(new SettleContext(6, 20), hayAvailable: 100);
Check("adult produces", adult.ProduceCount == 1);
Check("adult got hay", adult.Fullness == 255);
Check("adult friendship grew", adult.Friendship == 6);
Check("baby aged", baby.AgeDays == 2);
Check("baby no produce", baby.ProduceCount == 0);
Check("hay consumed 1", hay.HayConsumed == 1);

// 4. 没干草:成年掉好感不产
var hungry = new LedgerAnimal { Room = RoomType.Chicken, AgeDays = 10, Happiness = 200, Fullness = 0, DaysSinceProduce = 1, Friendship = 100 };
var l3 = new AnimalLedger { Capacity = 10 };
l3.TryAdd(hungry);
var hay2 = l3.SettleDay(new SettleContext(6, 20), hayAvailable: 0);
Check("hungry no produce", hungry.ProduceCount == 0);
Check("hungry loses friendship", hungry.Friendship == 80);
Check("hungry loses happiness", hungry.Happiness == 100);
Check("hay consumed 0", hay2.HayConsumed == 0);
Check("hungry count 1", hay2.HungryAdults == 1);

// 5. 产品入库
Check("produce buffered", l2.ProduceStacks.ContainsKey("(O)176") && l2.ProduceStacks["(O)176"] == 1);

// 6. 生产间隔:鸡每天产,鸭 2 天
var duck = new LedgerAnimal { Room = RoomType.Duck, AgeDays = 10, Happiness = 200, Fullness = 0, DaysSinceProduce = 1 };
var l4 = new AnimalLedger { Capacity = 10 };
l4.TryAdd(duck);
l4.SettleDay(new SettleContext(6, 20), 100);
Check("duck not produce day1", duck.ProduceCount == 0);
l4.SettleDay(new SettleContext(6, 20), 100);
Check("duck produces day2", duck.ProduceCount == 1);

// 7. 已有饱食度的成年不耗干草
var full = new LedgerAnimal { Room = RoomType.Chicken, AgeDays = 10, Happiness = 200, Fullness = 255, DaysSinceProduce = 0, Friendship = 0 };
var l5 = new AnimalLedger { Capacity = 10 };
l5.TryAdd(full);
var hayFull = l5.SettleDay(new SettleContext(6, 20), hayAvailable: 0);
Check("full adult no hay needed", hayFull.HayConsumed == 0 && hayFull.HungryAdults == 0);
Check("full adult keeps fullness", full.Fullness == 255);
Check("full adult keeps full after 10 days", l5.SettleDay(new SettleContext(6, 20), 0).HayConsumed == 0);

// 8. 干草不足:按台账顺序喂,先到的吃饱,后到的挨饿
var a1 = new LedgerAnimal { Room = RoomType.Chicken, AgeDays = 10, Happiness = 200, Fullness = 0, DaysSinceProduce = 1 };
var a2 = new LedgerAnimal { Room = RoomType.Chicken, AgeDays = 10, Happiness = 200, Fullness = 0, DaysSinceProduce = 1, Friendship = 100 };
var l6 = new AnimalLedger { Capacity = 10 };
l6.TryAdd(a1);
l6.TryAdd(a2);
var hayShort = l6.SettleDay(new SettleContext(6, 20), hayAvailable: 1);
Check("short hay: 1 consumed", hayShort.HayConsumed == 1);
Check("short hay: 1 hungry left", hayShort.HungryAdults == 1);
Check("short hay: first fed", a1.Fullness == 255 && a1.Friendship == 6);
Check("short hay: second hungry", a2.Fullness == 0 && a2.Friendship == 80 && a2.Happiness == 100);
Check("short hay: only fed one produces", l6.ProduceCount == 1 && a1.ProduceCount == 1 && a2.ProduceCount == 0);

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURES");
return failures == 0 ? 0 : 1;
