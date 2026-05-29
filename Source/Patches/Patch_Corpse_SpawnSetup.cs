using HarmonyLib;
using RimWorld;
using Verse;

namespace Contagion.Patches;

// When an infected animal dies its corpse is immediately set to Rotting so it cannot be
// butchered and will be hauled away as garbage. Two runtime flags on the map component
// override this per-pawn: butcherBypass (player accepted the risk via gizmo) and forceRot
// (player wants safe disposal of a healthy animal).
[HarmonyPatch(typeof(Corpse), nameof(Corpse.SpawnSetup))]
internal static class Patch_Corpse_SpawnSetup
{
    public static void Postfix(Corpse __instance, Map map, bool respawningAfterLoad)
    {
        if (respawningAfterLoad || __instance?.InnerPawn == null || map == null)
        {
            return;
        }

        if (__instance.InnerPawn.RaceProps?.Animal != true)
        {
            return;
        }

        CompRottable compRottable = __instance.TryGetComp<CompRottable>();
        if (compRottable == null)
        {
            return;
        }

        Contagion_MapTransmissionComponent component = map.GetComponent<Contagion_MapTransmissionComponent>();
        int pawnId = __instance.InnerPawn.thingIDNumber;

        if (component != null && component.ConsumeButcherBypass(pawnId))
        {
            return;
        }

        if (component != null && component.ConsumeForceRot(pawnId))
        {
            compRottable.RotImmediately();
            return;
        }

        if (ContagionAnimalDiseaseUtility.IsAnimalCorpseContagious(__instance.InnerPawn))
        {
            compRottable.RotImmediately();
        }
    }
}
