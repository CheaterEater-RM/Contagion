using HarmonyLib;
using RimWorld;
using Verse;

namespace Contagion.Patches;

// Tier-2 legibility (design §8): a "Disease protection" block on the pawn Gear tab, drawn right after the
// vanilla Sharp/Blunt/Heat armor summary.
//
// FillTab keeps curY as a local, so a FillTab postfix can't see it. Instead we postfix the per-stat
// TryDrawOverallArmor and act only after the last (Heat) armor row, advancing the same ref curY so the
// tab's scroll height stays correct. Shared draw logic lives in GearTabDiseaseProtectionRenderer, reused by
// the Combat Extended compat patch (Patch_CombatExtended_ITab_Inventory_OverallArmor).
[HarmonyPatch(typeof(ITab_Pawn_Gear), "TryDrawOverallArmor")]
internal static class Patch_ITab_Pawn_Gear_DiseaseProtection
{
    public static void Postfix(ITab_Pawn_Gear __instance, ref float curY, float width, StatDef stat)
    {
        GearTabDiseaseProtectionRenderer.DrawSummary(__instance, ref curY, width, stat);
    }
}
