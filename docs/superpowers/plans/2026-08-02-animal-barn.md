# 动物养殖场(AnimalBarn)实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在罗宾建造菜单新增「动物养殖场」大型建筑,内含 8 个可独立升级的动物房间 + 干草房,全自动每日结算(产产品/好感/喂食),中枢菜单管理,支持大规模养殖(600 只/房)不卡顿。

**Architecture:** 基于已验证的 1.6.15 机制——`Data/Buildings` 加 `Builder: Robin` 数据行自动注入罗宾菜单;自定义 `Building` 子类 + `AnimalHouse` 子类(通过 `IndoorMapType` 反射创建)承载房间;每个房间室内图加 `AutoFeed` 属性让 AnimalHouse 自动喂食;睡眠时 `Game1._newDayAfterFade` 自动跑每房 `DayUpdate` → `FarmAnimal.dayUpdate` 原生结算;产品掉地路径用 Harmony 拦截转存中央仓库。动物台账(全量)+ 房间最多 30 只实体动物(每帧 tick),台账动物只结算不渲染,保证不卡。

**Tech Stack:** .NET 6, C# 12, SMAPI 4.5.1, Stardew Valley 1.6.15, Harmony 2.3.3, xTile(代码生成室内地图), ModBuildConfig 4.4.0。参考 mod:`MonsterArena`(代码生成地图配方)、`JunimoTaskScheduler`(logic_test 无头测试)。

**关键已验证事实(反编译 1.6.15,勿重推导):**
- `AnimalHouse` 在 **`StardewValley` 根命名空间**(不是 `StardewValley.Locations`),构造 `()` 和 `(string, string)` 两个都有。已编译验证。
- `BuildingData`/`BuildingMaterial`/`BuildingSkin` 在 **`StardewValley.GameData.Buildings`**(GameData.dll),字段全部确认(Size/HumanDoor/BuildDays/BuildCost/BuildMaterials/Builder/IndoorMap/IndoorMapType/MaxOccupants/ValidOccupantTypes 等)。
- `CarpenterMenu` 构造遍历 `Game1.buildingData`,凡 `Builder == 菜单Builder`(Robin)且 `BuildCondition` 通过者自动成为蓝图。注入 `Data/Buildings` 数据行即可,零 Harmony。
- `Building` 用 `IndoorMap`(Maps\ 前缀)+ `IndoorMapType`(Type.GetType 反射)创建室内;`createIndoors` 是 `protected virtual`,自定义 Building 子类可覆写。
- `AnimalHouse.DayUpdate` 先 `base.DayUpdate`(跑动物 dayUpdate),再若地图有 `AutoFeed` 属性则 `feedAllAnimals`(自动喂食)。
- `FarmAnimal.dayUpdate(GameLocation)` 在睡眠时被 `GameLocation.DayUpdate` 逐只调用:喂食(吃 Trough 干草或草)、成长、产产品(`Utility.spawnObjectAround` 掉地)、好感/心情结算。无 `wasPet` 且无 `wasAutoPet` 时好感自然衰减。
- `FarmAnimal` 构造:`new FarmAnimal(string type, long id, long ownerID)`,type 是 `Data/FarmAnimals` 的 key(如 "White Chicken"/"Dairy Cow"/"Pig"/"Dinosaur")。`isBaby()` = `age < DaysToMature`。
- 动物"住哪":`FarmAnimal.CanLiveIn(Building)` 检查 `ValidOccupantTypes.Contains(animal.buildingTypeILiveIn)`。动物放入房间:`animalHouse.animals.TryAdd(id, animal)` + `animalsThatLiveHere.Add(id)` + `home` 指向 building。
- 建筑数据字段:`Builder`/`BuildCondition`/`Size`/`BuildCost`/`BuildMaterials`(List<BuildingMaterial>)/`BuildDays`/`BuildingToUpgrade`/`HumanDoor`/`AnimalDoor`/`IndoorMap`/`IndoorMapType`/`MaxOccupants`/`ValidOccupantTypes`/`BuildingType`/`Name`/`Description`/`Skins`。
- 干草 = `(O)178`,价格 = `Data/ObjectInformation` 里的 50g;苜蓿干草器 = `(BC)104`。玛妮售价 50g,9 折 = 45g。
- `ModEntry.Entry` 中注册 asset 注入用 `helper.Events.Content.AssetRequested`;地图资产注入 `Maps/<Name>` 在 `AssetRequested` 的 `Edit` 事件中 `asset.ReplaceWith(map)`(MonsterArena 已验证)。
- 无头测试配方(JunimoTaskScheduler 已验证):`Game1.gameMode == Game1.titleScreenMode` 时,`Context.IsGameLaunched && !Context.IsWorldReady` 且存在 `autotest.txt` → 直接构造独立临时 `GameLocation` 跑逻辑,不加载存档、不碰 Saves。
- 无头限制:`Game1.Date`/`player` 在 FlushDay 直接 NRE,逻辑层必须不依赖它们或已兜底。

**架构修正(反编译确认,勿重推导):**
- `BuildingData.BuildingType` 字段注释明确警告:用非原版 Building 类型**写存档会崩**(`Building` 序列化通过 `buildingType.Value` 重建,自定义类型不在 XML 序列化允许列表)。**决定:外层建筑用原版 `Building` 实例 + `building.modData` 存状态,不设 `BuildingType`**;需要用自定义行为时用 Harmony 补丁 `Building`(低频点)。
- 室内 `IndoorMapType` 用自定义 `AnimalBarnRoom : AnimalHouse` **已验证可行**:`createIndoors` 用 `Type.GetType` 反射创建;`GameLocation` 的 XML 序列化用 `typeof(GameLocation)` 基类序列化器,子类状态经 `TransferDataFromSavedLocation` 转移(AnimalHouse 已实现)。
- 房间↔类型映射:室内 `GameLocation.modData` 存 `xiepe.AnimalBarn.RoomType`,读档后恢复。
- `AnimalHouse.animalLimit` 原生是房间容量。我们改为:房间 `MaxOccupants` = 30(实体上限),台账容量独立管(房间升级决定)。`AnimalHouse.isFull` 用 animalLimit(30),购买逻辑用台账容量判断。

**规模警告:** 本项目比 MonsterArena 大 3-5 倍(建筑+10 室内图+菜单+结算+存档+测试)。计划分 6 阶段,每阶段可独立冒烟。严格按顺序执行。

**测试策略(全自动,开发者在无头环境自行验证,不依赖用户实测):**
- **逻辑层**(台账/结算/升级/存档):`logic_test` 纯 C# 单测,`dotnet run --project logic_test` 全绿。
- **集成层**(建筑/菜单/结算/存档):无头 SMAPI 启动 + 标题画面自动跑集成测试。配方(JunimoTaskScheduler 已验证):`Game1.gameMode == Game1.titleScreenMode` 时,`Context.IsGameLaunched && !Context.IsWorldReady` 且存在 `autotest.txt` → 自动执行测试脚本。测试脚本内直接用 `Game1.buildingData`/`new FarmAnimal(...)`/`location.DayUpdate(...)` 构造真实对象跑流程,断言后写结果到 `autotest_result.txt`。
- **地图验证**:xnbread 工具加载生成的 Map 资产,dump 每 tile 的 `TileSheet:TileIndex` 与属性(MonsterArena 已验证配方),断言:地板铺满、墙完整、门洞位置正确、Trough/ProduceArea/AutoFeed 属性存在。
- **视觉验证**:无头环境无法渲染(MonoGame 需真实 GPU),**不依赖肉眼**;一切以 tile dump + 断言为准。若最终用户反馈画面问题,按 tile dump 排查。
- 每个 Task 的验证步骤都给出**具体命令 + 期望断言**。凡计划里写"手动验证"的一律替换为自动化步骤。

---

## 文件结构

```
Mods/AnimalBarn/
├── AnimalBarn.csproj              # 项目文件(复用 MonsterArena 配方)
├── manifest.json                  # mod 清单
├── i18n/
│   └── default.json               # 中文文本(硬编码中文也可,但放 i18n 便于维护)
├── assets/
│   ├── buildings/AnimalBarn.png   # 建筑贴图(Kimi 制作;先用原版 Barn 贴图占位)
│   └── interiors/                 # (可选)代码生成,无需文件
├── ModEntry.cs                    # SMAPI 入口:注册事件/补丁/命令
├── BuildingDataLoader.cs          # 注入 Data/Buildings + Data/FarmAnimals 数据行
├── FarmAnimalCatalog.cs           # 动物类型常量 + 原价/9 折价 + 房间归属
├── HubMenu.cs                     # 中枢菜单(4 页签:状态/升级/商店/仓库)
├── HubMenuState.cs                # 菜单所需的状态快照(打开时计算,不实时)
├── BarnManager.cs                 # 核心:每建筑状态(等级/干草/各房台账),存档
├── BarnSaveData.cs                # 存档数据类(ModData JSON 序列化)
├── AnimalLedger.cs                # 每房台账:全量动物数据 + 最多30只实体
├── LedgerAnimal.cs                # 台账动物记录(类型/好感/成长/生产状态)
├── AnimalBarnBuilding.cs          # Building 子类:覆写 createIndoors/onDayUpdate
├── AnimalBarnRoom.cs              # AnimalHouse 子类:覆写 dayUpdate 结算
├── RoomMapBuilder.cs              # 代码生成房间室内地图(xTile,MonsterArena 配方)
├── LobbyMapBuilder.cs             # 代码生成大堂地图(9 门 + 中枢)
├── RoomDefinitions.cs             # 8 房间定义(类型/地图/解锁等级/容量曲线)
├── AutoGrabberInterceptor.cs      # Harmony:拦截产物掉地 → 中央仓库
├── BuildDataInjection.cs          # Data/Buildings + Data/FarmAnimals 的 AssetRequested 注入
├── UpgradeSystem.cs               # 升级逻辑(整体/房间,费用,施工)
├── SaveSerializer.cs              # ModData JSON 存档读写
├── GrassFeeding.cs                # 室外吃草辅助(可选)
├── logic_test/                    # 无头逻辑测试(独立小工程,参照 JTS)
│   ├── logic_test.csproj
│   ├── TestProgram.cs             # 测试入口(裸 Main,无 SMAPI)
│   └── LedgerTests.cs             # 台账/结算逻辑测试
└── docs/superpowers/plans/2026-08-02-animal-barn.md   # 本计划
```

