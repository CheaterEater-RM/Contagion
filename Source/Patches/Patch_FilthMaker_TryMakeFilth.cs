using System.Collections.Generic;
using System.Reflection;
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

[HarmonyPatch]
internal static class Patch_FilthMaker_TryMakeFilth_AnimalFilth
{
    public static MethodBase TargetMethod()
    {
        return AccessTools.Method(
            typeof(FilthMaker),
            nameof(FilthMaker.TryMakeFilth),
            new[]
            {
                typeof(IntVec3),
                typeof(Map),
                typeof(ThingDef),
                typeof(IEnumerable<string>),
                typeof(bool),
                typeof(Filth).MakeByRefType(),
                typeof(FilthSourceFlags)
            });
    }

    public static void Postfix(
        IntVec3 c,
        Map map,
        ThingDef filthDef,
        bool shouldPropagate,
        ref Filth outFilth,
        FilthSourceFlags additionalFlags,
        bool __result)
    {
        if (!__result
            || !shouldPropagate
            || map == null
            || outFilth == null
            || filthDef != ThingDefOf.Filth_AnimalFilth
            || (additionalFlags & FilthSourceFlags.Pawn) == 0)
        {
            return;
        }

        Pawn sourcePawn = FindAnimalAtCell(map, c);
        if (sourcePawn == null)
        {
            return;
        }

        map.GetComponent<Contagion_MapTransmissionComponent>()?.NotifyAnimalFilthCreated(outFilth, sourcePawn);
    }

    private static Pawn FindAnimalAtCell(Map map, IntVec3 cell)
    {
        List<Thing> things = cell.GetThingList(map);
        for (int i = 0; i < things.Count; i++)
        {
            if (things[i] is Pawn pawn && pawn.RaceProps?.Animal == true && !pawn.Dead)
            {
                return pawn;
            }
        }

        return null;
    }
}
