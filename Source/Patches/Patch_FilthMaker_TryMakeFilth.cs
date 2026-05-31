using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Contagion.Patches;

[HarmonyPatch(typeof(FilthMaker), nameof(FilthMaker.TryMakeFilth), new[] { typeof(IntVec3), typeof(Map), typeof(ThingDef), typeof(string), typeof(int), typeof(FilthSourceFlags) })]
internal static class Patch_FilthMaker_TryMakeFilth
{
    public static void Postfix(IntVec3 c, Map map, ThingDef filthDef, string source, bool __result)
    {
        if (!__result || map == null || filthDef != ThingDefOf.Filth_Vomit)
        {
            return;
        }

        Filth filth = null;
        List<Thing> things = c.GetThingList(map);
        for (int i = 0; i < things.Count; i++)
        {
            if (things[i] is Filth existingFilth && existingFilth.def == filthDef)
            {
                filth = existingFilth;
                break;
            }
        }

        if (filth == null)
        {
            return;
        }

        Pawn vomitingPawn = FindVomitingPawn(map, c, source);
        if (vomitingPawn == null)
        {
            return;
        }

        map.GetComponent<Contagion_MapTransmissionComponent>()?.NotifyVomitFilthCreated(filth, vomitingPawn);
    }

    private static Pawn FindVomitingPawn(Map map, IntVec3 cell, string source)
    {
        for (int i = 0; i < map.mapPawns.AllPawnsSpawned.Count; i++)
        {
            Pawn pawn = map.mapPawns.AllPawnsSpawned[i];
            if (pawn == null || pawn.Dead || pawn.CurJobDef != JobDefOf.Vomit || pawn.CurJob == null)
            {
                continue;
            }

            if (pawn.CurJob.targetA.Cell != cell)
            {
                continue;
            }

            if (!source.NullOrEmpty() && pawn.LabelIndefinite() != source)
            {
                continue;
            }

            return pawn;
        }

        return null;
    }
}
