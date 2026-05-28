using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Contagion.Patches;

internal static class Patch_NeutralArrivalCompletion_Helper
{
    public static void Prefix(IncidentParms parms, out HashSet<Pawn> __state)
    {
        __state = SnapshotSpawnedPawns(parms?.target as Map);
    }

    public static void Postfix(IncidentParms parms, bool __result, HashSet<Pawn> __state)
    {
        if (!__result || parms?.target is not Map map)
        {
            return;
        }

        SeedNewSpawnedPawns(map, __state);
    }

    private static HashSet<Pawn> SnapshotSpawnedPawns(Map map)
    {
        HashSet<Pawn> pawns = new HashSet<Pawn>();
        IReadOnlyList<Pawn> spawnedPawns = map?.mapPawns?.AllPawnsSpawned;
        if (spawnedPawns == null)
        {
            return pawns;
        }

        for (int i = 0; i < spawnedPawns.Count; i++)
        {
            Pawn pawn = spawnedPawns[i];
            if (pawn != null)
            {
                pawns.Add(pawn);
            }
        }

        return pawns;
    }

    private static void SeedNewSpawnedPawns(Map map, HashSet<Pawn> preExistingPawns)
    {
        IReadOnlyList<Pawn> spawnedPawns = map?.mapPawns?.AllPawnsSpawned;
        if (spawnedPawns == null)
        {
            return;
        }

        List<Pawn> arrivals = new List<Pawn>();
        for (int i = 0; i < spawnedPawns.Count; i++)
        {
            Pawn pawn = spawnedPawns[i];
            if (pawn == null
                || pawn.Dead
                || !pawn.Spawned
                || pawn.Map != map
                || preExistingPawns.Contains(pawn)
                || pawn.Faction?.HostileTo(Faction.OfPlayer) == true)
            {
                continue;
            }

            arrivals.Add(pawn);
        }

        if (arrivals.Count > 0)
        {
            ContagionArrivalUtility.SeedArrivals(arrivals);
        }
    }
}

[HarmonyPatch(typeof(IncidentWorker_TraderCaravanArrival), "TryExecuteWorker")]
internal static class Patch_IncidentWorker_TraderCaravanArrival_TryExecuteWorker
{
    public static void Prefix(IncidentParms parms, out HashSet<Pawn> __state)
    {
        Patch_NeutralArrivalCompletion_Helper.Prefix(parms, out __state);
    }

    public static void Postfix(IncidentParms parms, bool __result, HashSet<Pawn> __state)
    {
        Patch_NeutralArrivalCompletion_Helper.Postfix(parms, __result, __state);
    }
}

[HarmonyPatch(typeof(IncidentWorker_TravelerGroup), "TryExecuteWorker")]
internal static class Patch_IncidentWorker_TravelerGroup_TryExecuteWorker
{
    public static void Prefix(IncidentParms parms, out HashSet<Pawn> __state)
    {
        Patch_NeutralArrivalCompletion_Helper.Prefix(parms, out __state);
    }

    public static void Postfix(IncidentParms parms, bool __result, HashSet<Pawn> __state)
    {
        Patch_NeutralArrivalCompletion_Helper.Postfix(parms, __result, __state);
    }
}

[HarmonyPatch(typeof(IncidentWorker_VisitorGroup), "TryExecuteWorker")]
internal static class Patch_IncidentWorker_VisitorGroup_TryExecuteWorker
{
    public static void Prefix(IncidentParms parms, out HashSet<Pawn> __state)
    {
        Patch_NeutralArrivalCompletion_Helper.Prefix(parms, out __state);
    }

    public static void Postfix(IncidentParms parms, bool __result, HashSet<Pawn> __state)
    {
        Patch_NeutralArrivalCompletion_Helper.Postfix(parms, __result, __state);
    }
}
