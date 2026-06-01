using Verse;

namespace Contagion;

// Singleton Def carrying per-day spontaneous false-positive sick-signal rates for animals.
// Define exactly one instance in XML with defName "Contagion_FalsePositiveTuning".
// Modders can override the values via PatchOperationReplace or a secondary Def.
public sealed class ContagionAnimalFalsePositiveDef : Def
{
    // Chance per game-day per colony animal that it spontaneously shows Contagion_AnimalSick
    // with no underlying disease. Default: 0.2% (~1 false positive per 500 animal-days).
    public float spontaneousSickChanceDomesticPerDay = 0.002f;

    // Chance per game-day per wild (non-colony) animal on the map. Higher than domestic
    // because wild animals are less observed and cannot receive routine handler checks.
    // Default: 0.5% (~1 false positive per 200 animal-days).
    public float spontaneousSickChanceWildPerDay = 0.005f;

    public static ContagionAnimalFalsePositiveDef Active =>
        DefDatabase<ContagionAnimalFalsePositiveDef>.GetNamed("Contagion_FalsePositiveTuning", errorOnFail: false);
}
