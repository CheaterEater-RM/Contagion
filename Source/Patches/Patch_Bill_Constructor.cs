using HarmonyLib;
using RimWorld;
using Verse;

namespace Contagion.Patches;

[HarmonyPatch(typeof(Bill), MethodType.Constructor, typeof(RecipeDef), typeof(Precept_ThingStyle))]
internal static class Patch_Bill_Constructor
{
    public static void Postfix(Bill __instance)
    {
        ContagionBillUtility.ApplyButcherBillDefaults(__instance);
    }
}
