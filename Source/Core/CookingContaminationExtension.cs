using Verse;

namespace Contagion;

// Attached to RecipeDefs via XML to control how much contamination survives cooking.
// 0 = fully safe (nutrient paste), 1 = raw (no reduction). Default 0.2 (fine-meal level).
public sealed class CookingContaminationExtension : DefModExtension
{
    public float reductionFactor = 0.2f;
}
