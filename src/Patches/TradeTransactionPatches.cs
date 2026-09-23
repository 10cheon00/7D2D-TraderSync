using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TraderSync.Transactions;

namespace TraderSync.Patches
{
    [HarmonyPatch(typeof(ItemActionEntryPurchase), nameof(ItemActionEntryPurchase.OnActivated))]
    internal static class PurchaseTransactionPatch
    {
        [HarmonyPrefix] private static bool Prefix(ItemActionEntryPurchase __instance) => TradeClient.Begin(__instance, true);
    }
    [HarmonyPatch(typeof(ItemActionEntrySell), nameof(ItemActionEntrySell.OnActivated))]
    internal static class SellTransactionPatch
    {
        [HarmonyPrefix] private static bool Prefix(ItemActionEntrySell __instance) => TradeClient.Begin(__instance, false);
    }
    [HarmonyPatch(typeof(GameManager), "UpdateTick")]
    internal static class TransactionRetryPatch
    {
        [HarmonyPostfix] private static void Postfix() => TradeClient.Tick();
    }

    // Keep the transaction's local input stable while its server response is in flight.
    [HarmonyPatch]
    internal static class PendingTradeInputPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "HandleStackSwap", "HandlePartialStackPickup", "HandleDropOne",
                "HandleClickComplete", "SwapItem", "HandleMoveToPreferredLocation" })
                yield return AccessTools.Method(typeof(XUiC_ItemStack), name);
            // Craft completion/cancellation also writes the backpack.
            yield return AccessTools.Method(typeof(XUiC_CraftingQueue), "Update");
            yield return AccessTools.Method(typeof(XUiC_RecipeStack), "Update");
            yield return AccessTools.Method(typeof(XUiC_RecipeStack), "HandleOnPress");
            yield return AccessTools.Method(typeof(XUiM_PlayerInventory), "SortStacks");
            foreach (var type in typeof(BaseItemActionEntry).Assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(BaseItemActionEntry))
                    && t != typeof(ItemActionEntryPurchase) && t != typeof(ItemActionEntrySell)))
            {
                var method = type.GetMethod("OnActivated", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (method != null) yield return method;
            }
        }
        [HarmonyPrefix] private static bool Prefix() => !TradeClient.Pending;
    }

    [HarmonyPatch(typeof(GUIWindowManager), "Close", new[] { typeof(GUIWindow), typeof(bool) })]
    internal static class PendingTraderWindowClosePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(GUIWindow _w)
        {
            // Keep the modal trader input context while awaiting settlement. Shutdown
            // and death must still be able to close the UI.
            var player = GameManager.Instance.World?.GetPrimaryPlayer();
            return !TradeClient.Pending || _w == null || _w.Id != "trader"
                || player == null || player.IsDead();
        }
    }

    [HarmonyPatch(typeof(NetPackagePlayerInventory), nameof(NetPackagePlayerInventory.ProcessPackage))]
    internal static class PendingInventoryUploadPatch
    {
        [HarmonyPrefix]
        private static void Prefix(NetPackagePlayerInventory __instance, ref ItemStack[] ___toolbelt, ref Bag ___bag)
        {
            if (!TradeServer.AwaitingAck(__instance.Sender)) return;
            // Equipment updates remain valid; old bag/belt uploads must not undo a commit.
            ___toolbelt = null; ___bag = null;
        }
    }

    [HarmonyPatch(typeof(EntityPlayerLocal), nameof(EntityPlayerLocal.dropItemOnDeath))]
    internal static class PendingTradeDeathPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(EntityPlayerLocal __instance) => !TradeClient.DeferDeath(__instance);
    }

    [HarmonyPatch(typeof(EntityPlayerLocal), nameof(EntityPlayerLocal.OnDeathUpdate))]
    internal static class PendingTradeRespawnPatch
    {
        [HarmonyPrefix] private static bool Prefix() => !TradeClient.Pending;
    }

    [HarmonyPatch(typeof(EntityPlayerLocal), nameof(EntityPlayerLocal.dropItemOnQuit))]
    internal static class PendingTradeQuitDropPatch
    {
        // A pre-settlement backpack drop could duplicate assets already committed
        // on the server. Leave the canonical saved inventory intact on disconnect.
        [HarmonyPrefix] private static bool Prefix() => !TradeClient.Pending;
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.SavePlayerData))]
    internal static class PendingPlayerSavePatch
    {
        [HarmonyPrefix]
        private static void Prefix(ClientInfo _cInfo, PlayerDataFile _playerDataFile)
        {
            TradeServer.PreserveCommittedInventory(_cInfo, _playerDataFile);
        }
    }
}
