using System;
using UnityEngine;

namespace TraderSync.Transactions
{
    internal static class TradeClient
    {
        private static TradeRequest pending;
        private static XUi pendingUi;
        private static long sequence, completedSequence;
        private static float retryAt;
        private static EntityPlayerLocal deferredDeath;
        internal static bool ApplyingResult;
        internal static bool Pending => pending != null && !ApplyingResult;

        internal static bool DeferDeath(EntityPlayerLocal player)
        {
            if (!Pending) return false;
            deferredDeath = player;
            return true;
        }

        internal static bool Begin(BaseItemActionEntry action, bool buy)
        {
            var ui = action.ItemController.xui;
            if (!(ui.Trader.Trader is EntityTrader trader)) return true;
            if (Pending) return false;
            var player = ui.playerUI.entityPlayer;
            if (!player.DragAndDropItem.IsEmpty())
            {
                GameManager.ShowTooltip(player, "들고 있는 아이템을 내려놓은 뒤 거래하세요.");
                return false;
            }
            int slot, count, group = -1;
            bool belt = false;
            ItemStack item;
            if (buy)
            {
                var entry = action.ItemController as XUiC_TraderItemEntry;
                if (entry?.Item == null || entry.Item.IsEmpty()) return false;
                slot = entry.SlotIndex; item = entry.Item;
                count = entry.InfoWindow.BuySellCounter.Count;
                for (int g = 0; g < trader.TraderData.TierItemGroups.Count; g++)
                    if (Array.IndexOf(trader.TraderData.TierItemGroups[g], item) >= 0)
                    { group = g; slot = Array.IndexOf(trader.TraderData.TierItemGroups[g], item); break; }
            }
            else
            {
                var entry = action.ItemController as XUiC_ItemStack;
                if (entry == null || entry.ItemStack.IsEmpty()) return false;
                if (entry.StackLocation != XUiC_ItemStack.StackLocationTypes.Backpack
                    && entry.StackLocation != XUiC_ItemStack.StackLocationTypes.ToolBelt) return false;
                slot = entry.SlotNumber; item = entry.ItemStack;
                count = entry.InfoWindow.BuySellCounter.Count;
                belt = entry.StackLocation == XUiC_ItemStack.StackLocationTypes.ToolBelt;
            }
            if (count <= 0) return false;
            int price = buy ? XUiM_Trader.GetBuyPrice(ui, item.itemValue, count, null, slot)
                : XUiM_Trader.GetSellPrice(ui, item.itemValue, count);
            pending = new TradeRequest
            {
                Sequence = ++sequence, TraderId = trader.entityId, Buy = buy,
                Slot = slot, Count = count, Toolbelt = belt, StockGroup = group, QuotedPrice = price,
                StockHash = TradeWire.Hash(TraderStockSync.Serialize(trader.TraderData)),
                ItemHash = TradeWire.ItemHash(item.itemValue),
                InventoryHash = TradeWire.Hash(TradeWire.Inventory(player.bag.GetSlots(), player.inventory.GetSlots()))
            };
            pendingUi = ui;
            retryAt = Time.realtimeSinceStartup + 2f;
            if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
            {
                // Same reliable channel as the request, so the server sees this first.
                SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(
                    NetPackageManager.GetPackage<NetPackagePlayerInventory>().Setup(player, true, true, true, true));
            }
            Send();
            return false; // Never run the original optimistic client-side transaction.
        }

        private static void Send()
        {
            if (SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
                TradeServer.Handle(pending, null, pendingUi.playerUI.entityPlayer);
            else
                SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(
                    NetPackageManager.GetPackage<NetPackageTraderTransaction>().Setup(pending));
        }

        internal static void Tick()
        {
            if (!Pending || Time.realtimeSinceStartup < retryAt) return;
            retryAt = Time.realtimeSinceStartup + 2f;
            // A timeout is not a rollback: the original may already have committed.
            Send();
        }

        private static void Ack(long id, EntityPlayerLocal player)
        {
            if (SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer) TradeServer.Acknowledge(player, id);
            else SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(
                NetPackageManager.GetPackage<NetPackageTraderTransactionAck>().Setup(id));
        }

        internal static void Receive(TradeResult result)
        {
            var player = GameManager.Instance.World?.GetPrimaryPlayer();
            if (player == null) return;
            if (result.Sequence <= completedSequence) { Ack(result.Sequence, player); return; }
            if (pending == null || pending.Sequence != result.Sequence || pending.TraderId != result.TraderId) return;
            var ui = pendingUi;
            var trader = GameManager.Instance.World.GetEntity(result.TraderId) as EntityTrader;
            ApplyingResult = true;
            try
            {
                if (result.Success && !SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
                {
                    TradeWire.Inventory(result.Inventory, out var bag, out var belt);
                    ui.PlayerInventory.SetBackpackItemStacks(bag);
                    ui.PlayerInventory.SetToolbeltItemStacks(belt);
                }
                // Cached results never replace stock. A separate current, revisioned
                // snapshot is sent by the server, also on rejected/retried requests.
                completedSequence = result.Sequence;
                pending = null;
                pendingUi = null;
                Ack(result.Sequence, player);
                // Effects are run after marking the response applied, never on duplicate delivery.
                if (result.Success)
                {
                    if (result.Buy) QuestEventManager.Current.BoughtItems(trader?.EntityName ?? "", result.Count);
                    else
                    {
                        QuestEventManager.Current.SoldItems(trader?.EntityName ?? "", result.Count);
                        player.Progression.AddLevelExp(Math.Max(result.Price, 1), "_xpFromSelling", Progression.XPTypes.Selling);
                    }
                    Audio.Manager.PlayInsidePlayerHead("ui_trader_purchase");
                    if (ui.Trader.Trader is EntityTrader current && current.entityId == result.TraderId)
                    {
                        var window = ui.Trader.TraderWindowGroup?.GetChildByType<XUiC_TraderWindow>();
                        if (window != null) window.CompletedTransaction = true;
                    }
                }
                else GameManager.ShowTooltip(player, result.Message);
                TraderStockSync.RefreshWindow(player, result.TraderId);
            }
            finally
            {
                ApplyingResult = false;
                if (pending == null && deferredDeath != null)
                {
                    var deadPlayer = deferredDeath;
                    deferredDeath = null;
                    deadPlayer.dropItemOnDeath();
                }
            }
        }

        internal static void Clear()
        { pending = null; pendingUi = null; deferredDeath = null; sequence = completedSequence = 0; ApplyingResult = false; }
    }
}
