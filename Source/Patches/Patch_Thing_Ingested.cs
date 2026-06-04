using HarmonyLib;
using RimWorld;
using Verse;

namespace Contagion.Patches;

[HarmonyPatch(typeof(Thing), nameof(Thing.Ingested))]
internal static class Patch_Thing_Ingested
{
    public static void Prefix(Thing __instance, Pawn ingester, out ContagionIngestionContext __state)
    {
        __state = CaptureIngestionContext(__instance, ingester);
        ContagionCorpseUtility.NotifyCorpseIngested(__instance as Corpse, ingester);
    }

    public static void Postfix(Pawn ingester, ContagionIngestionContext __state)
    {
        if (!__state.IsValid)
        {
            return;
        }

        __state.Map.GetComponent<Contagion_MapTransmissionComponent>()?.NotifyAnimalIngested(ingester, __state);
    }

    private static ContagionIngestionContext CaptureIngestionContext(Thing food, Pawn ingester)
    {
        if (food == null || ingester?.RaceProps?.Animal != true)
        {
            return default;
        }

        Map map = food.MapHeld ?? ingester.MapHeld;
        if (map == null)
        {
            return default;
        }

        IntVec3 cell = food.SpawnedOrAnyParentSpawned ? food.PositionHeld : ingester.PositionHeld;
        if (!cell.IsValid || !cell.InBounds(map))
        {
            return default;
        }

        // "Stored" means kept in a storage building (shelf, hopper, feeding item) — the cleanest case.
        // Food loose in a stockpile zone on the floor is NOT counted here, so kibble in a zone still
        // classifies as prepared-ground feed rather than the near-zero shelved tier.
        bool isStored = food.Spawned && food.GetSlotGroup()?.parent is Building;
        // Single indoor/outdoor authority shared with the shedding/filth-contamination passes: indoor =
        // bounded enclosed room (not merely roofed). The eating route is outdoor-only; NotifyAnimalIngested
        // uses IsIndoor to defer enclosed-room exposure to the cleanable-filth living route.
        bool isIndoor = ContagionTransmissionUtility.IsIndoorBarnCell(cell, map);
        return new ContagionIngestionContext(
            map,
            cell,
            food.def,
            food is Plant,
            isStored,
            isIndoor,
            !isIndoor);
    }
}
