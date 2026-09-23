using HarmonyLib;

namespace TraderSync.Patches
{
    [HarmonyPatch(typeof(EntityTrader), nameof(EntityTrader.IsSharedLock), new[] { typeof(ushort) })]
    internal static class EntityTraderSharedLockPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ushort _channel, ref bool __result)
        {
            if (_channel == TraderViewers.TradeChannel)
            {
                __result = true;
            }
        }
    }
}
