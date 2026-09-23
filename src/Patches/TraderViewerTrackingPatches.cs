using HarmonyLib;
using TraderSync.Transactions;

namespace TraderSync.Patches
{
    [HarmonyPatch(typeof(EntityTrader), nameof(EntityTrader.OnLockedServer))]
    internal static class TraderOpenedPatch
    {
        [HarmonyPostfix]
        private static void Postfix(EntityTrader __instance, bool _success,
            int _lockingPlayerID, ushort _channel)
        {
            if (_success && _channel == TraderViewers.TradeChannel
                && SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
            {
                TraderViewers.Add(__instance.entityId, _lockingPlayerID);
                // Also propagate a restock triggered by opening the trader to existing viewers.
                TraderStockSync.PublishIfChanged(__instance, _lockingPlayerID);
            }
        }
    }

    [HarmonyPatch(typeof(EntityTrader), nameof(EntityTrader.OnUnlockedServer))]
    internal static class TraderClosedPatch
    {
        [HarmonyPostfix]
        private static void Postfix(EntityTrader __instance, int _unlockingPlayerId, ushort _channel)
        {
            if (_channel == TraderViewers.TradeChannel
                && SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
            {
                TraderViewers.Remove(__instance.entityId, _unlockingPlayerId);
            }
        }
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.PlayerDisconnected))]
    internal static class TraderPlayerDisconnectedPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ClientInfo _cInfo)
        {
            if (_cInfo != null && SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
                TraderViewers.RemovePlayer(_cInfo.entityId);
        }
    }

    // LockManager resets its own locks when starting a session; mirror that reset.
    [HarmonyPatch(typeof(LockManager), nameof(LockManager.Init))]
    internal static class TraderViewerSessionStartPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            TraderViewers.Clear();
            TraderStockSync.Clear();
            TradeServer.Clear();
            TradeClient.Clear();
        }
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.Cleanup))]
    internal static class TraderViewerSessionEndPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            TraderViewers.Clear();
            TraderStockSync.Clear();
            TradeServer.Clear();
            TradeClient.Clear();
        }
    }
}
