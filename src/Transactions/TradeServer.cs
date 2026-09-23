using System;
using System.Linq;

namespace TraderSync.Transactions
{
    internal static class TradeServer
    {
        private static readonly TransactionLedger<TradeResult> ledger = new TransactionLedger<TradeResult>();

        internal static bool AwaitingAck(ClientInfo connection) => ledger.AwaitingAck(connection);
        internal static void Acknowledge(object connection, long sequence) => ledger.Acknowledge(connection, sequence);
        internal static void Clear() => ledger.Clear();

        internal static void PreserveCommittedInventory(ClientInfo connection, PlayerDataFile incoming)
        {
            if (!AwaitingAck(connection) || connection.latestPlayerData == null) return;
            incoming.inventory = ItemStack.Clone(connection.latestPlayerData.inventory);
            incoming.bag = connection.latestPlayerData.bag.Clone();
        }

        internal static void Handle(TradeRequest request, ClientInfo connection, EntityPlayerLocal host = null)
        {
            if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer) return;
            object key = (object)connection ?? host;
            if (key == null) return;
            int playerId = connection != null ? connection.entityId : host.entityId;
            var result = ledger.Execute(key, request.Sequence,
                () => Execute(request, playerId, connection, host), r => r.Success);
            if (result == null) return;

            // Publication happens after the transaction has been recorded. A failed send
            // can be retried using the same request without executing the trade again.
            try
            {
                var trader = GameManager.Instance.World.GetEntity(request.TraderId) as EntityTrader;
                if (trader != null)
                {
                    TraderStockSync.PublishIfChanged(trader);
                    TraderStockSync.SendCurrentTo(trader, playerId);
                }
                if (connection != null)
                    connection.SendPackage(NetPackageManager.GetPackage<NetPackageTraderTransactionResult>().Setup(result));
                else
                    TradeClient.Receive(result);
            }
            catch (Exception ex)
            {
                Log.Error("[TraderSync] Committed transaction response will be retried: " + ex);
            }
        }

