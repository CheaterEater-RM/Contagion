using HarmonyLib;
using UnityEngine;
using Verse;

namespace Contagion.Patches;

[HarmonyPatch(typeof(PawnRenderNode), nameof(PawnRenderNode.ColorFor))]
internal static class Patch_PawnRenderNode_ColorFor
{
    public static void Postfix(Pawn pawn, ref Color __result)
    {
        if (ContagionCorpseRenderUtility.IsInfectedCorpsePawn(pawn))
        {
            __result = ContagionCorpseRenderUtility.GetInfectedColor(__result);
        }
    }
}
