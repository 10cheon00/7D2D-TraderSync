using System;
using System.Linq;

namespace TraderSync.Transactions
{
    // Only mutates staged clones; failure leaves live inventory untouched.
    internal sealed class TradeInventory
    {
        internal readonly ItemStack[] Bag;
        internal readonly ItemStack[] Belt;
        private readonly int beltSlots;
        internal TradeInventory(ItemStack[] bag, ItemStack[] belt, int publicBeltSlots)
        { Bag = ItemStack.Clone(bag); Belt = ItemStack.Clone(belt); beltSlots = Math.Min(belt.Length, publicBeltSlots); }

        internal void RemoveCurrency(int count)
        {
            int type = ItemClass.GetItem(TraderInfo.CurrencyItem).type;
            foreach (var slots in new[] { Bag, Belt })
            {
                for (int i = 0; i < slots.Length && count > 0; i++)
                {
                    if (slots == Belt && i >= beltSlots) break;
                    if (slots[i].IsEmpty() || slots[i].itemValue.type != type) continue;
                    int taken = Math.Min(count, slots[i].count);
                    slots[i].count -= taken; count -= taken;
                    if (slots[i].count == 0) slots[i] = ItemStack.Empty;
                }
            }
            if (count != 0) throw new TradeRejectedException("돈이 부족합니다.");
        }

        internal void Add(ItemStack stack)
        {
            int remaining = stack.count;
            // Respect stack metadata, quality and modifications through CanStackPartlyWith.
            foreach (var slots in new[] { Bag, Belt })
            {
                var location = slots == Bag ? XUiC_ItemStack.StackLocationTypes.Backpack : XUiC_ItemStack.StackLocationTypes.ToolBelt;
                if (!stack.CanMoveTo(location)) continue;
                int length = slots == Belt ? beltSlots : slots.Length;
                for (int i = 0; i < length && remaining > 0; i++)
                {
                    if (slots[i].IsEmpty() || !TradeWire.Encode(w => slots[i].itemValue.Write(w))
                        .SequenceEqual(TradeWire.Encode(w => stack.itemValue.Write(w)))) continue;
                    var part = new ItemStack(stack.itemValue, remaining);
                    if (!slots[i].CanStackPartlyWith(part, out int amount)) continue;
                    slots[i].count += amount; remaining -= amount;
                }
                for (int i = 0; i < length && remaining > 0; i++)
                {
                    if (!slots[i].IsEmpty()) continue;
                    int amount = Math.Min(remaining, stack.itemValue.ItemClass.MaxCount);
                    if (amount <= 0) throw new TradeRejectedException("잘못된 최대 스택 수량입니다.");
                    slots[i] = new ItemStack(stack.itemValue.Clone(), amount);
                    remaining -= amount;
                }
            }
            if (remaining != 0) throw new TradeRejectedException("인벤토리 공간이 부족합니다.");
        }

        internal void ReturnAmmo(ItemStack sold)
        {
            if (sold.itemValue.Meta <= 0) return;
            foreach (var action in sold.itemValue.ItemClass.Actions)
            {
                if (action is ItemActionRanged ranged && !(action is ItemActionTextureBlock)
                    && ranged.MagazineItemNames != null
                    && sold.itemValue.SelectedAmmoTypeIndex < ranged.MagazineItemNames.Length)
                {
                    Add(new ItemStack(ItemClass.GetItem(ranged.MagazineItemNames[sold.itemValue.SelectedAmmoTypeIndex]), sold.itemValue.Meta));
                    sold.itemValue.Meta = 0;
                }
            }
        }
    }

    internal sealed class TradeRejectedException : Exception
    { internal TradeRejectedException(string message) : base(message) {} }
}
