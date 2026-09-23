using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TraderSync;
using TraderSync.Transactions;

static class Program
{
    static EntityTrader trader;
    static ClientInfo a, b;
    static int checks;
    static ItemStack Stack(int type, int qty) => new ItemStack(new ItemValue { type = type }, qty);
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); Console.WriteLine("PASS: " + message); checks++; }
    static void Reset(int stock = 1, int money = 100)
    {
        TradeServer.Clear(); TraderViewers.Clear();
        ItemClass.All[1] = new ItemClass { EconomicValue = 1, MaxCount = 10000 };
        ItemClass.All[2] = new ItemClass(); ItemClass.All[3] = new ItemClass();
        trader = new EntityTrader { entityId = 100 };
        trader.TraderData.PrimaryInventory.Add(new TraderData.Entry(Stack(2, stock)));
        GameManager.Instance.World = new World();
        GameManager.Instance.World.Entities[100] = trader;
        a = Player(1, money); b = Player(2, money);
    }
    static ClientInfo Player(int id, int money)
    {
        var player = new EntityPlayer { entityId = id };
        GameManager.Instance.World.Entities[id] = player;
        TraderViewers.Add(100, id);
        return new ClientInfo { entityId = id, latestPlayerData = new PlayerDataFile
        { bag = new Bag { Slots = new[] { Stack(1, money), ItemStack.Empty, ItemStack.Empty } }, inventory = ItemStack.Clone(player.inventory.Slots) } };
    }
    static TradeRequest Request(ClientInfo c, long sequence = 1, bool buy = true, int count = 1)
    {
        var item = buy ? trader.TraderData.PrimaryInventory[0].Item : c.latestPlayerData.bag.Slots[1];
        return new TradeRequest { Sequence = sequence, TraderId = 100, Buy = buy, Slot = buy ? 0 : 1, Count = count,
            QuotedPrice = count * 10, StockHash = TradeWire.Hash(TraderStockSync.Serialize(trader.TraderData)),
            InventoryHash = TradeWire.Hash(TradeWire.Inventory(c.latestPlayerData.bag.Slots, c.latestPlayerData.inventory)),
            ItemHash = TradeWire.ItemHash(item.itemValue) };
    }
    static TradeResult Run(ClientInfo c, TradeRequest r)
    { TradeServer.Handle(r, c); if (!c.Results.TryDequeue(out var result)) return null; return result.Value; }
    static string State(ClientInfo c) => TradeWire.Hash(TradeWire.Inventory(c.latestPlayerData.bag.Slots, c.latestPlayerData.inventory))
        + TradeWire.Hash(TraderStockSync.Serialize(trader.TraderData));
    static int Count(ClientInfo c, int type) => c.latestPlayerData.bag.Slots.Concat(c.latestPlayerData.inventory)
        .Where(x => x.itemValue.type == type).Sum(x => x.count);

    static void Main()
    {
        Reset(); var ar = Request(a); var br = Request(b);
        var results = new TradeResult[2];
        Parallel.Invoke(() => results[0] = Run(a, ar), () => results[1] = Run(b, br));
        Check(results.Count(x => x.Success) == 1 && trader.TraderData.PrimaryInventory.Count == 0,
            "Concurrent last-item purchases: exactly one commit");
        Check(Count(a, 2) + Count(b, 2) == 1 && Count(a, 1) + Count(b, 1) == 190,
            "Only the winner pays and receives the item");

        Reset(5); ar = Request(a); var result1 = Run(a, ar); var state = State(a);
        var duplicate = Run(a, ar);
        Check(ReferenceEquals(result1, duplicate) && State(a) == state, "Duplicate request returns cached result without settlement twice");
        Check(TradeServer.AwaitingAck(a), "Committed player inventory remains protected until ACK");
        var lateSave = new PlayerDataFile { bag = new Bag { Slots = new[] { Stack(1, 100), ItemStack.Empty, ItemStack.Empty } }, inventory = ItemStack.Clone(a.latestPlayerData.inventory) };
        TradeServer.PreserveCommittedInventory(a, lateSave);
        Check(TradeWire.Hash(TradeWire.Inventory(lateSave.bag.Slots, lateSave.inventory))
            == TradeWire.Hash(TradeWire.Inventory(a.latestPlayerData.bag.Slots, a.latestPlayerData.inventory)),
            "Late full player save preserves committed goods and currency before ACK");
        TradeServer.Acknowledge(a, 99);
        Check(TradeServer.AwaitingAck(a), "Wrong ACK cannot release protection");
        Check(Run(a, Request(a, 2)) == null && State(a) == state, "New transaction waits for previous settlement ACK");
        TradeServer.Acknowledge(a, 1);
        Check(!TradeServer.AwaitingAck(a) && Run(a, Request(a, 2)).Success, "Matching ACK permits the next transaction");
        state = State(a);
        Check(Run(a, ar) == null && State(a) == state, "Old request stays rejected after cache advances");

        Reset(1, 9); state = State(a);
        Check(!Run(a, Request(a)).Success && State(a) == state, "Insufficient currency leaves stock and inventory unchanged");
        Reset(1); a.latestPlayerData.bag.Slots = new[] { Stack(1, 100), Stack(3, 100), Stack(3, 100) };
        a.latestPlayerData.inventory = new[] { Stack(3, 100), Stack(3, 100), ItemStack.Empty }; state = State(a);
        Check(!Run(a, Request(a)).Success && State(a) == state, "No capacity rolls back staged currency removal; dummy slot is not used");

        Reset(); ar = Request(a); ar.QuotedPrice = 1; state = State(a);
        Check(!Run(a, ar).Success && State(a) == state, "Server rejects forged price");
        Reset(); ar = Request(a); ar.InventoryHash = "stale"; state = State(a);
        Check(!Run(a, ar).Success && State(a) == state, "Stale personal inventory cannot be settled");
        Reset(); ar = Request(a); ar.ItemHash = TradeWire.ItemHash(new ItemValue { type = 3 }); state = State(a);
        Check(!Run(a, ar).Success && State(a) == state, "Slot reuse cannot substitute a different item");
        Reset(); ar = Request(a); TraderViewers.Remove(100, a.entityId); state = State(a);
        Check(!Run(a, ar).Success && State(a) == state, "Non-viewer cannot trade");
        Reset(); ar = Request(a); trader.Open = false; state = State(a);
        Check(!Run(a, ar).Success && State(a) == state, "Closed trader rejects transaction");
        Reset(); ar = Request(a); ar.Count = -1; state = State(a);
        Check(!Run(a, ar).Success && State(a) == state, "Negative quantity is rejected without mutations");

        Reset(99); a.latestPlayerData.bag.Slots[1] = Stack(2, 1); b.latestPlayerData.bag.Slots[1] = Stack(2, 1);
        ar = Request(a, buy: false); br = Request(b, buy: false);
        Parallel.Invoke(() => results[0] = Run(a, ar), () => results[1] = Run(b, br));
        Check(results.Count(x => x.Success) == 1 && trader.TraderData.GetPrimaryItemCount(new ItemValue { type = 2 }) == 100,
            "Concurrent selling cannot exceed trader purchase limit");
        Check(Count(a, 1) + Count(b, 1) == 210 && Count(a, 2) + Count(b, 2) == 1,
            "Only committed seller receives money and loses goods");

        Reset(5); a.latestPlayerData.bag.Slots[1] = Stack(2, 2); ar = Request(a, buy: false, count: 1);
        ItemClass.All[2].SellableToTrader = false; state = State(a);
        Check(!Run(a, ar).Success && State(a) == state, "Unsellable item leaves state unchanged");
        Reset(5); a.latestPlayerData.bag.Slots[1] = Stack(2, 2);
        Check(Run(a, Request(a, buy: false, count: 2)).Success && Count(a, 2) == 0 && Count(a, 1) == 120,
            "Sale commits goods and payment together");

        Reset(5); ar = Request(a); Run(a, ar); var protectedState = State(a);
        // Disconnection before ACK must leave the committed PlayerDataFile available for saving.
        TraderViewers.RemovePlayer(a.entityId);
        Check(State(a) == protectedState && TradeServer.AwaitingAck(a), "Disconnect does not roll back a committed transaction");
        TradeServer.Clear();
        Check(!TradeServer.AwaitingAck(a), "Session clear releases prior ledger state");

        var ledger = new TransactionLedger<string>(); int calls = 0;
        object connection = new object();
        Parallel.For(0, 100, _ => ledger.Execute(connection, 1, () => { Interlocked.Increment(ref calls); return "committed"; }, _ => true));
        Check(calls == 1, "100 concurrent duplicate requests execute once");

        Reset(1); ar = Request(a);
        var host = new EntityPlayerLocal { entityId = 1, bag = a.latestPlayerData.bag.Clone() };
        host.inventory.SetSlots(ItemStack.Clone(a.latestPlayerData.inventory));
        GameManager.Instance.World.Entities[1] = host;
        TradeServer.Handle(ar, null, host);
        Check(TradeClient.LastResult.Success && trader.TraderData.PrimaryInventory.Count == 0
            && host.bag.Slots.Where(x => x.itemValue.type == 1).Sum(x => x.count) == 90,
            "Host uses the same transactional purchase path");
        Reset(1); ar = Request(a);
        host = new EntityPlayerLocal { entityId = 1, bag = a.latestPlayerData.bag.Clone() };
        host.inventory.SetSlots(ItemStack.Clone(a.latestPlayerData.inventory));
        GameManager.Instance.World.Entities[1] = host;
        var beforeHost = TradeWire.Hash(TradeWire.Inventory(host.bag.Slots, host.inventory.Slots));
        host.inventory.FailNextSet = true;
        TradeServer.Handle(ar, null, host);
        Check(!TradeClient.LastResult.Success && trader.TraderData.PrimaryInventory[0].Item.count == 1
            && TradeWire.Hash(TradeWire.Inventory(host.bag.Slots, host.inventory.Slots)) == beforeHost,
            "Host callback failure restores inventory and leaves trader stock unchanged");
        Console.WriteLine($"{checks} transaction checks passed.");
    }
}
