using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TraderSync;

public class ItemValue
{
    public int Type, Quality;
    public void Write(BinaryWriter w) { w.Write(Type); w.Write(Quality); }
}
public class ItemStack
{
    public ItemValue itemValue = new ItemValue();
    public int count;
    public bool IsEmpty() => count == 0;
    public ItemStack Clone() => new ItemStack { itemValue = new ItemValue { Type = itemValue.Type, Quality = itemValue.Quality }, count = count };
}
public class PlayerInventory { public int CountAvailableSpaceForItem(ItemValue item) => 1000; }
public class XUi { public PlayerInventory PlayerInventory = new PlayerInventory(); }
public class XUiC_TraderItemEntry
{
    public ItemStack item;
    public ItemStack Item => item;
    public int SlotIndex;
    public bool IsSelected;
    public XUi xui;
    public object windowGroup;
    public XUiC_ItemInfoWindow InfoWindow;
    public XUiC_TraderWindow TraderWindow;
}
public class Counter { public int Count = 1, MaxCount = 100, Step = 1; public void ForceTextRefresh() {} }
public class XUiC_ItemActionList
{
    public enum ItemActionListTypes { Trader }
    public XUiC_TraderItemEntry Target;
    public void SetCraftingActionList(ItemActionListTypes type, XUiC_TraderItemEntry entry) => Target = entry;
    public void RefreshActionList() {}
}
public class XUiC_ItemInfoWindow
{
    public XUiC_TraderItemEntry selectedTraderItemStack;
    public ItemStack itemStack;
    public Counter BuySellCounter = new Counter();
    public XUiC_ItemActionList traderActionItemList = new XUiC_ItemActionList();
    public string ActiveTab = "stats";
    public int ClearCalls;
    public void RefreshBindings() {}
}
public class XUiC_TraderItemList
{
    public XUiC_TraderItemEntry selectedEntry;
    public int selectedIndex = -1;
    public ItemStack CurrentItem;
    public List<ItemStack> items = new List<ItemStack>();
    public List<int> indexList = new List<int>();
    public List<XUiC_TraderItemEntry> entryList = new List<XUiC_TraderItemEntry>();
    public int Page, Length = 2;
    public XUiC_ItemInfoWindow InfoWindow = new XUiC_ItemInfoWindow();
    public XUiC_TraderItemEntry SelectedEntry => selectedEntry;
    public void ClearSelection()
    {
        // Models the Harmony prefix, as well as the game's destructive clear behavior.
        if (TraderSelectionRefresh.IsRefreshing(this)) return;
        selectedEntry = null; selectedIndex = -1; CurrentItem = null;
        InfoWindow.selectedTraderItemStack = null; InfoWindow.ClearCalls++;
    }
}
public class XUiC_TraderWindow
{
    public XUiC_TraderItemList List = new XUiC_TraderItemList();
    public List<ItemStack> Incoming = new List<ItemStack>();
    public string Search = "unchanged", Category = "tools";
    public bool CompletedTransaction;
    public T GetChildByType<T>() where T : class => List as T;
    public void RefreshTraderItems()
    {
        List.items = Incoming.ToList(); List.indexList = Enumerable.Range(0, Incoming.Count).ToList();
        for (int i = 0; i < List.entryList.Count; i++)
        {
            int n = i + List.Page * List.Length;
            List.entryList[i].item = n < Incoming.Count ? Incoming[n] : null;
            List.entryList[i].SlotIndex = n;
        }
        // Vanilla SetItems clears selection when the previous visual row changes item.
        if (List.CurrentItem == null || List.SelectedEntry?.Item == null
            || List.CurrentItem.itemValue.Type != List.SelectedEntry.Item.itemValue.Type) List.ClearSelection();
    }
}
