using HarmonyLib;
using RimWorld;
using Verse;

namespace Contagion.Patches;

[HarmonyPatch(typeof(ThingWithComps), nameof(ThingWithComps.GetTooltip))]
internal static class Patch_ThingWithComps_GetTooltip_DiseaseProtection
{
    public static void Postfix(ThingWithComps __instance, ref TipSignal __result)
    {
        if (!(__instance is Apparel apparel))
        {
            return;
        }

        string tooltip = ContagionApparelProtectionUtility.BuildItemProtectionTooltip(apparel);
        if (tooltip.NullOrEmpty())
        {
            return;
        }

        if (__result.textGetter != null)
        {
            TipSignal original = __result;
            __result.textGetter = () => original.textGetter() + "\n\n" + tooltip;
        }
        else
        {
            __result.text += "\n\n" + tooltip;
        }
    }
}
