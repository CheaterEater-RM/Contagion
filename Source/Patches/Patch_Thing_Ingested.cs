using HarmonyLib;
using Verse;

namespace Contagion.Patches;

[HarmonyPatch(typeof(Thing), nameof(Thing.Ingested))]
internal static class Patch_Thing_Ingested
{
    public static void Prefix(Thing __instance, Pawn ingester)
    {
        ContagionCorpseUtility.NotifyCorpseIngested(__instance as Corpse, ingester);
    }
}