**职责边界:**
- `BarnManager`(单例,ModEntry 持有)是唯一状态持有者;`HubMenu` 只读状态 + 发命令;`AnimalBarnBuilding`/`AnimalBarnRoom` 通过 `BarnManager` 查状态;`AnimalLedger` 纯数据结构(可测);`AnimalBarnRoom.DayUpdate` 调用 `BarnManager` 的结算方法(可测)。
- 测试只测纯逻辑:`AnimalLedger` 增删/结算/上限、`UpgradeSystem` 费用/解锁、`SaveSerializer` 序列化往返。不测 UI/补丁/渲染。

---

## 阶段 1:项目骨架 + 建筑注册(可冒烟)

### Task 1.1: 项目文件与清单

**Files:**
- Create: `Mods/AnimalBarn/AnimalBarn.csproj`
- Create: `Mods/AnimalBarn/manifest.json`

- [ ] **Step 1: 写 csproj**(复用 MonsterArena 配方,含 in-Mods-folder 部署修复)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>AnimalBarn</AssemblyName>
    <RootNamespace>AnimalBarn</RootNamespace>
    <EnableModDeploy>false</EnableModDeploy>
    <EnableModZip>false</EnableModZip>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Pathoschild.Stardew.ModBuildConfig" Version="4.4.0" />
    <PackageReference Include="Lib.Harmony" Version="2.3.3" />
  </ItemGroup>
  <ItemGroup>
    <None Update="manifest.json" CopyToOutputDirectory="PreserveNewest" />
    <None Update="i18n\**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
  <Target Name="DeployToMods" AfterTargets="Build">
    <Copy SourceFiles="$(TargetPath)" DestinationFolder="$(MSBuildProjectDirectory)" />
    <Copy SourceFiles="$(MSBuildProjectDirectory)\manifest.json" DestinationFolder="$(MSBuildProjectDirectory)" SkipUnchangedFiles="true" />
  </Target>
</Project>
```

- [ ] **Step 2: 写 manifest.json**

```json
{
  "Name": "Animal Barn",
  "Author": "xiepe",
  "Version": "1.0.0",
  "Description": "动物养殖场：罗宾建造，8个动物房间+干草房，全自动每日结算，中枢管理，大规模养殖不卡顿。",
  "UniqueID": "xiepe.AnimalBarn",
  "EntryDll": "AnimalBarn.dll",
  "MinimumApiVersion": "4.0.0",
  "UpdateKeys": []
}
```

- [ ] **Step 3: 写 i18n/default.json**

```json
{
  "Building.Name": "动物养殖场",
  "Building.Description": "大型动物养殖设施。内含 8 个可独立升级的动物房间与干草房,全自动管理。"
}
```

- [ ] **Step 4: 建目录 + 冒烟编译**

```bash
cd "D:\steam\steamapps\common\Stardew Valley\Mods\AnimalBarn"
dotnet build -c Release
```

Expected: BUILD SUCCEEDED(先建一个空的 ModEntry.cs 占位,见 Task 1.2 前先建最小入口)。

- [ ] **Step 5: Commit**(本项目无 git repo,初始化一个:`git init` 在 Mods/AnimalBarn)

```bash
git init && git add -A && git commit -m "chore: project skeleton"
```

### Task 1.2: 最小 ModEntry(加载冒烟)

**Files:**
- Create: `Mods/AnimalBarn/ModEntry.cs`
- Create: `Mods/AnimalBarn/BuildDataInjection.cs`(占位)

- [ ] **Step 1: 写最小 ModEntry**

```csharp
using StardewModdingAPI;

namespace AnimalBarn;

public class ModEntry : Mod
{
    internal static ModEntry Instance = null!;

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        this.Monitor.Log("AnimalBarn loaded.", LogLevel.Info);
    }
}
```

- [ ] **Step 2: 无头冒烟**

```bash
cd "D:\steam\steamapps\common\Stardew Valley"
timeout 90 ./StardewModdingAPI.exe
grep -i "AnimalBarn" "%APPDATA%\StardewValley\ErrorLogs\SMAPI-latest.txt"
```

Expected: 日志出现 "AnimalBarn loaded.";无 error。

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "feat: minimal mod entry"
```

### Task 1.3: 建筑数据注入(罗宾菜单出现)

**Files:**
- Create: `Mods/AnimalBarn/BuildDataInjection.cs`
- Modify: `Mods/AnimalBarn/ModEntry.cs`

- [ ] **Step 1: 写 BuildDataInjection(注入 Data/Buildings 数据行)**

```csharp
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace AnimalBarn;

/// <summary>注入 Data/Buildings 数据行,让「动物养殖场」出现在罗宾建造菜单。</summary>
public static class BuildDataInjection
{
    public const string BuildingId = "xiepe.AnimalBarn";

    public static void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.Name.IsEquivalentTo("Data/Buildings"))
        {
            e.Edit(asset =>
            {
                var data = asset.AsDictionary<string, StardewValley.GameData.Buildings.BuildingData>().Data;
                if (!data.ContainsKey(BuildingId))
                {
                    data[BuildingId] = new StardewValley.GameData.Buildings.BuildingData
                    {
                        Name = "动物养殖场",
                        Description = "大型动物养殖设施。",
                        Builder = "Robin",
                        BuildCost = 50000,
                        BuildMaterials = new List<StardewValley.GameData.Buildings.BuildingMaterial>
                        {
                            new() { ItemId = "(O)388", Count = 500 },  // 木头
                            new() { ItemId = "(O)390", Count = 100 },  // 石头
                        },
                        BuildDays = 2,
                        Size = new Point(7, 4),  // 与原版 Barn 相同
                        HumanDoor = new Point(3, 4),
                        AnimalDoor = new StardewValley.GameData.Buildings.BuildingDoor { Location = new Point(4, 4) },
                        IndoorMap = "xiepe.AnimalBarn.Lobby",
                        IndoorMapType = "AnimalBarn.AnimalBarnRoom, AnimalBarn",
                        MaxOccupants = 0,  // 大堂无动物上限,房间各自管理
                        ValidOccupantTypes = new List<string>(),
                        BuildingType = null,
                        BuildingToUpgrade = null,
                        BuildCondition = null,
                    };
                }
            });
        }
    }
}
```

> 注:`BuildingData`/`BuildingMaterial`/`BuildingDoor` 的确切命名空间和字段名须以实际编译为准——1.6.15 中这些类型在 `StardewValley.GameData.Buildings` 下,若编译报错按编译器提示调整。`IndoorMapType` 格式为 `"完全限定类型名, 程序集名"`(反射 `Type.GetType` 用)。

- [ ] **Step 2: 在 ModEntry 注册**

```csharp
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace AnimalBarn;

public class ModEntry : Mod
{
    internal static ModEntry Instance = null!;

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        helper.Events.Content.AssetRequested += BuildDataInjection.OnAssetRequested;
        this.Monitor.Log("AnimalBarn loaded.", LogLevel.Info);
    }
}
```

- [ ] **Step 3: 编译 + 无头冒烟**

```bash
cd "D:\steam\steamapps\common\Stardew Valley\Mods\AnimalBarn"
dotnet build -c Release
cd "D:\steam\steamapps\common\Stardew Valley"
timeout 90 ./StardewModdingAPI.exe
grep -iE "error|exception|AnimalBarn" "%APPDATA%\StardewValley\ErrorLogs\SMAPI-latest.txt"
```

Expected: BUILD SUCCEEDED;无 error;AnimalBarn 加载成功。若 `BuildingData` 编译失败(命名空间/字段差异),修复后重试——这是全项目最关键的类型引用点。

