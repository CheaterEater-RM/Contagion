using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion.Patches;

[HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
internal static class Patch_Pawn_GetGizmos
{
    // Lazy-loaded icon textures — loaded once, then cached.
    private static Texture2D _iconDispose;
    private static Texture2D _iconButcherAnyway;

    private static Texture2D IconDispose =>
        _iconDispose ??= ContentFinder<Texture2D>.Get("UI/Designators/Slaughter");

    private static Texture2D IconButcherAnyway =>
        _iconButcherAnyway ??= ContentFinder<Texture2D>.Get("UI/Designators/Slaughter");

    public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
    {
        if (__instance == null || !__instance.Spawned || __instance.Map == null)
        {
            return;
        }

        List<Gizmo> gizmos = __result == null ? new List<Gizmo>() : new List<Gizmo>(__result);

        // Animal slaughter gizmos — shown when the pawn is a player-faction animal.
        if (__instance.RaceProps?.Animal == true
            && AutoSlaughterManager.CanEverAutoSlaughter(__instance)
            && __instance.Faction == Faction.OfPlayer)
        {
            AddAnimalSlaughterGizmos(__instance, gizmos);
        }

        if (Contagion_Mod.Settings?.DeveloperDiagnosticsEnabled != true)
        {
            __result = gizmos;
            return;
        }
        gizmos.Add(new Command_ContagionDeveloperSeedDisease(__instance));

        Contagion_MapTransmissionComponent component = __instance.Map.GetComponent<Contagion_MapTransmissionComponent>();
        if (component != null)
        {
            gizmos.Add(new Command_Action
            {
                defaultLabel = component.DeveloperTraceCaptureEnabled
                    ? "Contagion_DeveloperTraceCaptureOn".Translate()
                    : "Contagion_DeveloperTraceCaptureOff".Translate(),
                defaultDesc = "Contagion_DeveloperTraceCaptureDesc".Translate(),
                icon = component.DeveloperTraceCaptureEnabled ? TexCommand.ForbidOff : TexCommand.ForbidOn,
                Order = -93.5f,
                action = component.DeveloperToggleTraceCapture
            });
        }

        if (component?.DeveloperHasTracesForPawn(__instance) == true)
        {
            gizmos.Add(new Command_Action
            {
                defaultLabel = "Contagion_DeveloperClearPawnTraces".Translate(),
                defaultDesc = "Contagion_DeveloperClearPawnTracesDesc".Translate(),
                icon = TexCommand.ClearPrioritizedWork,
                Order = -93f,
                action = delegate
                {
                    component.DeveloperClearTracesForPawn(__instance);
                }
            });
        }

        __result = gizmos;
    }

    private static void AddAnimalSlaughterGizmos(Pawn animal, List<Gizmo> gizmos)
    {
        bool isSick = animal.health.hediffSet.HasHediff(ContagionDefOf.Contagion_AnimalSick);
        Contagion_MapTransmissionComponent component = animal.Map.GetComponent<Contagion_MapTransmissionComponent>();

        // "Slaughter and dispose" — available for all colony animals.
        // Adds a slaughter designation and arms the force-rot flag so the corpse spawns rotten.
        gizmos.Add(new Command_Action
        {
            defaultLabel = "Contagion_GizmoSlaughterDispose".Translate(),
            defaultDesc = "Contagion_GizmoSlaughterDisposeDesc".Translate(),
            icon = IconDispose,
            Order = -95f,
            action = delegate
            {
                if (component != null)
                {
                    component.ArmForceRot(animal.thingIDNumber);
                }

                if (animal.Map.designationManager.DesignationOn(animal, DesignationDefOf.Slaughter) == null)
                {
                    animal.Map.designationManager.AddDesignation(new Designation(animal, DesignationDefOf.Slaughter));
                }
            }
        });

        // "Slaughter and butcher anyway" — only when a sick signal is present.
        // Arms the butcher-bypass flag so the corpse spawns fresh despite disease.
        if (isSick)
        {
            gizmos.Add(new Command_Action
            {
                defaultLabel = "Contagion_GizmoSlaughterButcher".Translate(),
                defaultDesc = "Contagion_GizmoSlaughterButcherDesc".Translate(),
                icon = IconButcherAnyway,
                Order = -94.5f,
                action = delegate
                {
                    if (component != null)
                    {
                        component.ArmButcherBypass(animal.thingIDNumber);
                    }

                    if (animal.Map.designationManager.DesignationOn(animal, DesignationDefOf.Slaughter) == null)
                    {
                        animal.Map.designationManager.AddDesignation(new Designation(animal, DesignationDefOf.Slaughter));
                    }
                }
            });
        }
    }
}