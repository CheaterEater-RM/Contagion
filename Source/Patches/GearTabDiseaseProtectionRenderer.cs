using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion.Patches;

// Shared draw logic for the Tier-2 "Disease protection" block on the pawn Gear tab (design §8).
//
// Combat Extended replaces the Gear tab with CombatExtended.ITab_Inventory, a subclass of ITab_Pawn_Gear
// that reimplements FillTab with its own TryDrawOverallArmor overload — so the vanilla patch never fires
// under CE. Both the vanilla patch and the CE compat patch call into this one renderer, riding the final
// (Heat) armor row so curY stays correct for the tab's scroll plumbing. `instance` is the gear tab, either
// vanilla ITab_Pawn_Gear or the CE subclass (SelPawnForGear is inherited, so reflection on the base works).
internal static class GearTabDiseaseProtectionRenderer
{
    private static readonly PropertyInfo SelPawnForGearProperty =
        AccessTools.Property(typeof(ITab_Pawn_Gear), "SelPawnForGear");

    private const float RowHeight = 22f;

    public static void DrawSummary(ITab_Pawn_Gear instance, ref float curY, float width, StatDef stat)
    {
        // Only fire once per draw, riding the final armor row.
        if (stat != StatDefOf.ArmorRating_Heat || SelPawnForGearProperty == null)
        {
            return;
        }

        if (!(SelPawnForGearProperty.GetValue(instance) is Pawn pawn) || pawn.apparel == null || !pawn.RaceProps.Humanlike)
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
