using System;
using System.IO;

namespace TraderSync
{
    public sealed class NetPackageTraderSyncStock : NetPackage
    {
        private const int MaxSnapshotBytes = 4 * 1024 * 1024;
        private int traderId;
        private long revision;
        private byte[] stock;

        public override NetPackageDirection PackageDirection => NetPackageDirection.ToClient;

        public NetPackageTraderSyncStock Setup(int traderEntityId, long stockRevision, byte[] snapshot)
        {
            traderId = traderEntityId;
            revision = stockRevision;
            stock = (byte[])snapshot.Clone();
            return this;
        }

        public override void write(PooledBinaryWriter writer)
        {
            base.write(writer);
            BinaryWriter binary = writer;
            binary.Write(traderId);
            binary.Write(revision);
            binary.Write(stock.Length);
            binary.Write(stock);
        }

        public override void read(PooledBinaryReader reader)
        {
            traderId = reader.ReadInt32();
            revision = reader.ReadInt64();
            int length = reader.ReadInt32();
            if (length < 0 || length > MaxSnapshotBytes)
                throw new InvalidDataException("Invalid TraderSync stock snapshot length.");
            stock = reader.ReadBytes(length);
            if (stock.Length != length)
                throw new EndOfStreamException();
        }

        public override void ProcessPackage(World world, GameManager callbacks)
        {
            if (world == null || SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
                return;

            using (var stream = new MemoryStream(stock, false))
            using (var reader = new BinaryReader(stream))
            {
                var data = new TraderData();
                data.Read(reader);
                TraderStockSync.Receive(world, traderId, revision, data);
            }
        }

        public override int GetLength() => sizeof(ushort) + sizeof(int) + sizeof(long)
            + sizeof(int) + (stock?.Length ?? 0);
    }
}
