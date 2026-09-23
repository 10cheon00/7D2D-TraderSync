using System;
using System.Linq;
using TraderSync;

static class Program
{
    static int checks;
    static ItemStack Item(int type, int count = 10, int quality = 1) => new ItemStack
        { count = count, itemValue = new ItemValue { Type = type, Quality = quality } };
    static XUiC_TraderWindow Window(int page, int selectedRow, params ItemStack[] items)
    {
        var window = new XUiC_TraderWindow { Incoming = items.ToList() };
        var list = window.List; list.Page = page;
        for (int i = 0; i < list.Length; i++) list.entryList.Add(new XUiC_TraderItemEntry
            { xui = new XUi(), InfoWindow = list.InfoWindow, TraderWindow = window });
        window.RefreshTraderItems(); list.InfoWindow.ClearCalls = 0;
        var selected = list.entryList[selectedRow];
        selected.IsSelected = true; list.selectedEntry = selected; list.selectedIndex = selectedRow;
        list.CurrentItem = selected.Item; list.InfoWindow.selectedTraderItemStack = selected;
        list.InfoWindow.itemStack = selected.Item.Clone(); list.InfoWindow.BuySellCounter.Count = 7;
        return window;
    }
    static void Check(bool condition, string name)
    { if (!condition) throw new Exception(name); Console.WriteLine("PASS: " + name); checks++; }
    static void Main()
    {
        var w = Window(0, 1, Item(1), Item(2));
        w.Incoming = new[] { Item(1, 9), Item(2), Item(3) }.ToList();
        TraderSelectionRefresh.Refresh(w);
        Check(w.List.InfoWindow.ClearCalls == 0 && w.List.InfoWindow.selectedTraderItemStack.Item.itemValue.Type == 2,
            "Other player's buy/sell does not dismiss inspected item");
        Check(w.List.InfoWindow.BuySellCounter.Count == 7 && w.List.InfoWindow.ActiveTab == "stats"
            && w.Search == "unchanged" && w.Category == "tools" && w.List.Page == 0 && !w.CompletedTransaction,
            "Quantity, details tab, search, category and page remain unchanged");
        w.Incoming = new[] { Item(2), Item(3) }.ToList();
        TraderSelectionRefresh.Refresh(w);
        Check(w.List.selectedIndex == 0 && w.List.InfoWindow.selectedTraderItemStack.SlotIndex == 0
            && w.List.InfoWindow.traderActionItemList.Target.SlotIndex == 0 && w.List.InfoWindow.ClearCalls == 0,
            "Deleting a preceding row preserves identity and updates purchase slot");
        w.Incoming[0] = Item(2, 4); TraderSelectionRefresh.Refresh(w);
        Check(w.List.InfoWindow.itemStack.count == 4 && w.List.InfoWindow.BuySellCounter.Count == 4
            && w.List.InfoWindow.BuySellCounter.MaxCount == 4 && w.List.InfoWindow.ClearCalls == 0,
            "Stock reduction only clamps requested quantity to available stock");
        w.Incoming = new[] { Item(3) }.ToList(); TraderSelectionRefresh.Refresh(w);
        Check(w.List.InfoWindow.selectedTraderItemStack == null && w.List.InfoWindow.ClearCalls == 1,
            "Only removal of inspected item clears its details");

        w = Window(1, 0, Item(1), Item(2), Item(3), Item(4));
        w.Incoming = new[] { Item(2), Item(3), Item(4) }.ToList(); TraderSelectionRefresh.Refresh(w);
        Check(w.List.Page == 1 && w.List.InfoWindow.selectedTraderItemStack.Item.itemValue.Type == 3
            && w.List.InfoWindow.selectedTraderItemStack.SlotIndex == 1 && w.List.SelectedEntry == null,
            "Inspected item moving off-page retains details without changing page or highlighting wrong row");
        w.Incoming[1] = Item(3, 8); TraderSelectionRefresh.Refresh(w);
        Check(w.List.InfoWindow.selectedTraderItemStack.Item.count == 8 && w.List.InfoWindow.ClearCalls == 0,
            "Repeated sync keeps off-page detail controller current");

        w = Window(0, 0, Item(1, quality: 2), Item(1, quality: 5));
        w.Incoming = new[] { Item(1, quality: 5) }.ToList(); TraderSelectionRefresh.Refresh(w);
        Check(w.List.InfoWindow.selectedTraderItemStack == null, "Different quality item is not substituted for sold-out selection");
        w = Window(0, 1, Item(1), Item(2));
        w.List.InfoWindow.selectedTraderItemStack = null; // Player is inspecting a backpack item instead.
        w.Incoming = new[] { Item(1) }.ToList(); TraderSelectionRefresh.Refresh(w);
        Check(w.List.InfoWindow.ClearCalls == 0, "Remote stock sync does not clear backpack inspection");
        w = Window(0, 1, Item(2), Item(2));
        w.Incoming = new[] { Item(2) }.ToList(); TraderSelectionRefresh.Refresh(w);
        Check(w.List.InfoWindow.selectedTraderItemStack?.Item.itemValue.Type == 2
            && w.List.InfoWindow.selectedTraderItemStack.SlotIndex == 0 && w.List.InfoWindow.ClearCalls == 0,
            "Identical stack removal keeps inspection bound to the remaining identical item");
        Console.WriteLine($"{checks} selection checks passed.");
    }
}
