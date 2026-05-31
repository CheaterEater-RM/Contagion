using HarmonyLib;
using UnityEngine;
using Verse;

namespace Contagion.Patches;

[HarmonyPatch(typeof(PawnRenderNode_AnimalPart), nameof(PawnRenderNode_AnimalPart.GraphicFor))]
internal static class Patch_PawnRenderNode_AnimalPart_GraphicFor
{
    public static void Postfix(Pawn pawn, ref Graphic __result)
    {
        if (__result == null || !ContagionCorpseRenderUtility.IsInfectedCorpsePawn(pawn))
        {
            return;
        }

        Color color = ContagionCorpseRenderUtility.GetInfectedColor(__result.Color);
        Color colorTwo = ContagionCorpseRenderUtility.GetInfectedColor(__result.ColorTwo);
        __result = __result.GetColoredVersion(__result.Shader, color, colorTwo);
    }
}
