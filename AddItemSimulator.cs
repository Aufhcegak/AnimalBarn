namespace AnimalBarn;

/// <summary>
/// 取货"实际放入量"回归模拟器 —— 照抄原版 Farmer.addItemToInventory 语义
/// (Farmer.cs:4277-4335 反编译)+ Item.addToStack(Item.cs:504-523)。
///
/// 背景(2026-08-04 用户实测):进大门第 1 次取东西,弹"背包已满"+ 扣的给的不对。
/// 根因:旧算法靠 item.Stack 差值算放入量,但原版放进【空格】时 item.Stack 不减
/// (Farmer.cs:4318 `Items[i] = item; num = 0;` → 返回 null)→ 背包没同类物品时差值=0。
///
/// 本模拟器被两个工程编译:
///   - AnimalBarn 主工程:IntegrationTest(标题画面无头)用它在无 Game1.player 时验证算法;
///   - logic_test:TestProgram 用它做全场景红绿回归。
/// 与真实游戏的唯一差异:真实 addItemToInventoryBool 走 GetItemReceiveBehavior/净堆叠,
/// 动物产品(蛋/奶/毛)一律需要背包空间,堆叠上限 999 —— 本模拟器与此一致。
/// </summary>
public static class AddItemSimulator
{
    public const int MaxStack = 999;

    public class FakeItem
    {
        public string Id;
        public int Quality;
        public int Stack;

        public FakeItem(string id, int quality, int stack)
        {
            Id = id;
            Quality = quality;
            Stack = stack;
        }

        // 原版 Item.canStackWith(Item.cs:720-751):同类型 + 同质量 + 同 QualifiedItemId + 同名。
        // 动物产品是同一种 Object 类型,实际判定 = 同 ID + 同质量。
        public bool CanStackWith(FakeItem? other)
            => other != null && Id == other.Id && Quality == other.Quality;
    }

    public class FakeInventory
    {
        public List<FakeItem?> Slots;
        public int Capacity;

        public FakeInventory(int capacity = 36)
        {
            Capacity = capacity;
            Slots = Enumerable.Repeat<FakeItem?>(null, capacity).ToList();
        }

        /// <summary>原版 Farmer.addItemToInventory(Farmer.cs:4277-4335)语义照抄。
        /// 返回 null = 全部放进;返回 item = 剩余量(item.Stack 已减为剩余)。
        /// ⚠️ 放进空格时 item.Stack 不减(原版 `Items[i] = item; num = 0;` 后返回 null)。</summary>
        public FakeItem? AddItem(FakeItem item)
        {
            if (item == null) return null;
            int num = item.Stack;

            // 1) 合并到所有可堆叠格子
            foreach (var slot in Slots)
            {
                if (!item.CanStackWith(slot)) continue;
                int beforeMerge = item.Stack;
                num = AddToStack(slot!, item);          // slot.Stack += item.Stack(超上限返回剩余)
                int merged = beforeMerge - num;
                if (merged > 0)
                {
                    item.Stack = num;
                    if (num < 1) break;
                }
            }

            // 2) 剩余放第一个空格
            if (num > 0)
            {
                for (int i = 0; i < Capacity && i < Slots.Count; i++)
                {
                    if (Slots[i] == null)
                    {
                        Slots[i] = item;                 // ★ 放空格:item.Stack 不减(num 保持)!
                        num = 0;
                        break;
                    }
                }
            }

            return num <= 0 ? null : item;               // ★ 放空格后返回 null(item.Stack 未减)
        }

        /// <summary>原版 Item.addToStack(Item.cs:504-523):dst.Stack += src.Stack,超上限返回超出的剩余量。</summary>
        private static int AddToStack(FakeItem dst, FakeItem src)
        {
            if (dst.Stack + src.Stack <= MaxStack)
            {
                dst.Stack += src.Stack;
                return 0;
            }
            int excess = dst.Stack + src.Stack - MaxStack;
            dst.Stack = MaxStack;
            return excess;
        }

        /// <summary>数背包里同 ID+同质量总数(修复后算法的核心)。</summary>
        public int CountOf(string id, int quality)
            => Slots.Where(s => s != null && s.Id == id && s.Quality == quality).Sum(s => s!.Stack);
    }

    /// <summary>旧算法(有 bug):before - item.Stack。放进空格时 Stack 不减 → 算成 0。</summary>
    public static int OldAddCounted(FakeInventory inv, FakeItem item)
    {
        int before = item.Stack;
        inv.AddItem(item);
        return before - item.Stack;
    }

    /// <summary>新算法(修复后):数背包前后差(同 ID+同质量)。所有场景精确。</summary>
    public static int NewAddCounted(FakeInventory inv, FakeItem item)
    {
        int before = inv.CountOf(item.Id, item.Quality);
        inv.AddItem(item);
        return inv.CountOf(item.Id, item.Quality) - before;
    }
}
