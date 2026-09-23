using HarmonyLib;

namespace TraderSync.Patches
{
    // NPC stock is authoritative on the server. Vending machines retain vanilla behavior.
    [HarmonyPatch(typeof(NetPackageTraderData), nameof(NetPackageTraderData.ProcessPackage))]
    internal static class TraderStockReceivedPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(int ___entityId) => ___entityId == -1;
    }

    [HarmonyPatch(typeof(TraderData), nameof(TraderData.SetModified))]
    internal static class TraderStockModifiedPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ITrader _trader) => !(_trader is EntityTrader);
    }
}
