using HarmonyLib;
using RimWorld;
using Verse;

namespace Contagion.Patches;

[HarmonyPatch(typeof(IncidentWorker_WandererJoin), nameof(IncidentWorker_WandererJoin.SpawnJoiner))]
internal static class Patch_IncidentWorker_WandererJoin_SpawnJoiner
{
    public static void Postfix(Pawn pawn)
    {
        if (pawn == null)
        {
            return;
        }

        ContagionArrivalUtility.SeedArrivals(Gen.YieldSingle(pawn));
    }
}
