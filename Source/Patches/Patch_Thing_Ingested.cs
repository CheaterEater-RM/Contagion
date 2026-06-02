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

        bool isStored = food.Spawned && food.IsInValidStorage();
        bool isIndoor = IsIndoorCell(cell, map);
        bool isOutdoor = IsOutdoorCell(cell, map);
        return new ContagionIngestionContext(
            map,
            cell,
            food.def,
            food is Plant,
            isStored,
            isIndoor,
            isOutdoor);
    }

    private static bool IsIndoorCell(IntVec3 cell, Map map)
    {
        Room room = cell.GetRoom(map);
        return cell.Roofed(map)
            || (room != null && !room.PsychologicallyOutdoors && !room.UsesOutdoorTemperature);
    }

    private static bool IsOutdoorCell(IntVec3 cell, Map map)
    {
        return !IsIndoorCell(cell, map);
    }
}