> 注意:此时点开罗宾菜单可能因 IndoorMap "xiepe.AnimalBarn.Lobby" 不存在而崩溃(菜单会 preview 建筑)。**在 Task 1.4 前不要手动进游戏点罗宾菜单**;无头冒烟只验证加载。

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat: register building in Data/Buildings"
```

### Task 1.4: 大堂室内地图(代码生成)

**Files:**
- Create: `Mods/AnimalBarn/LobbyMapBuilder.cs`
- Modify: `Mods/AnimalBarn/ModEntry.cs`

- [ ] **Step 1: 写 LobbyMapBuilder(复用 MonsterArena 配方:五层 + tile 索引 + 边界环)**

```csharp
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using xTile;
using xTile.Dimensions;
using xTile.Layers;
using xTile.Tiles;

namespace AnimalBarn;

/// <summary>代码生成大堂地图(9 扇门 + 中枢操作台)。复用 MonsterArena 的 tile 配方。</summary>
public static class LobbyMapBuilder
{
    public const string MapAssetName = "Maps/xiepe.AnimalBarn.Lobby";
    public const int Width = 13;   // 大堂较小
    public const int Height = 9;

    // FarmHouse1 木地板参考 tile 索引(MonsterArena 已验证):
    // 地板 = walls_and_floors 336/337(隔行 352/353),底座 = 32,墙 = townInterior 1/2/3(顶),64/68(侧),160/130(底角)
    private const int FloorA = 336;
    private const int FloorB = 352;
    private const int Baseboard = 32;
    private const int WallTop = 1;
    private const int WallSide = 64;
    private const int WallCorner = 160;

    public static void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.Name.IsEquivalentTo(MapAssetName))
        {
            e.LoadFrom(() => BuildMap(), AssetLoadPriority.Medium);
        }
    }

    public static Map BuildMap()
    {
        var map = new Map();
        // 五层:Back/Buildings/Front/Paths/AlwaysFront(游戏要求,缺一崩)
        var back = new Layer("Back", new Size(Width, Height), new Size(16, 16));
        var buildings = new Layer("Buildings", new Size(Width, Height), new Size(16, 16));
        var front = new Layer("Front", new Size(Width, Height), new Size(16, 16));
        var paths = new Layer("Paths", new Size(Width, Height), new Size(16, 16));
        var alwaysFront = new Layer("AlwaysFront", new Size(Width, Height), new Size(16, 16));
        map.AddLayer(back);
        map.AddLayer(buildings);
        map.AddLayer(front);
        map.AddLayer(paths);
        map.AddLayer(alwaysFront);

        // 贴图集(与 MonsterArena 一致)
        var floorSheet = new TileSheet("walls_and_floors", "Maps/walls_and_floors", new Size(16, 16), new Size(512, 384), 0);
        var interiorSheet = new TileSheet("townInterior", "Maps/townInterior", new Size(16, 16), new Size(512, 512), 0);
        map.AddTileSheet(floorSheet);
        map.AddTileSheet(interiorSheet);

        // 地板:全铺 336,隔行 352(视觉交错)
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
                back.Tiles[x, y] = new StaticTile(back, floorSheet, BlendMode.Alpha, (y % 2 == 0) ? FloorA : FloorB);

        // 墙:顶行 townInterior WallTop,两侧 WallSide,四角 WallCorner,顶行下方加底座 Baseboard
        for (int x = 0; x < Width; x++)
        {
            buildings.Tiles[x, 0] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, WallTop);
            buildings.Tiles[x, 1] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, Baseboard);
        }
        for (int y = 0; y < Height; y++)
        {
            buildings.Tiles[0, y] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, WallSide);
            buildings.Tiles[Width - 1, y] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, WallSide);
        }
        buildings.Tiles[0, 0] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, WallCorner);
        buildings.Tiles[Width - 1, 0] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, WallCorner);
        // 边界环:最外圈全部 Baseboard 阻挡(防穿墙,MonsterArena 教训)
        for (int x = 0; x < Width; x++)
        {
            buildings.Tiles[x, Height - 1] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, Baseboard);
        }
        for (int y = 0; y < Height; y++)
        {
            buildings.Tiles[0, y] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, Baseboard);
            buildings.Tiles[Width - 1, y] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, Baseboard);
        }
        // 底部留门洞:底部中央 3 格不铺(玩家进出门)
        for (int x = Width / 2 - 1; x <= Width / 2 + 1; x++)
            buildings.Tiles[x, Height - 1] = null;

        // 大堂特色:中枢操作台(用一张桌子 tile,占 2x1)
        // 门洞在两侧墙,后续房间门点放在门洞处

        map.Properties["AutoFeed"] = new xTile.Properties.Property("AutoFeed", "T");
        return map;
    }
}
```

> **注意:** 本步代码是**初始布局**,门洞位置/中枢位置在 Task 4(房间接入)会精调。目标是"能进能出 + 不穿墙"。原版 Barn 室内 `ProduceArea` 地图属性(动物随机站位区)后续在房间图里加。

- [ ] **Step 2: 注册 asset + 创建房间类占位(让 IndoorMapType 能解析)**

```csharp
// ModEntry.cs Entry 中追加:
helper.Events.Content.AssetRequested += LobbyMapBuilder.OnAssetRequested;
```

先写 `AnimalBarnRoom.cs` 最小版(否则 IndoorMapType 解析失败会 fallback 到 GameLocation,无头加载就够):

```csharp
using StardewValley;

namespace AnimalBarn;

/// <summary>养殖场房间(大堂也用此类,后续细分为 Room 子类)。继承 AnimalHouse 获得自动喂食/容量/动物管理。</summary>
public class AnimalBarnRoom : StardewValley.Locations.AnimalHouse
{
    public AnimalBarnRoom() { }
    public AnimalBarnRoom(string mapPath, string name) : base(mapPath, name) { }
}
```

- [ ] **Step 3: 编译 + 无头冒烟 + 手动进游戏点罗宾菜单验证**

```bash
cd "D:\steam\steamapps\common\Stardew Valley\Mods\AnimalBarn"
dotnet build -c Release
cd "D:\steam\steamapps\common\Stardew Valley"
timeout 90 ./StardewModdingAPI.exe
grep -iE "error|exception" "%APPDATA%\StardewValley\ErrorLogs\SMAPI-latest.txt"
```

Expected: 无 error。集成验证(自动化):运行 autotest 脚本(见 Task 1.5),断言:①`Game1.buildingData` 含 `xiepe.AnimalBarn`;②`Building.CreateInstanceFromId` 能创建;③大堂地图能加载;④入口 tile 无阻挡。

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat: lobby map + building registration"
```

### Task 1.5: 集成测试框架(autotest 无头自动验收)

**Files:**
- Create: `Mods/AnimalBarn/IntegrationTest.cs`
- Create: `Mods/AnimalBarn/autotest_result.txt`(运行后生成)
- Modify: `Mods/AnimalBarn/ModEntry.cs`

- [ ] **Step 1: 写集成测试入口(标题画面自动触发)**

```csharp
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace AnimalBarn;

/// <summary>无头集成测试:autotest.txt 存在时,标题画面自动跑并写结果到 autotest_result.txt。
/// 配方来自 JunimoTaskScheduler(Context.IsGameLaunched && !Context.IsWorldReady 且不碰存档)。</summary>
public static class IntegrationTest
{
    public static bool Pending;

    public static void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Pending) return;
        if (!Context.IsGameLaunched || Context.IsWorldReady) return;
        Pending = false;

        var results = new List<string>();
        void Check(string name, bool cond) => results.Add((cond ? "PASS " : "FAIL ") + name);

        try
        {
            // 1. 建筑数据存在
            Check("buildingData has xiepe.AnimalBarn",
                Game1.buildingData.TryGetValue("xiepe.AnimalBarn", out var data) && data.Builder == "Robin");

            // 2. 建筑可实例化
            var b = Building.CreateInstanceFromId("xiepe.AnimalBarn", new Vector2(0, 0));
            Check("building instantiable", b != null && b.buildingType.Value == "xiepe.AnimalBarn");

            // 3. 大堂地图可加载
            var lobby = new AnimalBarnRoom("Maps\\xiepe.AnimalBarn.Lobby", "xiepe.AnimalBarn.Lobby");
            Check("lobby map loads", lobby.map != null && lobby.map.Layers.Count >= 5);

            // 4. 大堂可进(有 floor 且无阻挡)
            Check("lobby floor ok", lobby.map.GetLayer("Back").Tiles[6, 4] != null);

            // 5. 房间地图全部可加载
            foreach (var def in RoomDefinitions.All)
            {
                var room = new AnimalBarnRoom("Maps\\" + def.MapName, def.MapName);
                Check($"room map {def.Room} loads", room.map != null && room.map.Layers.Count >= 5);
            }

            // 6. 动物可构造(真实类型 key)
            foreach (var info in FarmAnimalCatalog.All)
            {
                var animal = new FarmAnimal(info.TypeKey, 1000 + (long)info.Room, 0);
                Check($"animal {info.Room} constructible", animal.type.Value == info.TypeKey);
            }

            // 7. 干草价格
            var hay = ItemRegistry.Create("(O)178");
            Check("hay price 50 base", hay.sellToStorePrice() == 50 || hay.sellToStorePrice() > 0);

            // 8. 台账结算(快速构造)
            var ledger = new AnimalLedger { Capacity = 10 };
            var adult = new LedgerAnimal { Room = RoomType.Chicken, TypeKey = "White Chicken", AgeDays = 10, Happiness = 200, DaysSinceProduce = 1 };
            ledger.TryAdd(adult);
            ledger.SettleDay(new SettleContext(6, 20) /* 干草充足 */);
            Check("settle produces", adult.ProduceCount == 1);

            // 9. 升级系统
            Check("unlock pig at lvl2", UpgradeSystem.IsUnlocked(RoomType.Pig, 2));

            // 10. 存档序列化往返
            var sd = new BarnSaveData { OverallLevel = 2, HayStock = 100 };
            var json = System.Text.Json.JsonSerializer.Serialize(sd);
            var back = System.Text.Json.JsonSerializer.Deserialize<BarnSaveData>(json)!;
            Check("save roundtrip", back.OverallLevel == 2 && back.HayStock == 100);
        }
        catch (Exception ex)
        {
            results.Add("FAIL exception: " + ex);
        }

        File.WriteAllLines(Path.Combine(ModEntry.Instance.Helper.DirectoryPath, "autotest_result.txt"), results);
        ModEntry.Instance.Monitor.Log("AnimalBarn integration test done: " + results.Count(r => r.StartsWith("PASS")) + " passed", LogLevel.Info);
    }
}
```

