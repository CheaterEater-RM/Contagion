using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Contagion.Patches;

[HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
internal static class Patch_Pawn_GetGizmos
{
    public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
    {
        if (__instance == null || !__instance.Spawned || __instance.Map == null)
        {
            return;
        }

        List<Gizmo> gizmos = __result == null ? new List<Gizmo>() : new List<Gizmo>(__result);

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
                defaultLabel = component.DeveloperDiagnostics.TraceCaptureEnabled
                    ? "Contagion_DeveloperTraceCaptureOn".Translate()
                    : "Contagion_DeveloperTraceCaptureOff".Translate(),
                defaultDesc = "Contagion_DeveloperTraceCaptureDesc".Translate(),
                icon = component.DeveloperDiagnostics.TraceCaptureEnabled ? TexCommand.ForbidOff : TexCommand.ForbidOn,
                Order = -93.5f,
                action = component.DeveloperDiagnostics.ToggleTraceCapture
            });
        }

        if (component?.DeveloperDiagnostics.HasTracesForPawn(__instance) == true)
        {
            gizmos.Add(new Command_Action
            {
                defaultLabel = "Contagion_DeveloperClearPawnTraces".Translate(),
                defaultDesc = "Contagion_DeveloperClearPawnTracesDesc".Translate(),
                icon = TexCommand.ClearPrioritizedWork,
                Order = -93f,
                action = delegate
                {
                    component.DeveloperDiagnostics.ClearTracesForPawn(__instance);
                }
            });
        }

        __result = gizmos;
    }
}
