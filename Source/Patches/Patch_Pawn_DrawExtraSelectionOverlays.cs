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
    }
}