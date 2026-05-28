using HarmonyLib;
using RimWorld;
using Verse;

namespace Contagion.Patches;

// Quest-driven arrivals (refugees, lodgers, shuttle allies, returning lent pawns, reward joiners)
// run through QuestPart_PawnsArrive when their inSignal fires. SeedArrivals self-guards on
// Spawned, so edge-walk-in arrivals are seeded and drop-pod-mode arrivals are skipped (the
// pawns are still inside the incoming pod and not yet spawned on the map at this point).
[HarmonyPatch(typeof(QuestPart_PawnsArrive), nameof(QuestPart_PawnsArrive.Notify_QuestSignalReceived))]
internal static class Patch_QuestPart_PawnsArrive_Notify_QuestSignalReceived
{
    public static void Postfix(QuestPart_PawnsArrive __instance)
    {
        if (__instance?.pawns == null || __instance.pawns.Count == 0)
        {
            return;
        }

        ContagionArrivalUtility.SeedArrivals(__instance.pawns);
    }
}
