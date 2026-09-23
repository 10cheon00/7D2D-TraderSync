using HarmonyLib;
using System;
using System.Collections.Generic;
using TraderSync.Transactions;

namespace TraderSync
{
    public sealed class TraderSyncMod : IModApi
    {
        public void InitMod(Mod modInstance)
        {
            // Register before the server/client builds its negotiated package ID mapping.
            var packages = (Dictionary<string, Type>)AccessTools.Field(
                typeof(NetPackageManager), "knownPackageTypes").GetValue(null);
            packages[typeof(NetPackageTraderSyncStock).Name] = typeof(NetPackageTraderSyncStock);
            packages[typeof(NetPackageTraderTransaction).Name] = typeof(NetPackageTraderTransaction);
            packages[typeof(NetPackageTraderTransactionResult).Name] = typeof(NetPackageTraderTransactionResult);
            packages[typeof(NetPackageTraderTransactionAck).Name] = typeof(NetPackageTraderTransactionAck);
            var harmony = new Harmony("TraderSync.SharedTraderWindow");
            harmony.PatchAll(typeof(TraderSyncMod).Assembly);
            Log.Out("[TraderSync] Shared NPC trader windows, server transactions and live stock propagation enabled.");
        }
    }
}
