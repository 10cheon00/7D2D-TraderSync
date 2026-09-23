using System;
using System.IO;
using System.Security.Cryptography;

namespace TraderSync.Transactions
{
    internal static class TradeWire
    {
        internal static byte[] Encode(Action<BinaryWriter> write)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            { write(writer); writer.Flush(); return stream.ToArray(); }
        }
        internal static string Hash(byte[] bytes)
        {
            using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(bytes));
        }
        internal static string ItemHash(ItemValue item) => Hash(Encode(item.Write));
        internal static byte[] Inventory(ItemStack[] bag, ItemStack[] belt) => Encode(w =>
        { GameUtils.WriteItemStack(w, bag); GameUtils.WriteItemStack(w, belt); });
        internal static void Inventory(byte[] bytes, out ItemStack[] bag, out ItemStack[] belt)
        {
            using (var reader = new BinaryReader(new MemoryStream(bytes, false)))
            { bag = GameUtils.ReadItemStack(reader); belt = GameUtils.ReadItemStack(reader); }
        }
        internal static void Blob(BinaryWriter writer, byte[] data)
        { writer.Write(data.Length); writer.Write(data); }
        internal static byte[] Blob(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > 4 * 1024 * 1024) throw new InvalidDataException("Invalid trade payload size.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return bytes;
        }
    }

    internal sealed class TradeRequest
    {
        internal long Sequence;
        internal int TraderId, Slot, Count, QuotedPrice, StockGroup = -1;
        internal bool Buy, Toolbelt;
        internal string StockHash, InventoryHash, ItemHash;
        internal void Write(BinaryWriter w)
        {
            w.Write(Sequence); w.Write(TraderId); w.Write(Slot); w.Write(Count);
            w.Write(QuotedPrice); w.Write(StockGroup); w.Write(Buy); w.Write(Toolbelt);
            w.Write(StockHash); w.Write(InventoryHash); w.Write(ItemHash);
        }
        internal static TradeRequest Read(BinaryReader r) => new TradeRequest
        {
            Sequence = r.ReadInt64(), TraderId = r.ReadInt32(), Slot = r.ReadInt32(),
            Count = r.ReadInt32(), QuotedPrice = r.ReadInt32(), StockGroup = r.ReadInt32(), Buy = r.ReadBoolean(),
            Toolbelt = r.ReadBoolean(), StockHash = r.ReadString(), InventoryHash = r.ReadString(), ItemHash = r.ReadString()
        };
    }

    internal sealed class TradeResult
    {
        internal long Sequence;
        internal int TraderId, Count, Price;
        internal bool Success, Buy;
        internal string Message = "";
        internal byte[] Inventory = Array.Empty<byte>();
        internal byte[] Stock = Array.Empty<byte>();
        internal void Write(BinaryWriter w)
        {
            w.Write(Sequence); w.Write(TraderId); w.Write(Success); w.Write(Buy);
            w.Write(Count); w.Write(Price); w.Write(Message);
            TradeWire.Blob(w, Inventory); TradeWire.Blob(w, Stock);
        }
        internal static TradeResult Read(BinaryReader r) => new TradeResult
        {
            Sequence = r.ReadInt64(), TraderId = r.ReadInt32(), Success = r.ReadBoolean(),
            Buy = r.ReadBoolean(), Count = r.ReadInt32(), Price = r.ReadInt32(), Message = r.ReadString(),
            Inventory = TradeWire.Blob(r), Stock = TradeWire.Blob(r)
        };
    }
}
