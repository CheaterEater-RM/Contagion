using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Contagion.Patches;

// Combat Extended compatibility for the per-apparel-row hover tooltip on the Gear tab.
//
// CE's ITab_Inventory.DrawThingRowCE builds each row's tooltip itself from
// thing.LabelCap + thing.DescriptionDetailed + thing.GetWeightAndBulkTip(); it never calls Thing.GetTooltip,
// so Patch_ThingWithComps_GetTooltip_DiseaseProtection can't reach it. We append our protection text to CE's
// own tooltip string via its GetWeightAndBulkTip extension. The Thing overload of GetWeightAndBulkTip is
// called ONLY by DrawThingRowCE (the loadout dialog uses the ThingDef/LoadoutGenericDef overloads), so this
// is scoped to the gear-tab row and does not leak into other CE UI. Reuses BuildItemProtectionTooltip, the
// same helper as the vanilla GetTooltip postfix. Prepare-gated and reflective: no compile-time CE reference.
[HarmonyPatch]
internal static class Patch_CombatExtended_Utility_Loadouts_GetWeightAndBulkTip
{
    private static MethodBase target;

    // Check the CE type with the silent TypeByName first (no log when CE is absent), then resolve the
    // method. Gating on the resolved method means a CE signature change quietly skips the patch; resolving
    // only when CE is present avoids a spurious AccessTools log line for players without CE.
    private static bool Prepare()
    {
        if (AccessTools.TypeByName("CombatExtended.Utility_Loadouts") == null)
        {
            return false;
        }

        target = AccessTools.Method("CombatExtended.Utility_Loadouts:GetWeightAndBulkTip", new[] { typeof(Thing) });
        return target != null;
    }

    private static MethodBase TargetMethod() => target;

    // The extension method's first parameter is named `thing`.
    private static void Postfix(Thing thing, ref string __result)
    {
        if (!(thing is Apparel apparel))
        {
            return;
        }

        string tip = ContagionApparelProtectionUtility.BuildItemProtectionTooltip(apparel);
        if (!tip.NullOrEmpty())
        {
            __result += "\n\n" + tip;
        }
    }
}
