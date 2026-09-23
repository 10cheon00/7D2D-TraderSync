using System;
using System.Collections.Generic;

namespace TraderSync.Transactions
{
    // Serializes the entire read/validate/stage/commit operation, not just its writes.
    // Keys are connection objects: reconnecting creates a new sequence space.
    internal sealed class TransactionLedger<T> where T : class
    {
        private sealed class Entry { internal long Sequence; internal T Result; internal bool AwaitingAck; }
        private readonly object gate = new object();
        private readonly Dictionary<object, Entry> entries = new Dictionary<object, Entry>();

        internal T Execute(object connection, long sequence, Func<T> transact, Func<T, bool> committed)
        {
            lock (gate)
            {
                if (sequence <= 0) return null;
                if (entries.TryGetValue(connection, out var previous))
                {
                    if (sequence == previous.Sequence) return previous.Result;
                    // Never re-execute an old request, even after its cached response is replaced.
                    if (sequence < previous.Sequence || previous.AwaitingAck) return null;
                }
                T result = transact();
                entries[connection] = new Entry
                {
                    Sequence = sequence, Result = result, AwaitingAck = committed(result)
                };
                return result;
            }
        }

        internal bool AwaitingAck(object connection)
        {
            lock (gate) return connection != null && entries.TryGetValue(connection, out var entry) && entry.AwaitingAck;
        }

        internal void Acknowledge(object connection, long sequence)
        {
            lock (gate)
                if (entries.TryGetValue(connection, out var entry) && entry.Sequence == sequence)
                    entry.AwaitingAck = false;
        }

        internal void Clear() { lock (gate) entries.Clear(); }
    }
}
