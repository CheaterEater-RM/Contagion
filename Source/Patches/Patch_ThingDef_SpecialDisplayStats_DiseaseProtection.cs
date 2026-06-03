using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion.Patches;

// Tier-3 legibility (design §8): apparel self-documents its disease seal on the item info card, so a
// modder's gear explains itself with no extra work. Coverage/integrity are pawn-state, so the card shows
// the resolved per-item seal only.
[HarmonyPatch(typeof(ThingDef), nameof(ThingDef.SpecialDisplayStats))]
internal static class Patch_ThingDef_SpecialDisplayStats_DiseaseProtection
{
    public static IEnumerable<StatDrawEntry> Postfix(IEnumerable<StatDrawEntry> values, ThingDef __instance)
    {
        foreach (StatDrawEntry entry in values)
        {
            yield return entry;
        }

        if (__instance?.apparel == null)
        {
            yield break;
        }

        ContagionApparelProtectionUtility.ItemProtectionDisplay display =
            ContagionApparelProtectionUtility.GetItemDisplay(__instance);
        if (!display.HasAnyProtection)
        {
            yield break;
        }

        if (display.AirborneLevel != ContagionApparelProtectionUtility.ProtectionLevel.None)
        {
            yield return new StatDrawEntry(
                StatCategoryDefOf.Apparel,
                "Contagion_ItemAirborneStatLabel".Translate(),
                display.AirwaySealed
                    ? "Contagion_ItemSealedValue".Translate()
                    : "Contagion_ItemProtectionStatValue".Translate(
                        ContagionApparelProtectionUtility.LevelLabel(display.AirborneLevel),
                        ContagionApparelProtectionUtility.FormatProtectionPercent(display.AirborneCategory.Protection)),
                "Contagion_ItemAirborneStatDesc".Translate(),
                -10000);
        }

        if (display.ContactLevel != ContagionApparelProtectionUtility.ProtectionLevel.None)
        {
            yield return new StatDrawEntry(
                StatCategoryDefOf.Apparel,
                "Contagion_ItemContactStatLabel".Translate(),
                "Contagion_ItemProtectionStatValue".Translate(
                    ContagionApparelProtectionUtility.LevelLabel(display.ContactLevel),
                    ContagionApparelProtectionUtility.FormatProtectionPercent(display.ContactCategory.Protection)),
                "Contagion_ItemContactStatDesc".Translate(),
                -10001);
        }

        if (display.SurfaceLevel != ContagionApparelProtectionUtility.ProtectionLevel.None)
        {
            yield return new StatDrawEntry(
                StatCategoryDefOf.Apparel,
                "Contagion_ItemSurfaceStatLabel".Translate(),
                "Contagion_ItemProtectionStatValue".Translate(
                    ContagionApparelProtectionUtility.LevelLabel(display.SurfaceLevel),
                    ContagionApparelProtectionUtility.FormatProtectionPercent(display.SurfaceCategory.Protection)),
                "Contagion_ItemSurfaceStatDesc".Translate(),
                -10002);
        }
    }
}
