using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TraderSync
{
    // A detail-only controller keeps an inspected item addressable even if deleting
    // a preceding row moves it onto another page. It is not a rendered list row.
    internal sealed class TraderDetailEntry : XUiC_TraderItemEntry { }

    internal static class TraderSelectionRefresh
    {
        [ThreadStatic] private static XUiC_TraderItemList refreshing;
        internal static bool IsRefreshing(XUiC_TraderItemList list) => ReferenceEquals(refreshing, list);

        private static string Key(ItemStack item)
        {
            if (item == null || item.IsEmpty()) return null;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                // Include quality, durability, mods and metadata, but exclude quantity.
                item.itemValue.Write(writer);
                writer.Flush();
                return Convert.ToBase64String(stream.ToArray());
            }
        }

        internal static void Refresh(XUiC_TraderWindow window)
        {
            var list = window.GetChildByType<XUiC_TraderItemList>();
            if (list == null) { window.RefreshTraderItems(); return; }
            var info = list.InfoWindow;
            var selected = list.SelectedEntry;
            var inspected = info?.selectedTraderItemStack;
            // The detail controller may be off-page while list.SelectedEntry is null.
            var source = inspected ?? selected;
            string key = Key(source?.Item);
            int count = info?.BuySellCounter?.Count ?? 0;
            var oldKeys = list.items.Select(Key).ToArray();
            int oldPosition = source == null ? -1 : list.indexList.IndexOf(source.SlotIndex);
            int ordinal = oldPosition < 0 ? -1 : oldKeys.Take(oldPosition).Count(k => k == key);
            bool ownsDetails = inspected != null;

            var previous = refreshing;
            refreshing = list;
            try
            {
                // The vanilla SetItems would clear details when a row shifts. During
                // this refresh only, defer that decision until matching by item identity.
                window.RefreshTraderItems();
            }
            finally { refreshing = previous; }

            if (key == null) return; // E.g. inspecting a backpack item: do not touch it.
            var matches = Enumerable.Range(0, list.items.Count)
                .Where(i => Key(list.items[i]) == key).ToArray();
            int position = -1;
            if (matches.Length > 0)
                position = matches[Math.Min(Math.Max(ordinal, 0), matches.Length - 1)];
            // No persistent row IDs exist. Identical item-value stacks are equivalent
            // purchase targets; retain the occurrence when possible, otherwise use the
            // remaining identical occurrence. Never substitute a different item value.

            if (selected != null) selected.IsSelected = false;
            list.selectedEntry = null;
            list.selectedIndex = -1;
            list.CurrentItem = null;
            if (position < 0)
            {
                if (ownsDetails) list.ClearSelection();
                return;
            }

            var item = list.items[position];
            int slot = list.indexList[position];
            int row = position - list.Page * list.Length;
            if (row >= 0 && row < list.entryList.Count)
            {
                var entry = list.entryList[row];
                entry.IsSelected = true;
                list.selectedEntry = entry;
                list.selectedIndex = row;
                list.CurrentItem = entry.Item;
            }
            if (!ownsDetails) return;

            var detail = inspected as TraderDetailEntry ?? new TraderDetailEntry
            {
                xui = source.xui, windowGroup = source.windowGroup,
                InfoWindow = info, TraderWindow = window
            };
            detail.item = item;
            detail.SlotIndex = slot;
            info.selectedTraderItemStack = detail;
            // Do not call SelectedEntry's setter or SetItemStack/SetInfo: they reset
            // quantity, comparison state and action UI. Update their backing data only.
            info.itemStack = item.Clone();
            if (!ReferenceEquals(inspected, detail))
                info.traderActionItemList.SetCraftingActionList(XUiC_ItemActionList.ItemActionListTypes.Trader, detail);
            var counter = info.BuySellCounter;
            int step = Math.Max(1, counter.Step);
            int available = Math.Min(item.count, source.xui.PlayerInventory.CountAvailableSpaceForItem(item.itemValue));
            counter.MaxCount = available / step * step;
            counter.Count = Math.Min(count, counter.MaxCount);
            counter.ForceTextRefresh();
            info.traderActionItemList.RefreshActionList();
            info.RefreshBindings();
        }
    }
}