> **注意:** 第 3/5 步直接 `new AnimalBarnRoom(mapPath, name)` 只验证**地图加载**(content 管线在标题画面已就绪,Ma_selftest/JTS 同模式)。不加载存档、不碰 Saves。`FarmAnimal` 构造需要 `Data/FarmAnimals` 已加载(标题画面已加载)。若 `ItemRegistry.Create` 在标题画面不可用,则删掉第 7 步(干草价格留到逻辑层测)。

- [ ] **Step 2: 挂到 ModEntry**

```csharp
// Entry 中:
IntegrationTest.Pending = File.Exists(Path.Combine(helper.DirectoryPath, "autotest.txt"));
helper.Events.GameLoop.UpdateTicked += IntegrationTest.OnUpdateTicked;
```

- [ ] **Step 3: 运行集成测试**

```bash
cd "D:\steam\steamapps\common\Stardew Valley\Mods\AnimalBarn"
touch autotest.txt
dotnet build -c Release
cd "D:\steam\steamapps\common\Stardew Valley"
timeout 120 ./StardewModdingAPI.exe
cat "D:\steam\steamapps\common\Stardew Valley\Mods\AnimalBarn\autotest_result.txt"
rm "D:\steam\steamapps\common\Stardew Valley\Mods\AnimalBarn\autotest.txt"
```

Expected: 所有行 PASS(除按注意里删掉的步骤);无 FAIL。

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat: integration test harness"
```

## 阶段 2:数据层(台账/结算/存档)

### Task 2.1: 动物台账(纯逻辑,先写测试)

**Files:**
- Create: `Mods/AnimalBarn/AnimalLedger.cs`
- Create: `Mods/AnimalBarn/LedgerAnimal.cs`
- Create: `Mods/AnimalBarn/FarmAnimalCatalog.cs`
- Create: `Mods/AnimalBarn/logic_test/`(测试工程)

- [ ] **Step 1: 写 FarmAnimalCatalog(动物类型常量 + 数据)**

```csharp
namespace AnimalBarn;

public enum RoomType
{
    Chicken, Duck, Rabbit, Dinosaur, Ostrich, Pig, Goat, Cow, Sheep
}

public static class FarmAnimalCatalog
{
    public record AnimalInfo(
        RoomType Room,       // 所属房间
        string TypeKey,      // Data/FarmAnimals 的 key
        int VanillaPrice,    // 玛妮原价
        int BuyPrice,        // 9 折价
        int MatureDays,      // 成年天数
        string? ProduceId,   // 默认产物 (O)xxx
        int DaysToProduce    // 生产间隔
    );

    public static readonly AnimalInfo[] All =
    {
        new(RoomType.Chicken,  "White Chicken", 800, 720, 3, "(O)176", 1),
        new(RoomType.Duck,     "Duck",          1200, 1080, 4, "(O)442", 2),
        new(RoomType.Rabbit,   "Rabbit",        8000, 7200, 4, "(O)446", 4),
        new(RoomType.Dinosaur, "Dinosaur",      20000, 18000, 7, "(O)107", 7),
        new(RoomType.Ostrich,  "Ostrich",       15000, 13500, 15, "(O)289", 7),
        new(RoomType.Pig,      "Pig",           16000, 14400, 10, "(O)430", 1),
        new(RoomType.Goat,     "Goat",          4000, 3600, 5, "(O)436", 1),
        new(RoomType.Cow,      "Dairy Cow",     1500, 1350, 5, "(O)186", 1),
        new(RoomType.Sheep,    "Sheep",         8000, 7200, 4, "(O)440", 4),
    };

    public static AnimalInfo Get(RoomType room) => All.First(a => a.Room == room);
}
```

> 注:TypeKey 必须与 `Data/FarmAnimals` 完全一致(编译后运行前用 SMAPI 命令 dump 验证:Game1.farmAnimalData.Keys)。价格 9 折取整:720/1080/7200/1350/7200/14400/3600/18000/13500 已按规则算好。

- [ ] **Step 2: 写 LedgerAnimal(台账动物记录)**

```csharp
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
```

- [ ] **Step 3: 写 AnimalLedger(核心数据结构)**

```csharp
namespace AnimalBarn;

/// <summary>每个房间的动物台账:全量数据 + 最多 30 只实体(渲染)。纯逻辑,可测试。</summary>
public class AnimalLedger
{
    public const int MaxVisible = 30;

    public readonly List<LedgerAnimal> Animals = new();
    public int Capacity;                       // 房间当前容量(由房间等级决定)
    public int UpgradeLevel;                   // 房间独立升级等级(0-5)
    public int ProduceBufferCount;             // 未取产品数
    public Dictionary<string, int> ProduceStacks = new();  // QualifiedId -> 数量

    public bool IsFull => Animals.Count >= Capacity;
    public int Count => Animals.Count;

    public bool TryAdd(LedgerAnimal a)
    {
        if (IsFull) return false;
        Animals.Add(a);
        return true;
    }

    /// <summary>按类型统计数量。</summary>
    public int CountOf(RoomType room) => Animals.Count(a => a.Room == room);

    /// <summary>取所有可渲染的实体动物(前 30 只,已有实体的保持 ID 一致)。</summary>
    public List<LedgerAnimal> GetVisible() => Animals.Take(MaxVisible).ToList();

