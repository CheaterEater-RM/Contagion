using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Contagion.Patches;

[HarmonyPatch(typeof(IncidentWorker_Raid), "PostProcessSpawnedPawns")]
internal static class Patch_IncidentWorker_Raid_PostProcessSpawnedPawns
{
    public static void Postfix(IncidentParms parms, List<Pawn> pawns)
    {
        if (pawns == null || pawns.Count == 0 || parms?.target is not Map map)
        {
            return;
        }

        if (parms.faction == null || !parms.faction.HostileTo(Faction.OfPlayer))
        {
            return;
        }

        List<Pawn> livePawns = new List<Pawn>();
        for (int i = 0; i < pawns.Count; i++)
        {
            if (pawns[i] != null && !pawns[i].Dead && pawns[i].Spawned && pawns[i].Map == map)
            {
                livePawns.Add(pawns[i]);
            }
        }

        if (livePawns.Count == 0)
        {
            return;
        }

        livePawns.Shuffle();
        for (int i = 0; i < livePawns.Count; i++)
        {
            // One infectious arrival max per hostile raid group.
            if (ContagionArrivalUtility.TrySeedRaidPawn(livePawns[i]))
            {
                break;
            }
        }
    }
}
