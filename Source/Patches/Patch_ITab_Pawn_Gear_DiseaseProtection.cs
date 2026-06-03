using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion.Patches;

// Tier-2 legibility (design §8): a three-line "Disease protection" block on the pawn Gear tab, drawn
// The row sits right after the vanilla Sharp/Blunt/Heat armor summary.
//
// FillTab keeps curY as a local, so a FillTab postfix can't see it. Instead we postfix the per-stat
// TryDrawOverallArmor and act only after the last (Heat) armor row, advancing the same ref curY so the
// tab's scroll height stays correct.
[HarmonyPatch(typeof(ITab_Pawn_Gear), "TryDrawOverallArmor")]
internal static class Patch_ITab_Pawn_Gear_DiseaseProtection
{
    private static readonly PropertyInfo SelPawnForGearProperty =
        AccessTools.Property(typeof(ITab_Pawn_Gear), "SelPawnForGear");

    private const float RowHeight = 22f;

    public static void Postfix(ITab_Pawn_Gear __instance, ref float curY, float width, StatDef stat)
    {
        // Only fire once per draw, riding the final armor row.
        if (stat != StatDefOf.ArmorRating_Heat || SelPawnForGearProperty == null)
        {
            return;
        }

        if (!(SelPawnForGearProperty.GetValue(__instance) is Pawn pawn) || pawn.apparel == null || !pawn.RaceProps.Humanlike)
        {
            return;
        }

        ContagionApparelProtectionUtility.ProtectionSummary summary = ContagionApparelProtectionUtility.GetProtectionSummary(pawn);

        Widgets.ListSeparator(ref curY, width, "Contagion_DiseaseProtectionHeader".Translate());
        Rect rect = new Rect(0f, curY, width, RowHeight);
        Widgets.Label(rect, ContagionApparelProtectionUtility.FormatProtectionSummaryLine(summary));
        TooltipHandler.TipRegion(rect, ContagionApparelProtectionUtility.BuildProtectionSummaryTooltip(pawn));
        curY += RowHeight;
    }
}