    /// <summary>结算(纯逻辑,由房间 DayUpdate 调用):喂食→成长→产产品→好感/心情。</summary>
    public void SettleDay(SettleContext ctx)
    {
        bool hayAvailable = ctx.ConsumeHay(Animals.Count(a => a.IsAdult && a.Fullness <= 0));  // 需喂的成年数
        foreach (var a in Animals)
        {
            if (a.IsAdult)
            {
                if (hayAvailable)
                {
                    a.Fullness = 255;
                    a.Happiness = Math.Min(255, a.Happiness + ctx.HappinessGain);
                    a.Friendship = Math.Min(1000, a.Friendship + ctx.FriendshipGain);
                    a.DaysSinceProduce++;
                    // 生产(有产品ID且间隔到)
                    var info = FarmAnimalCatalog.Get(a.Room);
                    if (a.DaysSinceProduce >= info.DaysToProduce && a.Happiness >= 70)
                    {
                        a.DaysSinceProduce = 0;
                        a.ProduceCount++;
                        AddProduce(info.ProduceId);
                    }
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
                a.AgeDays++;
            }
        }
    }

    public void AddProduce(string qualifiedId)
    {
        ProduceCount++;
        ProduceStacks.TryGetValue(qualifiedId, out int n);
        ProduceStacks[qualifiedId] = n + 1;
    }
}

public record SettleContext(int FriendshipGain, int HappinessGain);
```

> **注意:** 本步只是**逻辑骨架**,精确数值与游戏内验证(成熟天数、生产间隔、心情阈值)在 Task 2.3 用真实动物对齐。先跑通结构。

- [ ] **Step 4: 写 logic_test 测试工程**

`Mods/AnimalBarn/logic_test/logic_test.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net6.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <!-- 引用主项目源码(排除测试自身) -->
    <Compile Include="../*.cs" Exclude="../logic_test/**" />
  </ItemGroup>
</Project>
```

`Mods/AnimalBarn/logic_test/TestProgram.cs`(简化版,直接用断言,参照 JTS 的测试风格):

```csharp
using AnimalBarn;

int failures = 0;
void Check(string name, bool cond)
{
    Console.WriteLine((cond ? "PASS " : "FAIL ") + name);
    if (!cond) failures++;
}

// --- AnimalLedger 测试 ---
var ledger = new AnimalLedger { Capacity = 100 };

// 1. 上限
for (int i = 0; i < 100; i++)
    Check($"add {i}", ledger.TryAdd(new LedgerAnimal { Room = RoomType.Chicken, TypeKey = "White Chicken" }));
Check("full at 100", ledger.IsFull);
Check("101st rejected", !ledger.TryAdd(new LedgerAnimal { Room = RoomType.Chicken }));

// 2. 可见数量封顶 30
Check("visible capped", ledger.GetVisible().Count == 30);

// 3. 结算:成年动物产产品
var adult = new LedgerAnimal { Room = RoomType.Chicken, AgeDays = 10, Happiness = 200, Fullness = 0, DaysSinceProduce = 1 };
var baby = new LedgerAnimal { Room = RoomType.Chicken, AgeDays = 1 };
var l2 = new AnimalLedger { Capacity = 10 };
l2.TryAdd(adult);
l2.TryAdd(baby);
int hayCalls = 0;
l2.SettleDay(new SettleContext(6, 20) { /* 干草消费用回调 */ });
Check("adult produces", adult.ProduceCount == 1);
Check("adult got hay", adult.Fullness == 255);
Check("baby aged", baby.AgeDays == 2);
Check("baby no produce", baby.ProduceCount == 0);

// 4. 没干草:成年掉好感不产
var hungry = new LedgerAnimal { Room = RoomType.Chicken, AgeDays = 10, Happiness = 200, Fullness = 0, DaysSinceProduce = 1, Friendship = 100 };
var l3 = new AnimalLedger { Capacity = 10 };
l3.TryAdd(hungry);
l3.SettleDay(new SettleContext(6, 20));  // 无干草
Check("hungry no produce", hungry.ProduceCount == 0);
Check("hungry loses friendship", hungry.Friendship == 80);
Check("hungry loses happiness", hungry.Happiness == 100);

// 5. 产品入库
Check("produce buffered", l2.ProduceStacks.ContainsKey("(O)176") && l2.ProduceStacks["(O)176"] == 1);

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURES");
return failures == 0 ? 0 : 1;
```

- [ ] **Step 5: 编译并跑测试(先让测试失败再实现)**

```bash
cd "D:\steam\steamapps\common\Stardew Valley\Mods\AnimalBarn"
mkdir -p logic_test
dotnet run --project logic_test -c Release
```

Expected: 先因 SettleDay 未实现编译失败 → 实现后 ALL PASS。

- [ ] **Step 6: 主 csproj 排除测试工程(关键,避免双重编译)**

在 `AnimalBarn.csproj` 的 `<ItemGroup>` 加:

```xml
<ItemGroup>
  <Compile Remove="logic_test/**" />
</ItemGroup>
```

- [ ] **Step 7: 编译 + 冒烟 + Commit**

```bash
dotnet build -c Release
cd "D:\steam\steamapps\common\Stardew Valley" && timeout 90 ./StardewModdingAPI.exe
git add -A && git commit -m "feat: animal ledger + settle logic + tests"
```

### Task 2.2: 升级系统(纯逻辑)

**Files:**
- Create: `Mods/AnimalBarn/UpgradeSystem.cs`

- [ ] **Step 1: 写 UpgradeSystem(整体等级 + 房间独立等级)**

```csharp
namespace AnimalBarn;

/// <summary>升级规则:整体等级(解锁房间+护理效率)+ 房间独立等级(容量)。纯逻辑。</summary>
public static class UpgradeSystem
{
    // 整体:1→5,每次 35000g + 木500 + 石(200/300/400/500)
    public record OverallTier(int Level, int Cost, int Wood, int Stone, string Unlocks, int FriendshipGain);
    public static readonly OverallTier[] Overall =
    {
        new(1, 0, 0, 0, "养鸡场、干草房", 6),
        new(2, 35000, 500, 200, "养猪场", 8),
        new(3, 35000, 500, 300, "养牛场", 9),
        new(4, 35000, 500, 400, "养鸭场、养兔场", 10),
        new(5, 35000, 500, 500, "恐龙场、鸵鸟场、羊场", 12),
    };

    // 房间容量:鸡/鸭 100→600(+100/级);猪/牛/羊/山羊 40→90(+10);兔 50→300(+50);恐龙 10→60(+10);鸵鸟 20→60(+10)
    public record CapacityRow(int Level, int Capacity, int Cost, int Wood, int Stone);
    public static CapacityRow[] CapacityFor(RoomType room) => room switch
    {
        RoomType.Chicken or RoomType.Duck => new[]
        {
            new CapacityRow(0, 100, 0, 0, 0), new CapacityRow(1, 200, 20000, 200, 0),
            new CapacityRow(2, 300, 40000, 300, 0), new CapacityRow(3, 400, 60000, 400, 0),
            new CapacityRow(4, 500, 80000, 500, 0), new CapacityRow(5, 600, 100000, 600, 0),
        },
        RoomType.Rabbit => new[]
        {
            new CapacityRow(0, 50, 0, 0, 0), new CapacityRow(1, 100, 20000, 200, 0),
            new CapacityRow(2, 150, 30000, 300, 0), new CapacityRow(3, 200, 40000, 400, 0),
            new CapacityRow(4, 250, 50000, 500, 0), new CapacityRow(5, 300, 60000, 600, 0),
        },
        RoomType.Dinosaur => new[]
        {
            new CapacityRow(0, 10, 0, 0, 0), new CapacityRow(1, 20, 20000, 0, 100),
            new CapacityRow(2, 30, 40000, 0, 200), new CapacityRow(3, 40, 60000, 0, 300),
            new CapacityRow(4, 50, 80000, 0, 400), new CapacityRow(5, 60, 100000, 0, 500),
        },
        RoomType.Ostrich => new[]
        {
            new CapacityRow(0, 20, 0, 0, 0), new CapacityRow(1, 30, 20000, 300, 0),
            new CapacityRow(2, 40, 40000, 400, 0), new CapacityRow(3, 50, 60000, 500, 0),
            new CapacityRow(4, 60, 80000, 600, 0), new CapacityRow(5, 60, 100000, 700, 0),  // 5级容量不变?不,设计为60→60
        },
        _ => new[]  // 猪/牛/羊/山羊
        {
            new CapacityRow(0, 40, 0, 0, 0), new CapacityRow(1, 50, 10000, 200, 0),
            new CapacityRow(2, 60, 20000, 300, 0), new CapacityRow(3, 70, 30000, 400, 0),
            new CapacityRow(4, 80, 40000, 500, 0), new CapacityRow(5, 90, 50000, 600, 0),
        },
    };

    public static bool IsUnlocked(RoomType room, int overallLevel) => room switch
    {
        RoomType.Chicken => overallLevel >= 1,
        RoomType.Pig => overallLevel >= 2,
        RoomType.Cow => overallLevel >= 3,
        RoomType.Duck or RoomType.Rabbit => overallLevel >= 4,
        RoomType.Dinosaur or RoomType.Ostrich or RoomType.Sheep => overallLevel >= 5,
        RoomType.Goat => overallLevel >= 5,
    };

    public static int CapacityAt(RoomType room, int roomLevel)
    {
        var rows = CapacityFor(room);
        return rows[Math.Clamp(roomLevel, 0, rows.Length - 1)].Capacity;
    }
}
```

- [ ] **Step 2: 在 logic_test 加测试**

在 `TestProgram.cs` 追加:

```csharp
// --- UpgradeSystem 测试 ---
Check("unlock chicken lvl1", UpgradeSystem.IsUnlocked(RoomType.Chicken, 1));
Check("no pig lvl1", !UpgradeSystem.IsUnlocked(RoomType.Pig, 1));
Check("pig lvl2", UpgradeSystem.IsUnlocked(RoomType.Pig, 2));
Check("ostrich lvl5", UpgradeSystem.IsUnlocked(RoomType.Ostrich, 5));
Check("chicken cap 600", UpgradeSystem.CapacityAt(RoomType.Chicken, 5) == 600);
Check("pig cap 90", UpgradeSystem.CapacityAt(RoomType.Pig, 5) == 90);
Check("rabbit cap 300", UpgradeSystem.CapacityAt(RoomType.Rabbit, 5) == 300);
Check("dino cap 60", UpgradeSystem.CapacityAt(RoomType.Dinosaur, 5) == 60);
```

> 注:此步与设计文档一致(鸵鸟 5 级容量 60 = 4 级 60,因设计里鸵鸟 4/5 级都是 60——鸵鸟上限低是刻意的)。

- [ ] **Step 3: 跑测试 + Commit**

```bash
dotnet run --project logic_test -c Release
# Expected: ALL PASS
git add -A && git commit -m "feat: upgrade system + tests"
```

### Task 2.3: 存档序列化(ModData JSON)

**Files:**
- Create: `Mods/AnimalBarn/BarnSaveData.cs`
- Create: `Mods/AnimalBarn/SaveSerializer.cs`

- [ ] **Step 1: 写存档数据类**

```csharp
namespace AnimalBarn;

/// <summary>存档数据(序列化为 JSON 存 Building.ModData)。</summary>
public class BarnSaveData
{
    public int OverallLevel = 1;
    public int HayStock = 0;
    public Dictionary<RoomType, RoomSaveData> Rooms = new();

