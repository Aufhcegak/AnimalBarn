# 动物养殖场 AnimalBarn

星露谷物语 (Stardew Valley) 1.6 的大型动物养殖建筑 mod —— 罗宾建造的「动物养殖场」，内含 **9 个独立动物房间 + 自动结算系统 + 中枢管理菜单**。

## 功能特色

### 🏗️ 大型养殖建筑
- 罗宾建造菜单新增「动物养殖场」，大型建筑（含大堂 + 9 个动物房间）
- 建筑外观为定制涂装（绿顶 + 暖棕）

### 🐔 9 个动物房间（每种动物独立一间）
- 养鸡场 / 养鸭场 / 养兔场 / 恐龙场 / 鸵鸟场 / 养猪场 / 羊场（山羊）/ 养牛场 / 养羊场（绵羊）
- 每间房内部：左右动物圈（栅栏）+ 中央走道（人走）
- 房间入口：大堂北墙一扇门 → 右键弹**房间选择菜单**，选房间直达
- 从房间南出口返回大堂

### ⚙️ 全自动每日结算
- **自动喂食**：动物吃全局干草库存（不依赖筒仓）
- **自动产蛋/奶/毛**：产物自动收进房间仓库（不用满地捡）
- **自动抚摸机**：每个房间相当于一台自动抚摸机 —— 动物好感/心情每天自动提升（1 级房间=一半效果，满级房间=满效果）
- **动物不会"被关在外面"**：动物判定为在屋内被照顾，心情始终高昂

### 🖥️ 中枢管理菜单（大堂电脑）
- **状态页**：各房间动物数/容量/待收产品
- **升级页**：整体升级（解锁房间）+ 各房间升级（扩容）
- **商店页**：购买 9 种动物（shift=5 只 / Ctrl+shift=25 只批量）+ 购买干草
- **仓库页**：按物品+星级分页展示产物（普通/银星/金星/铱星），点击取走（shift 批量）

### 💾 存档安全
- 全部使用原版类型（AnimalHouse/Object），不自定义 GameLocation/Object 子类 → 不卡保存、不崩存档
- 状态存建筑 modData（JSON），读档完整恢复

## 安装

1. 安装 [SMAPI](https://smapi.io/)（4.x，游戏 1.6.15+）
2. 下载本 mod，放入 `Mods/AnimalBarn/` 文件夹
3. 启动游戏，罗宾建造菜单里找「动物养殖场」（5 万金 + 木 500 + 石 100，2 天建成）

## 玩法流程

1. 建好养殖场 → 进大堂（北墙电脑可 F1 查看）
2. 点电脑 → 商店页买动物（先买鸡，1 级就解锁）
3. 大堂北墙门 → 选房间进 → 看动物在圈里吃草产蛋
4. 睡一天 → 产物自动进仓库 → 电脑仓库页取走
5. 升级养殖场 → 解锁猪/牛/鸭/兔/恐龙/鸵鸟/羊等房间

## 开发

```bash
# 构建(输出到 Mods/AnimalBarn/AnimalBarn.dll)
dotnet build -c Release

# 纯逻辑单元测试
cd logic_test && dotnet run -c Release

# 游戏内自动验证(放 autotest.txt 进游戏,结果写 autotest_bot.txt)
# 反编译源码(调试参考)
ilspycmd -p -o src "Stardew Valley.dll"
```

## 兼容性

- 星露谷 **1.6.15** / SMAPI **4.5.1**（已验证）
- 支持中文（简体）
- 与 LookupAnything 兼容（动物/物品按 F1 可查详情）
- 联机：仅主机可操作养殖场（客机只读，防 desync）

## 仓库结构

```
Mods/AnimalBarn/
├── ModEntry.cs            # 入口/事件接线
├── LobbyMapBuilder.cs     # 大堂地图生成
├── RoomMapBuilder.cs      # 房间地图生成
├── HubMenu.cs             # 中枢 4 页签菜单
├── HubConsole.cs          # 中枢电脑(自定义大件)
├── SettlementService.cs   # 每日结算编排
├── AnimalLedger.cs        # 台账结算逻辑
├── BarnPatches.cs         # Harmony 补丁(结算/心情/提示)
├── AutoGrabberInterceptor.cs  # 产物拦截
├── BuildDataInjection.cs  # 建筑/物品数据注入
├── docs/                  # 设计文档 + 渲染预览图
└── logic_test/            # 纯逻辑单元测试
```

## 致谢

- [SMAPI](https://smapi.io/) —— mod 加载框架
- [Stardew Valley Wiki](https://stardewvalleywiki.com/) —— modding 文档
