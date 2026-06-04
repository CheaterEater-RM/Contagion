using HarmonyLib;
using Verse;

namespace Contagion.Patches;

[HarmonyPatch(typeof(Pawn), nameof(Pawn.DrawExtraSelectionOverlays))]
internal static class Patch_Pawn_DrawExtraSelectionOverlays
{
    public static void Postfix(Pawn __instance)
    {
        if (Contagion_Mod.Settings?.DeveloperDiagnosticsEnabled != true
            || __instance == null
            || !__instance.Spawned
            || __instance.Map == null
            || __instance.Map != Find.CurrentMap)
        {
            return;
        }

        ContagionDeveloperOverlayDrawer.DrawNominalSpreadOverlay(__instance);

        // A single selected animal also gets a "danger if it grazed here" fecal-oral eating heatmap.
        // Keeping this single-selection-only avoids stacking independently normalized heatmaps when
        // selecting a herd. Parasites carry no airborne/proximity vectors, so the spread overlay above
        // no-ops for them and only this fires.
        if (__instance.RaceProps?.Animal == true && Find.Selector.SingleSelectedThing == __instance)
        {
            ContagionDeveloperOverlayDrawer.DrawEatingRiskOverlay(__instance);
        }
    }
}
