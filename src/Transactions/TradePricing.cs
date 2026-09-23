using System;
using UnityEngine;
using SandboxOptions;

namespace TraderSync.Transactions
{
    // Mirrors this build's XUiM_Trader formulas, with an explicit server-side player
    // instead of XUiM_Player.GetPlayer() (which would select the host).
    internal static class TradePricing
    {
        internal static int Price(EntityPlayer player, TraderData trader, ItemValue item, int count, bool buy)
        {
            var cls = item.ItemClass;
            bool block = cls.IsBlock();
            int bundle = block ? Block.list[item.type].EconomicBundleSize : cls.EconomicBundleSize;
            if (bundle <= 0 || count <= 0 || count % bundle != 0)
                throw new TradeRejectedException("거래 묶음 수량이 맞지 않습니다.");
            float value = block ? Block.list[item.type].EconomicValue : cls.EconomicValue;
            if (!buy) value *= block ? Block.list[item.type].EconomicSellScale : cls.EconomicSellScale;
            if (!block) value = EffectManager.GetValue(PassiveEffects.EconomicValue, item, value, player);
            if (value == 0f) return 0;
            var info = trader.TraderInfo;
            float custom = buy ? info.OverrideBuyMarkup : info.OverrideSellMarkdown;
            bool overridden = custom != -1f;
            float markup = overridden ? custom : (buy ? TraderInfo.BuyMarkup : TraderInfo.SellMarkdown);
            float total;
            if (item.HasQuality)
            {
                float min = cls.TraderQualityMinMod, max = cls.TraderQualityMaxMod;
                if (!(min > 0f) && !(max > 0f)) { min = TraderInfo.QualityMinMod; max = TraderInfo.QualityMaxMod; }
                total = value * markup * Mathf.Lerp(min, max, (item.Quality - 1f) / 5f) * item.PercentUsesLeft;
            }
            else if (cls.HasSubItems)
            {
                total = 0f;
                foreach (var mod in item.Modifications)
                    if (!mod.IsEmpty()) total += Price(player, trader, mod, 1, buy);
            }
            else total = value * markup;
            if (!overridden)
            {
                float effect = EffectManager.GetValue(buy ? PassiveEffects.BarteringBuying : PassiveEffects.BarteringSelling,
                    null, 0f, player, null, cls.ItemTags);
                total += total * (buy ? -effect : effect);
            }
            double basePrice = total * (count / bundle);
            if (double.IsNaN(basePrice) || double.IsInfinity(basePrice) || basePrice < 0 || basePrice > int.MaxValue)
                throw new TradeRejectedException("잘못된 거래 가격입니다.");
            float final = (int)basePrice * SandboxOptionManager.GetFloat(buy
                ? global::SandboxOptions.SandboxOptions.TraderBuyPrices
                : global::SandboxOptions.SandboxOptions.TraderSellPrices);
            if (float.IsNaN(final) || float.IsInfinity(final) || final < 0 || final >= int.MaxValue)
                throw new TradeRejectedException("잘못된 거래 가격입니다.");
            return Mathf.CeilToInt(final);
        }
    }
}