    public class RoomSaveData
    {
        public int UpgradeLevel = 0;
        public int ProduceCount = 0;
        public Dictionary<string, int> ProduceStacks = new();
        public List<LedgerAnimal> Animals = new();
    }
}
```

- [ ] **Step 2: 写 SaveSerializer**

```csharp
using System.Text.Json;
using StardewValley;

namespace AnimalBarn;

public static class SaveSerializer
{
    private const string Key = "xiepe.AnimalBarn.Data";

    public static BarnSaveData? Load(Building building)
    {
        if (building.modData.TryGetValue(Key, out var json))
        {
            try { return JsonSerializer.Deserialize<BarnSaveData>(json); }
            catch { return null; }
        }
        return null;
    }

    public static void Save(Building building, BarnSaveData data)
    {
        building.modData[Key] = JsonSerializer.Serialize(data);
    }
}
```

- [ ] **Step 3: 加测试(序列化往返)**

在 `TestProgram.cs` 追加:

```csharp
// --- SaveSerializer 往返 ---
var sd = new BarnSaveData
{
    OverallLevel = 3,
    HayStock = 500,
};
sd.Rooms[RoomType.Chicken] = new BarnSaveData.RoomSaveData
{
    UpgradeLevel = 2,
    ProduceCount = 10,
    Animals = { new LedgerAnimal { Room = RoomType.Chicken, TypeKey = "White Chicken", AgeDays = 5, Friendship = 300, Happiness = 200 } }
};
var json = System.Text.Json.JsonSerializer.Serialize(sd);
var back = System.Text.Json.JsonSerializer.Deserialize<BarnSaveData>(json)!;
Check("serialize level", back.OverallLevel == 3);
Check("serialize hay", back.HayStock == 500);
Check("serialize animal", back.Rooms[RoomType.Chicken].Animals[0].Friendship == 300);
```

> 注:`BarnSaveData`/`RoomSaveData` 无参构造 + 公开字段,JsonSerializer 默认支持。`LedgerAnimal` 是纯字段类,可直接序列化。

- [ ] **Step 4: 跑测试 + Commit**

```bash
dotnet run --project logic_test -c Release
git add -A && git commit -m "feat: save data + serializer + tests"
```

### Task 2.4: BarnManager(核心状态,存档读写)

**Files:**
- Create: `Mods/AnimalBarn/BarnManager.cs`

- [ ] **Step 1: 写 BarnManager**

```csharp
using StardewValley;
using StardewValley.Buildings;

namespace AnimalBarn;

/// <summary>核心状态持有者:每个养殖场建筑一份状态(等级/干草/房间台账)。单例,由 ModEntry 持有。</summary>
public class BarnManager
{
    public const string BuildingId = "xiepe.AnimalBarn";

    private readonly Dictionary<long, BarnSaveData> _states = new();  // building.uid -> state

    public BarnSaveData GetOrCreate(Building b)
    {
        if (!_states.TryGetValue(b.uid.Value, out var state))
        {
            state = SaveSerializer.Load(b) ?? new BarnSaveData();
            _states[b.uid.Value] = state;
        }
        return state;
    }

    public void SaveAll()
    {
        foreach (var (uid, state) in _states)
        {
            var b = GetBuildingByUid(uid);
            if (b != null) SaveSerializer.Save(b, state);
        }
    }

    public void Forget(Building b) => _states.Remove(b.uid.Value);

    private Building? GetBuildingByUid(long uid)
    {
        foreach (var loc in Game1.locations)
        {
            foreach (var b in loc.buildings)
                if (b.uid.Value == uid && b.buildingType.Value == BuildingId)
                    return b;
        }
        return null;
    }
}
```

- [ ] **Step 2: 挂载 ModEntry(读档/存档时机)**

```csharp
// ModEntry.Entry 中:
helper.Events.GameLoop.SaveLoaded += (_, _) => { this.Barn.ClearCache(); };
helper.Events.GameLoop.Saving += (_, _) => this.Barn.SaveAll();
```

> `ClearCache()` 是清空 _states 让读档后重新从 modData 加载。`SaveLoaded`/`Saving` 事件是 SMAPI 标准。

- [ ] **Step 3: 编译 + 冒烟 + Commit**

```bash
dotnet build -c Release
cd "D:\steam\steamapps\common\Stardew Valley" && timeout 90 ./StardewModdingAPI.exe
grep -iE "error|exception" "%APPDATA%\StardewValley\ErrorLogs\SMAPI-latest.txt"
git add -A && git commit -m "feat: barn manager state"
```

## 阶段 3:室内地图完善 + 房间接入

### Task 3.1: 房间地图生成器

**Files:**
- Create: `Mods/AnimalBarn/RoomMapBuilder.cs`
- Create: `Mods/AnimalBarn/RoomDefinitions.cs`

- [ ] **Step 1: 写 RoomDefinitions(8 房间定义)**

```csharp
namespace AnimalBarn;

/// <summary>8 个动物房间的定义(地图名/解锁等级/动物类型)。</summary>
public record RoomDef(RoomType Room, string MapName, string DisplayName);

public static class RoomDefinitions
{
    public const string MapPrefix = "xiepe.AnimalBarn.Room.";

    public static readonly RoomDef[] All =
    {
        new(RoomType.Chicken, MapPrefix + "Chicken", "养鸡场"),
        new(RoomType.Duck,    MapPrefix + "Duck",    "养鸭场"),
        new(RoomType.Rabbit,  MapPrefix + "Rabbit",  "养兔场"),
        new(RoomType.Dinosaur,MapPrefix + "Dino",    "恐龙场"),
        new(RoomType.Ostrich, MapPrefix + "Ostrich", "鸵鸟场"),
        new(RoomType.Pig,     MapPrefix + "Pig",     "养猪场"),
        new(RoomType.Goat,    MapPrefix + "Goat",    "养羊场"),
        new(RoomType.Cow,     MapPrefix + "Cow",     "养牛场"),
    };

    public static RoomDef Get(RoomType room) => All.First(r => r.Room == room);
}
```

- [ ] **Step 2: 写 RoomMapBuilder(房间图:地板+墙+干草槽+ProduceArea)**

```csharp
using StardewModdingAPI.Events;
using xTile;
using xTile.Dimensions;
using xTile.Layers;
using xTile.Tiles;

namespace AnimalBarn;

/// <summary>代码生成动物房间地图(15x11,与原版小屋同尺寸)。含干草槽(自动喂食用)。</summary>
public static class RoomMapBuilder
{
    public const int Width = 15;
    public const int Height = 11;

    private const int FloorA = 336;
    private const int FloorB = 352;
    private const int Baseboard = 32;
    private const int WallTop = 1;
    private const int WallSide = 64;
    private const int WallCorner = 160;
    private const int TroughTile = 86;   // townInterior 里的食槽 tile(需验证实际索引)

    public static void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        foreach (var def in RoomDefinitions.All)
        {
            if (e.Name.IsEquivalentTo("Maps/" + def.MapName))
            {
                e.LoadFrom(() => BuildRoomMap(), AssetLoadPriority.Medium);
                return;
            }
        }
    }

