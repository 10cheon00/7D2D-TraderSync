using System;
using System.IO;
using System.Linq;
using TraderSync;

static class Program
{
    static void Check(bool ok, string scenario)
    {
        if (!ok) throw new Exception(scenario);
        Console.WriteLine("PASS: " + scenario);
    }

    static void Main()
    {
        var connection = SingletonMonoBehaviour<ConnectionManager>.Instance;
        connection.IsServer = true;
        var trader = new EntityTrader { entityId = 100 };
        trader.TraderData.Count = 10;
        var host = new EntityPlayerLocal { entityId = 1 };
        var world = new World { LocalPlayer = host };
        GameManager.Instance.World = world;
        world.Entities[100] = trader;
        world.Entities[1] = host;
        foreach (int id in new[] { 2, 3, 4 }) world.Entities[id] = new EntityPlayer { entityId = id };
        LocalPlayerUI.Current = new LocalPlayerUI();
        LocalPlayerUI.Current.xui.Trader.Trader = trader;
        foreach (int id in new[] { 1, 2, 3 }) TraderViewers.Add(100, id);
        TraderViewers.Add(200, 4);
        TraderStockSync.RememberIfMissing(trader);
        TraderStockSync.PublishIfChanged(trader, 2);
        Check(connection.Sent.Count == 0, "Unchanged inventory does not broadcast");

        trader.TraderData.Count = 9;
        TraderStockSync.PublishIfChanged(trader, 2);
        Check(connection.Sent.Count == 1 && connection.Sent[0].Recipient == 3,
            "Remote trade reaches same-trader viewer only, excluding originator");
        Check(LocalPlayerUI.Current.xui.Trader.TraderWindowGroup.Window.Refreshes == 1,
            "Host viewer refreshes without a network packet");

        var packet = connection.Sent[0].Packet;
        byte[] encoded;
        using (var stream = new MemoryStream())
        {
            packet.write(new PooledBinaryWriter(stream));
            encoded = stream.ToArray();
        }
        Check(encoded.Length == packet.GetLength(), "Wire length matches serialized packet");
        var incoming = new NetPackageTraderSyncStock();
        using (var stream = new MemoryStream(encoded))
        using (var reader = new PooledBinaryReader(stream))
        {
            reader.ReadUInt16(); // Game package dispatch consumes the ID.
            incoming.read(reader);
        }
        connection.IsServer = false;
        trader.TraderData.Count = 10;
        incoming.ProcessPackage(world, GameManager.Instance);
        Check(trader.TraderData.Count == 9 && connection.Sent.Count == 1,
            "Packet round trip applies stock without echo upload");
        var window = LocalPlayerUI.Current.xui.Trader.TraderWindowGroup.Window;
        Check(window.Items.Clears == 0, "Remote refresh delegates to non-destructive selection refresh");
        TraderStockSync.Receive(world, 100, 2, new TraderData { Count = 8 });
        incoming.ProcessPackage(world, GameManager.Instance);
        Check(trader.TraderData.Count == 8, "Old revision cannot overwrite newer stock");
        LocalPlayerUI.Current.windowManager.Open = false;
        TraderStockSync.Receive(world, 100, 3, new TraderData { Count = 7 });
        Check(trader.TraderData.Count == 8, "Late packet after window close is ignored");
        LocalPlayerUI.Current.windowManager.Open = true;
        LocalPlayerUI.Current.xui.Trader.Trader = new EntityTrader { entityId = 200 };
        TraderStockSync.Receive(world, 100, 3, new TraderData { Count = 7 });
        Check(trader.TraderData.Count == 8, "Packet for previous trader is ignored");

        connection.IsServer = true;
        LocalPlayerUI.Current.xui.Trader.Trader = trader;
        connection.Sent.Clear();
        TraderStockSync.PublishIfChanged(trader, 1);
        Check(connection.Sent.Select(x => x.Recipient).OrderBy(x => x).SequenceEqual(new[] { 2, 3 }),
            "Host trade broadcasts to both remote viewers");
        TraderViewers.Remove(100, 3);
        connection.Sent.Clear();
        trader.TraderData.Count = 7;
        TraderStockSync.PublishIfChanged(trader, 1);
        Check(connection.Sent.Count == 1 && connection.Sent[0].Recipient == 2,
            "Player who stopped trading is no longer a recipient");

        TraderStockSync.Clear();
        TraderViewers.Clear();
        connection.IsServer = false;
        TraderStockSync.Receive(world, 100, 1, new TraderData { Count = 20 });
        Check(trader.TraderData.Count == 20 && TraderViewers.GetViewers(100).Length == 0,
            "Session reset clears membership and revision state");
    }
}
