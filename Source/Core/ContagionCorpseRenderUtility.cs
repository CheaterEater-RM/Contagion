using UnityEngine;
using Verse;

namespace Contagion;

public static class ContagionCorpseRenderUtility
{
    private static readonly Color InfectionTint = new Color(0.44f, 0.72f, 0.40f);

    public static bool IsInfectedCorpsePawn(Pawn pawn)
    {
        if (pawn?.Dead != true)
        {
            return false;
        }

        Comp_InfectedCorpse comp = pawn.Corpse?.TryGetComp<Comp_InfectedCorpse>();
        return comp?.TryGetDiseaseForDisplay(out HediffDef _) == true || comp?.IsSuspectedInfected == true;
    }

    public static Color GetInfectedColor(Color color)
    {
        return new Color(
            color.r * 0.55f + InfectionTint.r * 0.45f,
            color.g * 0.55f + InfectionTint.g * 0.45f,
            color.b * 0.55f + InfectionTint.b * 0.45f,
            color.a);
    }
}