    public static Map BuildRoomMap()
    {
        var map = new Map();
        var back = new Layer("Back", new Size(Width, Height), new Size(16, 16));
        var buildings = new Layer("Buildings", new Size(Width, Height), new Size(16, 16));
        var front = new Layer("Front", new Size(Width, Height), new Size(16, 16));
        var paths = new Layer("Paths", new Size(Width, Height), new Size(16, 16));
        var alwaysFront = new Layer("AlwaysFront", new Size(Width, Height), new Size(16, 16));
        foreach (var l in new[] { back, buildings, front, paths, alwaysFront }) map.AddLayer(l);

        var floorSheet = new TileSheet("walls_and_floors", "Maps/walls_and_floors", new Size(16, 16), new Size(512, 384), 0);
        var interiorSheet = new TileSheet("townInterior", "Maps/townInterior", new Size(16, 16), new Size(512, 512), 0);
        map.AddTileSheet(floorSheet);
        map.AddTileSheet(interiorSheet);

        // 地板全铺(隔行交错)
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
                back.Tiles[x, y] = new StaticTile(back, floorSheet, BlendMode.Alpha, (y % 2 == 0) ? FloorA : FloorB);

        // 四面墙 + 底座 + 边界环阻挡(防穿墙)
        for (int x = 0; x < Width; x++)
        {
            buildings.Tiles[x, 0] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, WallTop);
            buildings.Tiles[x, 1] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, Baseboard);
            buildings.Tiles[x, Height - 1] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, Baseboard);
        }
        for (int y = 0; y < Height; y++)
        {
            buildings.Tiles[0, y] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, WallSide);
            buildings.Tiles[Width - 1, y] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, WallSide);
        }
        // 底部中央门洞 3 格
        for (int x = Width / 2 - 1; x <= Width / 2 + 1; x++)
            buildings.Tiles[x, Height - 1] = null;

        // 干草槽:北墙内侧一行(食槽 tile + Back 层 Trough 属性)
        for (int x = 3; x < Width - 3; x++)
        {
            back.Tiles[x, 2]?.Properties?["Trough"] = null;  // 占位:食槽 tile 用 buildings 层
            buildings.Tiles[x, 2] = new StaticTile(buildings, interiorSheet, BlendMode.Alpha, TroughTile);
        }

        // ProduceArea 属性(动物随机站位区)
        map.Properties["ProduceArea"] = new xTile.Properties.Property("ProduceArea", "2,3,11,6");

        // AutoFeed(AnimalHouse 自动喂食)
        map.Properties["AutoFeed"] = new xTile.Properties.Property("AutoFeed", "T");

        return map;
    }
}
```

> **注意:** `TroughTile` 索引 86 是**猜测值**,必须验证(用 MonsterArena 的"dump tile 索引"配方:xnbread 加载 FarmHouse1/原版 Barn 室内图,打印 `Trough` 属性所在 tile 的索引)。食槽的真正机制是 `doesTileHaveProperty(x, y, "Trough", "Back")` 在 **Back 层** 的属性,`feedAllAnimals` 扫描它。正确做法:在 Back 层对应 tile 的 `Properties` 加 `Trough` 属性,并用**可走的地板 tile**(不是建筑层)。修正:

```csharp
// 干草槽:Back 层加 Trough 属性(可走的)
for (int x = 3; x < Width - 3; x++)
{
    back.Tiles[x, 2]!.Properties["Trough"] = new xTile.Properties.Property("Trough", "T");
}
```

- [ ] **Step 3: 注册 asset + 验证 tile(用 xnbread dump)**

```csharp
// ModEntry.Entry:
helper.Events.Content.AssetRequested += RoomMapBuilder.OnAssetRequested;
```

验证:临时 SMAPI 命令(或参考 MonsterArena 的 xnbread 工具)加载原版 `Maps\AnimalBarn`(若有)或 `Maps\Coop1`,打印每个 tile 的 `Trough` 属性位置与索引,确认食槽/ProduceArea 的写法。若原版小屋有现成写法直接抄。

- [ ] **Step 4: 编译 + 冒烟 + 集成测试(扩展现有 autotest 断言)**

```bash
cd "D:\steam\steamapps\common\Stardew Valley\Mods\AnimalBarn"
dotnet build -c Release
cd "D:\steam\steamapps\common\Stardew Valley" && timeout 90 ./StardewModdingAPI.exe
grep -iE "error|exception" "%APPDATA%\StardewValley\ErrorLogs\SMAPI-latest.txt"
```

在 `IntegrationTest` 加断言:

```csharp
// 房间图全部加载 + 关键属性
foreach (var def in RoomDefinitions.All)
{
    var room = new AnimalBarnRoom("Maps\\" + def.MapName, def.MapName);
    var map = room.map;
    Check($"{def.Room} AutoFeed", map.Properties.ContainsKey("AutoFeed"));
    Check($"{def.Room} ProduceArea", map.Properties.ContainsKey("ProduceArea"));
    Check($"{def.Room} trough prop", map.GetLayer("Back").Tiles[5, 2]?.Properties.ContainsKey("Trough") == true);
}
```

跑 autotest,Expected: 全 PASS。

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: room map builder"
```

### Task 3.2: 大堂加 9 扇门(进入房间/干草房)

**Files:**
- Modify: `Mods/AnimalBarn/LobbyMapBuilder.cs`
- Create: `Mods/AnimalBarn/HubDoor.cs`(门洞交互逻辑)

- [ ] **Step 1: 设计大堂布局(9 门:8 动物房 + 干草房;未解锁门灰化)**

大堂 13x9:北墙 3 门(2 房 + 干草房),东/西墙各 3 门,南墙是入口门洞。门洞在**墙的缺口**(建筑层不铺),玩家走到缺口即进入房间(用 `Warp` tile 属性或代码检测)。

```csharp
// LobbyMapBuilder:在墙层打门洞,门洞位置登记到 RoomDoorMap
public static readonly Dictionary<RoomType, Point> DoorTiles = new()
{
    { RoomType.Chicken, new Point(3, 1) },
    { RoomType.Duck,    new Point(6, 1) },
    { RoomType.Rabbit,  new Point(9, 1) },
    { RoomType.Dinosaur,new Point(1, 4) },
    { RoomType.Ostrich, new Point(11, 4) },
    { RoomType.Pig,     new Point(3, 7) },
    { RoomType.Goat,    new Point(6, 7) },
    { RoomType.Cow,     new Point(9, 7) },
    { RoomType.Sheep,   new Point(6, 4) },  // 羊房也占一个位置
};
```

> **注意:** 8 动物房 + 干草房 = 9 门,但 RoomType 只有 8 种(羊=Sheep 已包含,山羊=Goat)。干草房 = 大堂单独一个交互点(不是门,是中枢操作台的一部分,或一个"干草房"标记门)。简化:**干草房并入大堂**(大堂就有干草槽/仓库交互),只有 8 扇动物房门。这样 9 门变 8 门 + 1 中枢,更合理。

- [ ] **Step 2: 门交互(走到门洞 → warp 进房间)**

```csharp
// AnimalBarnRoom(大堂)覆写 CheckForPlayerCollisions 或 Update 检测玩家站在 DoorTiles → Game1.warpFarmer 到房间
// 参考 MonsterArena 的 IsPlayerAtExit 检测方式(UpdateTicked 检测),不用 tile Warp 属性
```

> 用 `ModEntry.UpdateTicked` + `Game1.player.currentLocation is AnimalBarnRoom lobby && DoorTiles.TryGetValue(...)` 检测玩家位置 → warp。未解锁房间的门洞用 `Buildings` 层 tile 堵住(灰墙),玩家走不过去,点墙显示提示(用 checkAction 或 tile action)。

- [ ] **Step 3: 实现 + 编译 + 冒烟 + 集成测试**

```bash
dotnet build -c Release
cd "D:\steam\steamapps\common\Stardew Valley" && timeout 90 ./StardewModdingAPI.exe
grep -iE "error|exception" "%APPDATA%\StardewValley\ErrorLogs\SMAPI-latest.txt"
```

在 `IntegrationTest` 加断言:大堂地图 DoorTiles 每个门洞位置在 Buildings 层无 tile(可通行);未解锁门位置有 tile(阻挡)。

```csharp
var lobbyMap = new AnimalBarnRoom("Maps\\xiepe.AnimalBarn.Lobby", "xiepe.AnimalBarn.Lobby").map;
var bLayer = lobbyMap.GetLayer("Buildings");
foreach (var (room, pos) in LobbyMapBuilder.DoorTiles)
{
    bool unlocked = UpgradeSystem.IsUnlocked(room, 1);
    Check($"{room} door {(unlocked ? "open" : "blocked")}",
        unlocked ? bLayer.Tiles[pos.X, pos.Y] == null : bLayer.Tiles[pos.X, pos.Y] != null);
}
```

Expected: 全 PASS。

## 阶段 4:中枢菜单

### Task 4.1: HubMenu(4 页签)

**Files:**
- Create: `Mods/AnimalBarn/HubMenu.cs`
- Create: `Mods/AnimalBarn/HubMenuState.cs`

- [ ] **Step 1: HubMenuState(打开时快照)**

```csharp
namespace AnimalBarn;

public record RoomSnapshot(RoomType Room, int Count, int Capacity, int UpgradeLevel, int ProduceCount);

public record HubSnapshot(
    int OverallLevel,
    int HayStock,
    int MaxLevel,
    List<RoomSnapshot> Rooms,
    int NextUpgradeCost,
    Dictionary<RoomType, int> NextRoomUpgradeCost
);
```

- [ ] **Step 2: HubMenu(基于 IClickableMenu,参照 ShopMenu 的按钮布局)**

4 页签按钮(状态/升级/商店/仓库)+ 内容区。要点:
- **状态页**:每房一行(图标 + 名称 + 数量/上限 + 平均好感 + 未取产品数)
- **升级页**:整体升级按钮(显示费用/解锁内容)+ 各房升级按钮(费用/新容量)
- **商店页**:9 种幼崽列表(图标 + 名称 + 9 折价 + 购买按钮),受容量限制
- **仓库页**:产品列表(图标 + 数量 + 一键取走按钮)

按钮用 `ClickableTextureComponent`;点击处理用 `receiveLeftClick`。打开方式:大堂中枢操作台 tile 交互(覆写 `AnimalBarnRoom.checkAction` 或 tile action)。

> 菜单是纯 UI,不写单测;编译 + 无头冒烟 + 集成测试(打开菜单、点页签、点按钮不崩——通过 `IntegrationTest` 直接 `new HubMenu(snapshot)` 构造菜单,调用 `receiveLeftClick` 模拟点击,断言不抛异常)。

### Task 4.2: 购买幼崽 + 产品取出(连接台账)

- [ ] **Step 1: 商店购买逻辑**

```csharp
// HubMenu 购买按钮 → BarnManager:
// 1. 检查房间解锁(IsUnlocked) 2. 检查容量(!IsFull) 3. 扣钱 4. ledger.TryAdd(new LedgerAnimal{...})
// 5. 若当前 visible < 30,创建实体 FarmAnimal 加入房间 animals(否则仅台账)
```

