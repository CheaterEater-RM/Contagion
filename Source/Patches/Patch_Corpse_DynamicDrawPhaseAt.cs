using HarmonyLib;
using UnityEngine;
using Verse;

namespace Contagion.Patches;

[HarmonyPatch(typeof(Corpse), nameof(Corpse.DynamicDrawPhaseAt))]
internal static class Patch_Corpse_DynamicDrawPhaseAt
{
    public static void Postfix(Corpse __instance, DrawPhase phase, Vector3 drawLoc)
    {
        if (phase != DrawPhase.Draw)
        {
            return;
        }

        __instance?.TryGetComp<Comp_InfectedCorpse>()?.DrawInfectionOverlay(drawLoc);
    }
}