        private static TradeResult Execute(TradeRequest request, int playerId, ClientInfo connection, EntityPlayerLocal host)
        {
            var result = new TradeResult { Sequence = request.Sequence, TraderId = request.TraderId, Buy = request.Buy };
            var world = GameManager.Instance.World;
            var trader = world?.GetEntity(request.TraderId) as EntityTrader;
            try
            {
                var player = world?.GetEntity(playerId) as EntityPlayer;
                if (trader == null || player == null || player.IsDead()
                    || !TraderViewers.GetViewers(request.TraderId).Contains(playerId)
                    || !trader.CanLockOnServer(playerId, null, TraderViewers.TradeChannel))
                    throw new TradeRejectedException("상인과 거래 중이 아닙니다.");
                if (request.Count <= 0 || request.Count > ushort.MaxValue || request.Slot < 0)
                    throw new TradeRejectedException("잘못된 거래 수량 또는 슬롯입니다.");
                var originalStock = trader.TraderData;
                result.Stock = TraderStockSync.Serialize(originalStock);
                if (request.StockHash != TradeWire.Hash(result.Stock))
                    throw new TradeRejectedException("상인 재고가 변경되었습니다. 확인 후 다시 거래하세요.");

                var saved = connection?.latestPlayerData;
                ItemStack[] bag = host != null ? host.bag.GetSlots() : saved?.bag?.GetSlots();
                ItemStack[] belt = host != null ? host.inventory.GetSlots() : saved?.inventory;
                if (bag == null || belt == null || request.InventoryHash != TradeWire.Hash(TradeWire.Inventory(bag, belt)))
                    throw new TradeRejectedException("인벤토리 동기화 중입니다. 다시 거래하세요.");

                var stock = originalStock.Clone();
                var inventory = new TradeInventory(bag, belt, player.inventory.PUBLIC_SLOTS);
                ItemStack item;
                if (request.Buy)
                {
                    if (request.StockGroup == -1)
                    {
                        if (request.Slot >= stock.PrimaryInventory.Count)
                            throw new TradeRejectedException("상품이 더 이상 없습니다.");
                        item = stock.PrimaryInventory[request.Slot].Item;
                    }
                    else
                    {
                        ValidateTierGroup(player, stock, request.StockGroup);
                        var group = stock.TierItemGroups[request.StockGroup];
                        if (request.Slot >= group.Length) throw new TradeRejectedException("상품이 더 이상 없습니다.");
                        item = group[request.Slot];
                    }
                }
                else
                {
                    var slots = request.Toolbelt ? inventory.Belt : inventory.Bag;
                    if (request.Slot >= slots.Length || (request.Toolbelt && request.Slot >= player.inventory.PUBLIC_SLOTS))
                        throw new TradeRejectedException("잘못된 인벤토리 슬롯입니다.");
                    item = slots[request.Slot];
                }
                if (item == null || item.IsEmpty() || item.count < request.Count)
                    throw new TradeRejectedException("거래할 수량이 부족합니다.");
                if (request.ItemHash != TradeWire.ItemHash(item.itemValue))
                    throw new TradeRejectedException("선택한 품목이 변경되었습니다. 다시 선택하세요.");
                var cls = item.itemValue.ItemClass;
                if (!request.Buy && !(cls.IsBlock() ? Block.list[item.itemValue.type].SellableToTrader : cls.SellableToTrader))
                    throw new TradeRejectedException("이 품목은 판매할 수 없습니다.");
                int price = TradePricing.Price(player, stock, item.itemValue, request.Count, request.Buy);
                if (price <= 0 || price != request.QuotedPrice)
                    throw new TradeRejectedException("거래 가격이 변경되었거나 유효하지 않습니다. 다시 확인하세요.");
                var transfer = item.Clone();
                transfer.count = request.Count;

                if (request.Buy)
                {
                    inventory.RemoveCurrency(price);
                    inventory.Add(transfer);
                    item.count -= request.Count;
                    if (request.StockGroup == -1 && item.count == 0) stock.PrimaryInventory.RemoveAt(request.Slot);
                    stock.AvailableMoney = checked(stock.AvailableMoney + price);
                }
                else
                {
                    long limit = (long)cls.MaxCount * TraderInfo.TraderBuyLimit;
                    if (limit > 0 && (long)stock.GetPrimaryItemCount(item.itemValue) + request.Count > limit)
                        throw new TradeRejectedException("상인의 해당 품목 매입 한도를 초과합니다.");
                    item.count -= request.Count;
                    if (item.count == 0) (request.Toolbelt ? inventory.Belt : inventory.Bag)[request.Slot] = ItemStack.Empty;
                    inventory.ReturnAmmo(transfer);
                    // Keep distinct metadata intact instead of merging by type alone.
                    stock.PrimaryInventoryAdd(new TraderData.Entry(transfer.Clone(), 0, true));
                    inventory.Add(new ItemStack(ItemClass.GetItem(TraderInfo.CurrencyItem), price));
                }

                // All serialization/allocation and validation happen before the live commit.
                result.Inventory = TradeWire.Inventory(inventory.Bag, inventory.Belt);
                result.Stock = TraderStockSync.Serialize(stock);
                result.Count = request.Count; result.Price = price;
                if (host == null)
                {
                    var stagedBag = saved.bag.Clone();
                    stagedBag.SetSlots(inventory.Bag);
                    // Plain reference writes, no UI/network callbacks within this commit.
                    saved.bag = stagedBag;
                    saved.inventory = inventory.Belt;
                    saved.bModifiedSinceLastSave = true;
                    trader.TraderData = stock;
                }
                else
                {
                    var oldBag = ItemStack.Clone(bag);
                    var oldBelt = ItemStack.Clone(belt);
                    try
                    {
                        TradeClient.ApplyingResult = true;
                        host.bag.SetSlots(inventory.Bag);
                        host.inventory.SetSlots(inventory.Belt);
                        trader.TraderData = stock;
                    }
                    catch
                    {
                        host.bag.SetSlots(oldBag);
                        host.inventory.SetSlots(oldBelt);
                        trader.TraderData = originalStock;
                        throw;
                    }
                    finally { TradeClient.ApplyingResult = false; }
                }
                result.Success = true;
            }
            catch (TradeRejectedException ex) { result.Message = ex.Message; }
            catch (Exception ex)
            {
                result.Message = "거래를 처리할 수 없습니다.";
                Log.Error("[TraderSync] Transaction rejected before commit: " + ex);
            }
            return result;
        }

        private static void ValidateTierGroup(EntityPlayer player, TraderData stock, int group)
        {
            if (group < 0 || group >= stock.TierItemGroups.Count || group >= stock.TraderInfo.TierItemGroups.Count)
                throw new TradeRejectedException("유효하지 않은 특수 재고입니다.");
            float level = EffectManager.GetValue(PassiveEffects.SecretStash, null, player.Progression.Level, player);
            var rule = stock.TraderInfo.TierItemGroups[group];
            if (!(level >= rule.minLevel || rule.minLevel == -1) || !(level <= rule.maxLevel || rule.maxLevel == -1))
                throw new TradeRejectedException("접근할 수 없는 특수 재고입니다.");
        }
    }
}
