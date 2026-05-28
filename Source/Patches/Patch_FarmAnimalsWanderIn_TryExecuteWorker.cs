using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Contagion.Patches;

[HarmonyPatch(typeof(IncidentWorker_FarmAnimalsWanderIn), "TryExecuteWorker")]
internal static class Patch_FarmAnimalsWanderIn_TryExecuteWorker
{
    public static void Prefix(IncidentParms parms, out HashSet<Pawn> __state)
    {
        __state = new HashSet<Pawn>();
        if (parms?.target is not Map map)
        {
            return;
        }

        IReadOnlyList<Pawn> existingPawns = map.mapPawns?.AllPawnsSpawned;
        if (existingPawns == null)
        {
            return;
        }

        for (int i = 0; i < existingPawns.Count; i++)
        {
            Pawn pawn = existingPawns[i];
            if (pawn != null && pawn.RaceProps?.Animal == true && pawn.Faction == Faction.OfPlayer)
            {
                __state.Add(pawn);
            }
        }
    }

    public static void Postfix(IncidentParms parms, HashSet<Pawn> __state, bool __result)
    {
        if (!__result || parms?.target is not Map map || __state == null)
        {
            return;
        }

        IReadOnlyList<Pawn> allPawns = map.mapPawns?.AllPawnsSpawned;
        if (allPawns == null)
        {
            return;
        }

        List<Pawn> newAnimals = new List<Pawn>();
        for (int i = 0; i < allPawns.Count; i++)
        {
            Pawn pawn = allPawns[i];
            if (pawn != null
                && pawn.RaceProps?.Animal == true
                && pawn.Faction == Faction.OfPlayer
                && !__state.Contains(pawn))
            {
                newAnimals.Add(pawn);
            }
        }

        if (newAnimals.Count > 0)
        {
            ContagionArrivalUtility.SeedArrivalGroup(newAnimals, ContagionArrivalGroupKind.FarmAnimals);
        }
    }
}
