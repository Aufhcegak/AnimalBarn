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

// --- UpgradeSystem 测试 ---
Check("unlock chicken lvl1", UpgradeSystem.IsUnlocked(RoomType.Chicken, 1));
Check("no pig lvl1", !UpgradeSystem.IsUnlocked(RoomType.Pig, 1));
Check("pig lvl2", UpgradeSystem.IsUnlocked(RoomType.Pig, 2));
Check("no cow lvl2", !UpgradeSystem.IsUnlocked(RoomType.Cow, 2));
Check("cow lvl3", UpgradeSystem.IsUnlocked(RoomType.Cow, 3));
Check("duck lvl4", UpgradeSystem.IsUnlocked(RoomType.Duck, 4));
Check("rabbit lvl4", UpgradeSystem.IsUnlocked(RoomType.Rabbit, 4));
Check("no duck lvl3", !UpgradeSystem.IsUnlocked(RoomType.Duck, 3));
Check("dino lvl5", UpgradeSystem.IsUnlocked(RoomType.Dinosaur, 5));
Check("ostrich lvl5", UpgradeSystem.IsUnlocked(RoomType.Ostrich, 5));
Check("sheep lvl5", UpgradeSystem.IsUnlocked(RoomType.Sheep, 5));
Check("goat lvl5", UpgradeSystem.IsUnlocked(RoomType.Goat, 5));
Check("no dino lvl4", !UpgradeSystem.IsUnlocked(RoomType.Dinosaur, 4));
Check("chicken cap 600", UpgradeSystem.CapacityAt(RoomType.Chicken, 5) == 600);
Check("chicken cap 100", UpgradeSystem.CapacityAt(RoomType.Chicken, 0) == 100);
Check("pig cap 90", UpgradeSystem.CapacityAt(RoomType.Pig, 5) == 90);
Check("rabbit cap 300", UpgradeSystem.CapacityAt(RoomType.Rabbit, 5) == 300);
Check("dino cap 60", UpgradeSystem.CapacityAt(RoomType.Dinosaur, 5) == 60);
Check("ostrich cap 60", UpgradeSystem.CapacityAt(RoomType.Ostrich, 5) == 60);
Check("ostrich cap clamp", UpgradeSystem.CapacityAt(RoomType.Ostrich, 9) == 60);
Check("goat cap 90", UpgradeSystem.CapacityAt(RoomType.Goat, 5) == 90);
Check("friendship lvl1", UpgradeSystem.FriendshipGainAt(1) == 6);
Check("friendship lvl3", UpgradeSystem.FriendshipGainAt(3) == 9);

// --- RoomDefinitions:羊与山羊共用羊场(9 动物 8 房),买羊/判解锁/容量都要落到 Goat 房 ---
Check("RoomFor sheep -> goat house", RoomDefinitions.RoomFor(RoomType.Sheep) == RoomType.Goat);
Check("RoomFor goat -> goat house", RoomDefinitions.RoomFor(RoomType.Goat) == RoomType.Goat);
Check("RoomFor chicken 1:1", RoomDefinitions.RoomFor(RoomType.Chicken) == RoomType.Chicken);
Check("RoomFor cow 1:1", RoomDefinitions.RoomFor(RoomType.Cow) == RoomType.Cow);
Check("Get(sheep house) via RoomFor no-throw", RoomDefinitions.Get(RoomDefinitions.RoomFor(RoomType.Sheep)).DisplayName == "羊场");
Check("RoomDefinitions has 8 rooms", RoomDefinitions.All.Length == 8);
Check("catalog has 9 animals", FarmAnimalCatalog.All.Length == 9);
Check("friendship lvl5", UpgradeSystem.FriendshipGainAt(5) == 12);
Check("friendship clamp low", UpgradeSystem.FriendshipGainAt(0) == 6);
Check("friendship clamp high", UpgradeSystem.FriendshipGainAt(99) == 12);

// --- SaveSerializer 往返(纯 JsonSerializer,不经 Building) ---
// 选项须与 SaveSerializer 一致(IncludeFields:本 mod 数据类均用公共字段)。
var serOpts = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
var sd = new BarnSaveData
{
    OverallLevel = 3,
    HayStock = 500,
    ProduceCount = 10,
};
sd.GlobalProduceStacks["(O)176"] = 10;
var room = sd.GetRoom(RoomType.Chicken);
room.UpgradeLevel = 2;
room.ProduceCount = 10;
room.ProduceStacks["(O)176"] = 5;
room.Animals.Add(new LedgerAnimal { Room = RoomType.Chicken, TypeKey = "White Chicken", AgeDays = 5, Friendship = 300, Happiness = 200, DaysSinceProduce = 1, ProduceCount = 2, OwnerId = 99 });
var json = System.Text.Json.JsonSerializer.Serialize(sd, serOpts);
var back = System.Text.Json.JsonSerializer.Deserialize<BarnSaveData>(json, serOpts)!;
Check("serialize level", back.OverallLevel == 3);
Check("serialize hay", back.HayStock == 500);
Check("serialize produce", back.ProduceCount == 10);
Check("serialize global stack", back.GlobalProduceStacks["(O)176"] == 10);
Check("serialize room exists", back.HasRoom(RoomType.Chicken));
Check("serialize room level", back.GetRoom(RoomType.Chicken).UpgradeLevel == 2);
Check("serialize room stack", back.GetRoom(RoomType.Chicken).ProduceStacks["(O)176"] == 5);
Check("serialize animal", back.GetRoom(RoomType.Chicken).Animals.Count == 1);
Check("serialize animal fields", back.GetRoom(RoomType.Chicken).Animals[0].Friendship == 300 && back.GetRoom(RoomType.Chicken).Animals[0].TypeKey == "White Chicken");
Check("serialize animal room enum", back.GetRoom(RoomType.Chicken).Animals[0].Room == RoomType.Chicken);

// --- AnimalLedger.FromRoom/SaveTo 往返 ---
var rs = new BarnSaveData.RoomSaveData { UpgradeLevel = 2 };
rs.Animals.Add(new LedgerAnimal { Room = RoomType.Chicken, TypeKey = "White Chicken", AgeDays = 5, Friendship = 300, Happiness = 200 });
rs.ProduceStacks["(O)176"] = 3;
var lg = AnimalLedger.FromRoom(rs);
Check("fromroom animals", lg.Animals.Count == 1);
Check("fromroom animal fields", lg.Animals[0].Friendship == 300 && lg.Animals[0].AgeDays == 5);
Check("fromroom stacks", lg.ProduceStacks["(O)176"] == 3);
lg.ProduceStacks["(O)176"] = 5;
lg.ProduceCount = 7;
lg.SaveTo(rs);
Check("saveto stacks", rs.ProduceStacks["(O)176"] == 5);
Check("saveto count", rs.ProduceCount == 7);
Check("saveto animals", rs.Animals.Count == 1 && rs.Animals[0].Friendship == 300);

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURES");
return failures == 0 ? 0 : 1;