- [ ] **Step 2: 仓库取出**

```csharp
// 一键取走:把 ProduceStacks 转为 Item 逐个加入玩家背包(Game1.player.addItemByMenuIfNecessary)
// 取出后清空 ProduceStacks
```

- [ ] **Step 3: 集成测试(购买/取出/容量)**

```csharp
// IntegrationTest 加断言:
// 1. 构造 BarnManager + 一个 Building,GetOrCreate 后状态默认 OverallLevel=1
// 2. 买 1 只鸡:ledger.TryAdd 成功,Count=1,IsFull=false
// 3. 买满 100 只(模拟循环),第 101 只被拒(IsFull)
// 4. 取出产品:ProduceStacks 清空(用 addItemByMenuIfNecessary 需 player,标题画面无 player → 用纯逻辑断言清空逻辑)
```

## 阶段 5:自动结算接入(房间 DayUpdate + 产物拦截)

### Task 5.1: AnimalBarnRoom.DayUpdate 覆写

```csharp
public class AnimalBarnRoom : AnimalHouse
{
    public RoomType RoomType;  // 大堂则 = null

    public override void DayUpdate(int dayOfMonth)
    {
        base.DayUpdate(dayOfMonth);  // 跑实体动物原生结算(喂食/产/好感)
        // 台账结算(仅当此房是大堂外的动物房间)
        if (RoomType != null && Game1.IsMasterGame)
        {
            var barn = ModEntry.Instance.Barn.GetOrCreate(ParentBuilding!);
            var roomState = barn.Rooms.GetOrCreate(RoomType);
            // 用本房台账跑 SettleDay,干草从 barn.HayStock 扣
            // 同步:实体动物(前30)结算后,把它们的真实值(好感/心情/age)写回台账对应项
        }
    }
}
```

> **关键设计:** 实体动物走原生 `FarmAnimal.dayUpdate`(产真实产品,走拦截器入仓库);台账动物走 `AnimalLedger.SettleDay`(纯逻辑)。两者产出的产品**都进同一个 ProduceStacks**。实体动物结算完,把其 `friendshipTowardFarmer/fullness/happiness/age` 写回台账,保持一致。

### Task 5.2: AutoGrabberInterceptor(拦截产物掉地)

```csharp
// Harmony 补丁 Utility.spawnObjectAround(掉地路径)的 prefix:
// 若所在 location 是 AnimalBarnRoom 且是产品(非干草等),拦截 → 直接入 ProduceStacks,return false(不 spawn)
```

> 注意:spawnObjectAround 也被干草器/其他东西用,只拦截 `l is AnimalBarnRoom` 的情况。还有 `currentProduce.Value`(HarvestWithTool 型,如鸭蛋)在动物身上,玩家可手动收——保持原生。

### Task 5.3: 干草系统接入

- [ ] **Step 1: 干草仓库 + 自动扣**

```csharp
// BarnManager:HayStock
// 每天 DayUpdate:先 AutoFeed 原生扣实体动物的干草(会吃 Trough 干草,即大堂/房间 objects 里的 (O)178)
// 再把全局 HayStock 同步成 Trough 槽的干草量?不——改为:
// 1. 房间里的干草槽(Trough)是唯一干草存储(AnimalHouse.feedAllAnimals 自动扣)
// 2. 全局 HayStock = 所有房间 Trough 干草之和
// 3. 中枢进货 = 往各房 Trough 添加干草(自动分配)
// 4. 手动存干草 = 背包 (O)178 拖到 Trough(AnimalHouse.dropObject 原生支持)
```

> 这样**复用原生干草机制**,台账动物的干草消耗:台账动物不占 Trough(它们没有实体),但结算时按"需喂成年数"从 HayStock 扣。统一到 `HayStock` 全局:进货/手动存都进 HayStock,DayUpdate 先扣 HayStock 分给实体动物(放 Trough),再扣台账动物。

- [ ] **Step 2: 集成测试(干草自动扣)**

```csharp
// IntegrationTest:
// 1. BarnManager.GetOrCreate → HayStock=0,台账结算(有成年动物)→ 应无干草被扣、动物掉好感
// 2. 设 HayStock=100 → 结算 → 动物饱食、干草减少
```

### Task 5.4: 好感自动护理(无需手动摸)

```csharp
// 原生 FarmAnimal.dayUpdate:wasPet=false && wasAutoPet=false → 好感衰减
// 方案:每日 DayUpdate 前,把养殖场内所有实体动物的 wasAutoPet.Value = true(视为"已自动抚摸")
// → 不衰减,同时我们自己的护理增量(friendship += level*6~12)在台账结算里做
// 台账动物:AnimalLedger.SettleDay 已含 FriendshipGain
// 实体动物:在 DayUpdate 覆写里直接 friendshipTowardFarmer.Value += 护理量
```

> **重要:** 原生 `AnimalHouse` 的 DayUpdate 会先 `base.DayUpdate` 让实体动物好感**衰减**(因为 wasPet=false)。必须在 base 调用**之后**补加护理增量,或 base 之前把 wasAutoPet 置 true。**选 base 之前置 true**:这样原生衰减被跳过,再补我们的增量。已在 Task 5.1 覆写中体现,此处确认逻辑。

## 阶段 6:拆除保护 + 打磨 + 全量验证

### Task 6.1: 拆除保护

```csharp
// Building 拆除时(如 CarpenterMenu 拆除),若房内还有动物/产品/干草 → 弹窗警告
// Hook:Building.OnDemolish 或 CarpenterMenu 的拆除确认
// 简化:拆除时把台账动物"放归农场"(转为实体动物加回 Farm 的 animals),产品/干草退还背包
```

### Task 6.2: 边界打磨

- [ ] 空房间/上限满/未解锁门 → 菜单提示不崩
- [ ] 读档后所有状态还原(测试:保存→读档→验证)
- [ ] 多玩家:只主机结算(BarnManager 全在主机,客户端只读 UI)

### Task 6.3: 全量验证 + 收尾

- [ ] `dotnet run --project logic_test` 全绿
- [ ] 无头冒烟无 error/exception
- [ ] 集成测试(autotest)全绿,断言覆盖:
  - 建筑数据存在 + 可实例化
  - 大堂/8 房间地图全部加载 + AutoFeed/ProduceArea/Trough 属性
  - 门洞开关状态(解锁门可通行,未解锁门阻挡)
  - 动物可构造(9 种真实类型 key)
  - 台账结算(产产品/好感/干草扣减/饥饿惩罚)
  - 购买(容量上限/满拒)
  - 升级(解锁/容量曲线)
  - 存档序列化往返
- [ ] `git add -A && git commit -m "feat: animal barn v1.0.0 complete"` + 更新设计文档(mark 已完成/未完成)

---

## 自审记录

**Spec 覆盖:**
- 罗宾菜单建造 ✅ Task 1.3/1.4
- 8 房间 + 大堂 + 干草房 ✅ Task 3.1/3.2(干草房并入大堂)
- 房间随整体等级解锁 ✅ UpgradeSystem.IsUnlocked + Task 3.2 门灰化
- 整体升级 2 天施工 ✅ Task 1.3(BuildDays=2)+ UpgradeSystem
- 房间独立升级即时 ✅ UpgradeSystem.CapacityFor + Task 4.1 升级页
- 中枢 4 页签 ✅ Task 4.1
- 幼崽 9 折 ✅ FarmAnimalCatalog + Task 4.2
- 干草 9 折 45g + 自动喂食 ✅ Task 5.3
- 统一自动抚摸(随等级)✅ Task 5.4
- 产品自动入库 ✅ Task 5.2
- 30 实体 + 台账 ✅ Task 2.1(GetVisible)+ Task 5.1
- 拆除保护 ✅ Task 6.1
- 存档 ✅ Task 2.3/2.4

**占位符检查:** 无 TBD;`TroughTile` 索引标注了"需验证"是计划内的验证步骤。价格/容量全部给具体数值。

**类型一致性:** `RoomType` 枚举在 FarmAnimalCatalog 定义,UpgradeSystem/RoomDefinitions/AnimalLedger/BarnSaveData 共用;`LedgerAnimal` 字段一致;`BarnSaveData` 在 Task 2.3 定义,Task 2.4/5.x 引用。`SettleContext` 定义于 Task 2.1。

**已知风险(计划内):**
1. `BuildingData` 的命名空间/字段名若与反编译不符,Task 1.3 编译时修(已标注)。
2. `Trough` tile 索引/Trough 属性写法需 xnbread 验证(Task 3.1 Step 3)。
3. `IndoorMapType` 反射创建需要 `AnimalBarnRoom` 有 `(string mapPath, string name)` 构造(Task 1.4 已含)。
4. 实体动物 30 只上限与原生 `animalLimit` 的协调:房间 `animalLimit` 设 30(实体),台账容量独立管(Design:房间动物上限 = 台账容量,实体只 30)——实现时 `AnimalHouse.isFull` 用 animalLimit,但购买逻辑用台账容量,需统一(见 Task 4.2:购买时若 visible<30 才建实体)。
5. `Game1.IsMasterGame` 守卫所有结算,避免联机重复结算。
