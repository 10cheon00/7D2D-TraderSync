using System.IO;

namespace TraderSync.Transactions
{
    public sealed class NetPackageTraderTransaction : NetPackage
    {
        private TradeRequest request;
        internal NetPackageTraderTransaction Setup(TradeRequest value) { request = value; return this; }
        public override NetPackageDirection PackageDirection => NetPackageDirection.ToServer;
        public override void write(PooledBinaryWriter writer) { base.write(writer); request.Write(writer); }
        public override void read(PooledBinaryReader reader) => request = TradeRequest.Read(reader);
        public override void ProcessPackage(World world, GameManager callbacks)
        {
            if (world != null && Sender != null) TradeServer.Handle(request, Sender);
        }
        public override int GetLength() => sizeof(ushort) + TradeWire.Encode(request.Write).Length;
    }

    public sealed class NetPackageTraderTransactionResult : NetPackage
    {
        private TradeResult result;
        internal NetPackageTraderTransactionResult Setup(TradeResult value) { result = value; return this; }
        public override NetPackageDirection PackageDirection => NetPackageDirection.ToClient;
        public override void write(PooledBinaryWriter writer) { base.write(writer); result.Write(writer); }
        public override void read(PooledBinaryReader reader) => result = TradeResult.Read(reader);
        public override void ProcessPackage(World world, GameManager callbacks)
        {
            if (world != null && !SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer) TradeClient.Receive(result);
        }
        public override int GetLength() => sizeof(ushort) + TradeWire.Encode(result.Write).Length;
    }

    public sealed class NetPackageTraderTransactionAck : NetPackage
    {
        private long sequence;
        internal NetPackageTraderTransactionAck Setup(long value) { sequence = value; return this; }
        public override NetPackageDirection PackageDirection => NetPackageDirection.ToServer;
        public override void write(PooledBinaryWriter writer) { base.write(writer); ((BinaryWriter)writer).Write(sequence); }
        public override void read(PooledBinaryReader reader) => sequence = reader.ReadInt64();
        public override void ProcessPackage(World world, GameManager callbacks)
        {
            if (world != null && Sender != null && SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
                TradeServer.Acknowledge(Sender, sequence);
        }
        public override int GetLength() => 10;
    }
}
