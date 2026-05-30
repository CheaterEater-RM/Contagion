using HarmonyLib;
using RimWorld;
using Verse;

namespace Contagion.Patches;

[HarmonyPatch(typeof(FloatMenuOptionProvider_Ingest), "GetSingleOptionFor")]
internal static class Patch_FloatMenuOptionProvider_Ingest
{
    public static void Postfix(Thing clickedThing, FloatMenuContext context, ref FloatMenuOption __result)
    {
        if (__result == null
            || context?.FirstSelectedPawn?.RaceProps?.Humanlike != true
            || !ContagionCorpseUtility.TryGetInfectedDisease(clickedThing as Corpse, out HediffDef diseaseDef))
        {
            return;
        }

        __result.Label = __result.Label + " (" + "Contagion_InfectedCorpseFloatMenuWarning".Translate() + ")";
        __result.tooltip = new TipSignal("Contagion_InfectedCorpseFloatMenuTooltip".Translate(diseaseDef.LabelCap));
    }
}
