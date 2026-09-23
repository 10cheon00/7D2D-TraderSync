using System;
using System.Collections.Generic;

namespace TraderSync
{
    /// <summary>Server-side membership for the NPC trader's trade lock channel.</summary>
    public static class TraderViewers
    {
        public const ushort TradeChannel = 1;

        private static readonly Dictionary<int, HashSet<int>> viewersByTrader =
            new Dictionary<int, HashSet<int>>();
        private static readonly object syncRoot = new object();

        internal static void Add(int traderEntityId, int playerEntityId)
        {
            lock (syncRoot)
            {
                if (!viewersByTrader.TryGetValue(traderEntityId, out var viewers))
                {
                    viewers = new HashSet<int>();
                    viewersByTrader.Add(traderEntityId, viewers);
                }

                viewers.Add(playerEntityId);
            }
        }

        internal static void Remove(int traderEntityId, int playerEntityId)
        {
            lock (syncRoot)
            {
                if (!viewersByTrader.TryGetValue(traderEntityId, out var viewers))
                    return;

                viewers.Remove(playerEntityId);
                if (viewers.Count == 0)
                    viewersByTrader.Remove(traderEntityId);
            }
        }

        internal static void RemovePlayer(int playerEntityId)
        {
            lock (syncRoot)
            {
                var emptyTraders = new List<int>();
                foreach (var entry in viewersByTrader)
                {
                    entry.Value.Remove(playerEntityId);
                    if (entry.Value.Count == 0)
                        emptyTraders.Add(entry.Key);
                }

                foreach (int traderEntityId in emptyTraders)
                    viewersByTrader.Remove(traderEntityId);
            }
        }

        /// <summary>Returns a snapshot so callers cannot modify the tracked membership.</summary>
        public static int[] GetViewers(int traderEntityId)
        {
            lock (syncRoot)
            {
                if (!viewersByTrader.TryGetValue(traderEntityId, out var viewers))
                    return Array.Empty<int>();

                var snapshot = new int[viewers.Count];
                viewers.CopyTo(snapshot);
                return snapshot;
            }
        }

        internal static void Clear()
        {
            lock (syncRoot)
                viewersByTrader.Clear();
        }
    }
}
