using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TraderSync
{
    internal static class TraderStockSync
    {
        // Accessed by game callbacks / ProcessPackage on the game thread.
        private static readonly Dictionary<int, byte[]> lastStocks = new Dictionary<int, byte[]>();
        private static readonly Dictionary<int, long> revisions = new Dictionary<int, long>();
        private static readonly Dictionary<int, long> receivedRevisions = new Dictionary<int, long>();

        internal static byte[] Serialize(TraderData data)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                data.Write(writer);
                writer.Flush();
                return stream.ToArray();
            }
        }

        internal static void RememberIfMissing(EntityTrader trader)
        {
            if (!lastStocks.ContainsKey(trader.entityId))
                lastStocks[trader.entityId] = Serialize(trader.TraderData);
        }

        internal static void PublishIfChanged(EntityTrader trader, int senderId = -1)
        {
            if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
                return;

            byte[] stock = Serialize(trader.TraderData);
            if (lastStocks.TryGetValue(trader.entityId, out var previous) && previous.SequenceEqual(stock))
                return;

            lastStocks[trader.entityId] = stock;
            revisions.TryGetValue(trader.entityId, out long revision);
            revisions[trader.entityId] = ++revision;

            foreach (int playerId in TraderViewers.GetViewers(trader.entityId))
            {
                // The originator has already applied this vanilla transaction locally.
                if (playerId == senderId)
                    continue;

                var player = GameManager.Instance.World.GetEntity(playerId);
                if (player is EntityPlayerLocal localPlayer)
                {
                    RefreshWindow(localPlayer, trader.entityId);
                }
                else if (player is EntityPlayer)
                {
                    SingletonMonoBehaviour<ConnectionManager>.Instance.SendPackage(
                        NetPackageManager.GetPackage<NetPackageTraderSyncStock>()
                            .Setup(trader.entityId, revision, stock),
                        _onlyClientsAttachedToAnEntity: false, _attachedToEntityId: playerId);
                }
            }
        }

        internal static void Receive(World world, int traderId, long revision, TraderData data)
        {
            if (receivedRevisions.TryGetValue(traderId, out long previous) && revision <= previous)
                return;

            var player = world.GetPrimaryPlayer();
            if (player == null || !(world.GetEntity(traderId) is EntityTrader trader))
                return;
            var ui = LocalPlayerUI.GetUIForPlayer(player);
            if (ui == null || !ui.windowManager.IsWindowOpen("trader")
                || !(ui.xui.Trader.Trader is EntityTrader current) || current.entityId != traderId)
                return;

            receivedRevisions[traderId] = revision;
            trader.TraderData.CopyFrom(data);
            // CopyFrom does not call SetModified: receiving never uploads the snapshot again.
            RefreshWindow(player, traderId);
        }

        internal static void SendCurrentTo(EntityTrader trader, int playerId)
        {
            if (GameManager.Instance.World.GetEntity(playerId) is EntityPlayerLocal local)
            { RefreshWindow(local, trader.entityId); return; }
            revisions.TryGetValue(trader.entityId, out long revision);
            SingletonMonoBehaviour<ConnectionManager>.Instance.SendPackage(
                NetPackageManager.GetPackage<NetPackageTraderSyncStock>()
                    .Setup(trader.entityId, revision, Serialize(trader.TraderData)),
                _onlyClientsAttachedToAnEntity: false, _attachedToEntityId: playerId);
        }

        internal static void RefreshWindow(EntityPlayerLocal player, int traderId)
        {
            var ui = LocalPlayerUI.GetUIForPlayer(player);
            if (ui == null || !ui.windowManager.IsWindowOpen("trader")
                || !(ui.xui.Trader.Trader is EntityTrader current) || current.entityId != traderId)
                return;

            var group = ui.xui.Trader.TraderWindowGroup;
            var window = group?.GetChildByType<XUiC_TraderWindow>();
            if (window == null)
                return;

            // The group wrapper marks CompletedTransaction, so do not use it for remote updates.
            TraderSelectionRefresh.Refresh(window);
            group.RefreshTraderWindow();
        }

        internal static void Clear()
        {
            lastStocks.Clear();
            revisions.Clear();
            receivedRevisions.Clear();
        }
    }
}
