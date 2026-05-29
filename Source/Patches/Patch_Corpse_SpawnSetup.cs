using HarmonyLib;
using RimWorld;
using Verse;

namespace Contagion.Patches;

// When an infected animal or humanlike pawn dies, its corpse is immediately set to Rotting
// so it cannot be butchered and will be hauled away as garbage.
//
// Animals: two runtime flags on the map component override this per-pawn — butcherBypass
// (player accepted the risk via gizmo) and forceRot (safe disposal of a healthy animal).
// Humans: always rot immediately; there is no bypass gizmo for humanlike corpses.
[HarmonyPatch(typeof(Corpse), nameof(Corpse.SpawnSetup))]
internal static class Patch_Corpse_SpawnSetup
{
    public static void Postfix(Corpse __instance, Map map, bool respawningAfterLoad)
    {
        if (respawningAfterLoad || __instance?.InnerPawn == null || map == null)
        {
            return;
        }

        Pawn innerPawn = __instance.InnerPawn;
        CompRottable compRottable = __instance.TryGetComp<CompRottable>();
        if (compRottable == null)
        {
            return;
        }

        if (innerPawn.RaceProps?.Animal == true)
        {
            Contagion_MapTransmissionComponent component = map.GetComponent<Contagion_MapTransmissionComponent>();
            int pawnId = innerPawn.thingIDNumber;

            if (component != null && component.ConsumeButcherBypass(pawnId))
            {
                return;
            }

            if (component != null && component.ConsumeForceRot(pawnId))
            {
                compRottable.RotImmediately();
                return;
            }

            if (ContagionAnimalDiseaseUtility.IsAnimalCorpseContagious(innerPawn))
            {
                compRottable.RotImmediately();
            }
        }
    }
}
