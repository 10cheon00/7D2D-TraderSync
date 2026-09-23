// Minimal game API doubles. The transaction/staging/pricing code under test is production code.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TraderSync.Transactions;

public class ItemClass
{
    public static readonly Dictionary<int, ItemClass> All = new Dictionary<int, ItemClass>();
    public int MaxCount = 100, EconomicBundleSize = 1;
    public float EconomicValue = 10, EconomicSellScale = 1, TraderQualityMinMod, TraderQualityMaxMod;
    public bool SellableToTrader = true, HasSubItems;
    public object ItemTags;
    public ItemAction[] Actions = Array.Empty<ItemAction>();
    public bool IsBlock() => false;
    public static ItemValue GetItem(string name) => new ItemValue { type = 1 };
}
public class ItemValue
{
    public int type, Meta, Quality = 1, SelectedAmmoTypeIndex;
    public bool HasQuality;
    public float PercentUsesLeft = 1;
    public ItemValue[] Modifications = Array.Empty<ItemValue>();
    public ItemClass ItemClass => ItemClass.All[type];
    public bool IsEmpty() => type == 0;
    public ItemValue Clone() => (ItemValue)MemberwiseClone();
    public void Write(BinaryWriter w) { w.Write(type); w.Write(Meta); w.Write(Quality); }
}
public class ItemStack
{
    public ItemValue itemValue;
    public int count;
    public ItemStack(ItemValue item, int qty) { itemValue = item; count = qty; }
    public static ItemStack Empty => new ItemStack(new ItemValue(), 0);
    public bool IsEmpty() => count <= 0 || itemValue.type == 0;
    public ItemStack Clone() => new ItemStack(itemValue.Clone(), count);
    public static ItemStack[] Clone(ItemStack[] items) => items.Select(x => x.Clone()).ToArray();
    public bool CanMoveTo(XUiC_ItemStack.StackLocationTypes location) => true;
    public bool CanStackPartlyWith(ItemStack other, out int amount)
    { amount = Math.Min(other.count, itemValue.ItemClass.MaxCount - count); return itemValue.type == other.itemValue.type && amount > 0; }
}
public class XUiC_ItemStack { public enum StackLocationTypes { Backpack, ToolBelt } }
public class ItemAction {}
public class ItemActionRanged : ItemAction { public string[] MagazineItemNames; }
public class ItemActionTextureBlock : ItemActionRanged {}
public static class GameUtils
{
    public static void WriteItemStack(BinaryWriter w, ItemStack[] slots)
    { w.Write(slots.Length); foreach (var s in slots) { s.itemValue.Write(w); w.Write(s.count); } }
    public static ItemStack[] ReadItemStack(BinaryReader r)
    {
        var slots = new ItemStack[r.ReadInt32()];
        for (int i = 0; i < slots.Length; i++) slots[i] = new ItemStack(new ItemValue
        { type = r.ReadInt32(), Meta = r.ReadInt32(), Quality = r.ReadInt32() }, r.ReadInt32());
        return slots;
    }
}
public class Bag
{
    public ItemStack[] Slots;
    public ItemStack[] GetSlots() => Slots;
    public void SetSlots(ItemStack[] slots) => Slots = slots;
    public Bag Clone() => new Bag { Slots = ItemStack.Clone(Slots) };
}
public class Inventory
{
    public bool FailNextSet;
    public int PUBLIC_SLOTS = 2;
    public ItemStack[] Slots = { ItemStack.Empty, ItemStack.Empty, ItemStack.Empty };
    public ItemStack[] GetSlots() => Slots;
    public void SetSlots(ItemStack[] slots)
    {
        if (FailNextSet) { FailNextSet = false; throw new InvalidOperationException("Injected host inventory callback failure"); }
        Slots = slots;
    }
}
public class Entity { public int entityId; }
public class EntityPlayer : Entity
{
    public bool Dead;
    public Inventory inventory = new Inventory();
    public Bag bag = new Bag();
    public Progression Progression = new Progression();
    public bool IsDead() => Dead;
}
public class Progression { public int Level = 1; }
public class EntityPlayerLocal : EntityPlayer {}
public class EntityTrader : Entity
{
    public TraderData TraderData = new TraderData();
    public bool Open = true;
    public bool CanLockOnServer(int id, object context, ushort channel) => Open;
}
public class TraderInfo
{
    public static string CurrencyItem = "coin";
    public static int TraderBuyLimit = 1;
    public static float BuyMarkup = 1, SellMarkdown = 1, QualityMinMod = 1, QualityMaxMod = 1;
    public float OverrideBuyMarkup = -1, OverrideSellMarkdown = -1;
    public class TierItemGroup { public int minLevel = -1, maxLevel = -1; }
    public List<TierItemGroup> TierItemGroups = new List<TierItemGroup>();
}
public class TraderData
{
    public class Entry
    {
        public ItemStack Item; public sbyte Markup; public bool AddedByPlayer;
        public Entry(ItemStack item, sbyte markup = 0, bool added = false) { Item = item; Markup = markup; AddedByPlayer = added; }
    }
    public List<Entry> PrimaryInventory = new List<Entry>();
    public List<ItemStack[]> TierItemGroups = new List<ItemStack[]>();
    public int AvailableMoney;
    public TraderInfo TraderInfo = new TraderInfo();
    public TraderData Clone() => new TraderData
    {
        AvailableMoney = AvailableMoney, TraderInfo = TraderInfo,
        PrimaryInventory = PrimaryInventory.Select(e => new Entry(e.Item.Clone(), e.Markup, e.AddedByPlayer)).ToList(),
        TierItemGroups = TierItemGroups.Select(ItemStack.Clone).ToList()
    };
    public void PrimaryInventoryAdd(Entry entry) => PrimaryInventory.Add(entry);
    public int GetPrimaryItemCount(ItemValue value) => PrimaryInventory.Where(e => e.Item.itemValue.type == value.type).Sum(e => e.Item.count);
    public void Write(BinaryWriter w)
    {
        w.Write(AvailableMoney); w.Write(PrimaryInventory.Count);
        foreach (var e in PrimaryInventory) { GameUtils.WriteItemStack(w, new[] { e.Item }); w.Write(e.Markup); w.Write(e.AddedByPlayer); }
        w.Write(TierItemGroups.Count); foreach (var g in TierItemGroups) GameUtils.WriteItemStack(w, g);
    }
}
public class PlayerDataFile { public Bag bag; public ItemStack[] inventory; public bool bModifiedSinceLastSave; }
public class ClientInfo
{
    public int entityId;
    public PlayerDataFile latestPlayerData;
    public ConcurrentQueue<TradeResultHolder> Results = new ConcurrentQueue<TradeResultHolder>();
    public void SendPackage(TraderSync.Transactions.NetPackageTraderTransactionResult packet) => Results.Enqueue(new TradeResultHolder { Value = packet.Value });
}
public class TradeResultHolder { internal TradeResult Value; }
public class World
{
    public Dictionary<int, Entity> Entities = new Dictionary<int, Entity>();
    public Entity GetEntity(int id) => Entities.TryGetValue(id, out var entity) ? entity : null;
}
public class GameManager { public static GameManager Instance = new GameManager(); public World World; }
public class ConnectionManager { public bool IsServer = true; }
public class SingletonMonoBehaviour<T> where T : new() { public static T Instance = new T(); }
public static class NetPackageManager { public static T GetPackage<T>() where T : new() => new T(); }
public static class Log { public static void Error(string text) => Console.WriteLine(text); }
public class Block
{
    public static Dictionary<int, Block> list = new Dictionary<int, Block>();
    public int EconomicBundleSize = 1; public float EconomicValue = 10, EconomicSellScale = 1; public bool SellableToTrader = true;
}
public enum PassiveEffects { EconomicValue, BarteringBuying, BarteringSelling, SecretStash }
public static class EffectManager
{
    public static float GetValue(PassiveEffects effect, ItemValue item, float value, EntityPlayer player, object other = null, object tags = null) => value;
}
namespace UnityEngine
{
    public static class Mathf
    {
        public static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0, 1);
        public static int CeilToInt(float value) => (int)Math.Ceiling(value);
    }
}
namespace SandboxOptions
{
    public enum SandboxOptions { TraderBuyPrices, TraderSellPrices }
    public static class SandboxOptionManager { public static float GetFloat(SandboxOptions key) => 1; }
}
namespace TraderSync
{
    internal static class TraderStockSync
    {
        internal static byte[] Serialize(TraderData data) => TradeWire.Encode(data.Write);
        internal static void PublishIfChanged(EntityTrader trader) {}
        internal static void SendCurrentTo(EntityTrader trader, int playerId) {}
    }
}
namespace TraderSync.Transactions
{
    public class NetPackageTraderTransactionResult
    {
        internal TradeResult Value;
        internal NetPackageTraderTransactionResult Setup(TradeResult result) { Value = result; return this; }
    }
    internal static class TradeClient
    {
        internal static bool ApplyingResult;
        internal static TradeResult LastResult;
        internal static void Receive(TradeResult result) => LastResult = result;
    }
}
