using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Contagion.Patches;

// Combat Extended compatibility for the Gear-tab "Disease protection" summary block.
//
// CE swaps the vanilla Gear tab for CombatExtended.ITab_Inventory (a subclass of ITab_Pawn_Gear) that
// reimplements FillTab and calls its OWN private TryDrawOverallArmor(Dictionary, ref curY, ...) — never the
// vanilla overload Patch_ITab_Pawn_Gear_DiseaseProtection rides, so that patch is dead under CE. This
// postfix re-adds the block after CE's final (Heat) armor row. CE draws Blunt -> Sharp -> Heat (Heat last),
// matching the vanilla slot. No compile-time CE reference: the target is resolved reflectively by name and
// __instance is typed as the base ITab_Pawn_Gear. Mutually exclusive with the vanilla patch at runtime
// (CE replaces the tab class for all pawns), so there is no double-draw.
[HarmonyPatch]
internal static class Patch_CombatExtended_ITab_Inventory_OverallArmor
{
    private static MethodBase target;

    // Check the CE type with the silent TypeByName first (no log when CE is absent), then resolve the
    // method. Gating on the resolved method means a CE signature change quietly skips the patch instead of
    // throwing at PatchAll time; resolving only when CE is present avoids a spurious AccessTools log line
    // for players without CE.
    private static bool Prepare()
    {
        if (AccessTools.TypeByName("CombatExtended.ITab_Inventory") == null)
        {
            return false;
        }

        target = AccessTools.Method(
            "CombatExtended.ITab_Inventory:TryDrawOverallArmor",
            new[]
            {
                typeof(Dictionary<BodyPartRecord, float>), typeof(float).MakeByRefType(),
                typeof(float), typeof(StatDef), typeof(string), typeof(string)
            });
        return target != null;
    }

    private static MethodBase TargetMethod() => target;

    // Harmony binds postfix params by name; CE names them curY/width/stat.
    private static void Postfix(ITab_Pawn_Gear __instance, ref float curY, float width, StatDef stat)
    {
        GearTabDiseaseProtectionRenderer.DrawSummary(__instance, ref curY, width, stat);
    }
}
