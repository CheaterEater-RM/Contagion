using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Contagion.Patches;

[HarmonyPatch(typeof(IncidentWorker_WandererJoin), "TryExecuteWorker")]
internal static class Patch_IncidentWorker_WandererJoin_TryExecuteWorker
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

        IReadOnlyList<Pawn> spawnedPawns = map.mapPawns?.AllPawnsSpawned;
        if (spawnedPawns == null)
        {
            return;
        }

        List<Pawn> arrivals = new List<Pawn>();
        for (int i = 0; i < spawnedPawns.Count; i++)
        {
            Pawn pawn = spawnedPawns[i];
            if (pawn != null
                && !pawn.Dead
                && pawn.Spawned
                && pawn.Map == map
                && !__state.Contains(pawn))
            {
                arrivals.Add(pawn);
            }
        }

        if (arrivals.Count > 0)
        {
            ContagionSeedingCoordinator.HandleArrivalGroup(arrivals, ContagionArrivalGroupKind.WandererJoin);
        }
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
}
