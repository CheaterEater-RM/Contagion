using HarmonyLib;
using RimWorld;
using Verse;

namespace Contagion.Patches;

[HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.WillEat), typeof(Pawn), typeof(Thing), typeof(Pawn), typeof(bool), typeof(bool))]
internal static class Patch_FoodUtility_WillEat
{
    public static void Postfix(Pawn p, Thing food, ref bool __result)
    {
        if (!__result || p?.RaceProps?.Humanlike != true)
        {
            return;
        }

        if (ContagionCorpseUtility.IsInfectedCorpse(food))
        {
            __result = false;
        }
    }
}
