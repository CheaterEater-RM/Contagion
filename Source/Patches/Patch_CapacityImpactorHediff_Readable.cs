using HarmonyLib;
using Verse;

namespace Contagion.Patches;

// Anonymizes the cause of a capacity reduction in the health-tab capacity-breakdown
// tooltip when the responsible hediff is hidden (e.g. an undiagnosed Contagion disease).
//
// Vanilla already filters non-Visible hediffs out of the hediff list itself
// (HediffSet.VisibleHediffs), but PawnCapacityUtility still applies a hidden hediff's
// capMods to the capacity level and pulls its label through CapacityImpactorHediff.Readable
// for the breakdown tooltip — leaking the disease name. We keep the visible reduction
// (so the player still sees "something is wrong") but replace the named cause with a
// generic label. Postfix + narrow condition keeps this conflict-safe (Hard Rule #6).
[HarmonyPatch(typeof(PawnCapacityUtility.CapacityImpactorHediff), nameof(PawnCapacityUtility.CapacityImpactorHediff.Readable))]
internal static class Patch_CapacityImpactorHediff_Readable
{
    public static void Postfix(PawnCapacityUtility.CapacityImpactorHediff __instance, ref string __result)
    {
        if (__instance.hediff != null && !__instance.hediff.Visible)
        {
            __result = "Contagion_UnknownCapacityCause".Translate();
        }
    }
}
