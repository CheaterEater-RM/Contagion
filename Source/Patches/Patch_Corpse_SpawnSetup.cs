using HarmonyLib;
using RimWorld;
using Verse;

namespace Contagion.Patches;

// Infected corpses stay fresh and carry their infection marker on Comp_InfectedCorpse.
// The only remaining immediate-rot path is the explicit disposal override.
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

        Contagion_MapTransmissionComponent component = map.GetComponent<Contagion_MapTransmissionComponent>();
        if (component?.ConsumeForceRot(__instance.InnerPawn.thingIDNumber) != true)
        {
            return;
        }

        __instance.TryGetComp<CompRottable>()?.RotImmediately();
    }
}
