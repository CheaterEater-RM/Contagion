using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Contagion.Patches;

// Quest-driven arrivals (refugees, lodgers, shuttle allies, returning lent pawns, reward joiners)
// run through QuestPart_PawnsArrive when their inSignal fires. We seed only pawns that became
// spawned during the matching signal, so unrelated quest signals and failed retargets do not
// consume pending events.
[HarmonyPatch(typeof(QuestPart_PawnsArrive), nameof(QuestPart_PawnsArrive.Notify_QuestSignalReceived))]
internal static class Patch_QuestPart_PawnsArrive_Notify_QuestSignalReceived
{
    public static void Prefix(QuestPart_PawnsArrive __instance, Signal signal, out HashSet<Pawn> __state)
    {
        __state = new HashSet<Pawn>();
        if (__instance?.pawns == null || signal.tag != __instance.inSignal)
        {
            return;
        }

        for (int i = 0; i < __instance.pawns.Count; i++)
        {
            Pawn pawn = __instance.pawns[i];
            if (pawn != null && pawn.Spawned)
            {
                __state.Add(pawn);
            }
        }
    }

    public static void Postfix(QuestPart_PawnsArrive __instance, Signal signal, HashSet<Pawn> __state)
    {
        if (__instance?.pawns == null || __instance.pawns.Count == 0 || signal.tag != __instance.inSignal)
        {
            return;
        }

        List<Pawn> arrivals = new List<Pawn>();
        for (int i = 0; i < __instance.pawns.Count; i++)
        {
            Pawn pawn = __instance.pawns[i];
            if (pawn != null
                && !pawn.Dead
                && pawn.Spawned
                && !__state.Contains(pawn))
            {
                arrivals.Add(pawn);
            }
        }

        if (arrivals.Count > 0)
        {
            ContagionSeedingCoordinator.HandleArrivalGroup(
                arrivals,
                __instance.joinPlayer ? ContagionArrivalGroupKind.QuestJoiner : ContagionArrivalGroupKind.QuestGuest);
        }
    }
}
