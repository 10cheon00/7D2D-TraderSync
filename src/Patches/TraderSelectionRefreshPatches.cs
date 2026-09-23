using HarmonyLib;

namespace TraderSync.Patches
{
    [HarmonyPatch(typeof(XUiC_TraderItemList), nameof(XUiC_TraderItemList.ClearSelection))]
    internal static class PreserveTraderSelectionPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(XUiC_TraderItemList __instance) => !TraderSelectionRefresh.IsRefreshing(__instance);
    }

    [HarmonyPatch(typeof(ItemActionEntryPurchase), "refreshBinding")]
    internal static class TraderDetailBindingPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ItemActionEntryPurchase __instance)
        {
            // Detail-only controllers are already bound by the sync code. Vanilla
            // writes through the rendered Item property and assumes a row view exists.
            return !(__instance.ItemController is TraderDetailEntry);
        }
    }
}
