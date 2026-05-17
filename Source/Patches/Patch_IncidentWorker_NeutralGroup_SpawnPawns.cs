using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Contagion.Patches;

[HarmonyPatch(typeof(IncidentWorker_NeutralGroup), "SpawnPawns", new[] { typeof(IncidentParms) })]
internal static class Patch_IncidentWorker_NeutralGroup_SpawnPawns
{
    public static void Postfix(IncidentParms parms, List<Pawn> __result)
    {
        if (__result == null || __result.Count == 0 || parms?.target is not Map)
        {
            return;
        }

        ContagionArrivalUtility.SeedArrivals(__result);
    }
}