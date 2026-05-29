using HarmonyLib;
using Verse;

namespace Contagion.Patches;

// Prevent sick-signalled animals from entering the auto-slaughter queue. The player must
// either diagnose (clear the signal) or manually choose slaughter via the pawn gizmos.
[HarmonyPatch(typeof(AutoSlaughterManager), nameof(AutoSlaughterManager.CanAutoSlaughterNow))]
internal static class Patch_AutoSlaughter_Contagion
{
    public static void Postfix(Pawn animal, ref bool __result)
    {
        if (!__result || animal == null)
        {
            return;
        }

        if (animal.health.hediffSet.HasHediff(ContagionDefOf.Contagion_AnimalSick))
        {
            __result = false;
        }
    }
}
