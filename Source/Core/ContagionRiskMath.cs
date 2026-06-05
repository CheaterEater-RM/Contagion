using System;

namespace Contagion;

public static class ContagionRiskMath
{
    public static float BiomeDiseaseCommonalityFactor(float commonality)
    {
        // RimWorld authors biome disease commonality around 100 as the neutral baseline.
        return Math.Max(0f, commonality) / 100f;
    }

    public static float SeederBonusChance(float chance)
    {
        return (float)Math.Pow(Clamp01(chance), 1.0 / 3.0);
    }

    public static float ButcheryExposureFactor(float cookingLevel, float medicineLevel, float animalsLevel, bool animalCorpse)
    {
        float skill = cookingLevel + medicineLevel * 0.25f;
        if (animalCorpse)
        {
            skill += animalsLevel * 0.25f;
        }

        float normalizedSkill = Clamp01(skill / 20f);
        return Lerp(1f, 0.45f, normalizedSkill);
    }

    public static float CookingExposureFactor(float cookingLevel, float lowSkillFactor, float highSkillFactor)
    {
        float normalizedCooking = Clamp01(cookingLevel / 20f);
        return Lerp(Math.Max(0f, lowSkillFactor), Math.Max(0f, highSkillFactor), normalizedCooking);
    }

    public static float CookingSurvivalFactor(float cookingLevel, float lowSkillFactor, float skillAsymptoteFactor, float skillDecayRate)
    {
        float low = Math.Max(0f, lowSkillFactor);
        float asymptote = Clamp(skillAsymptoteFactor, 0f, low);
        float decayRate = Math.Max(0f, skillDecayRate);
        return asymptote + (low - asymptote) * (float)Math.Exp(-decayRate * cookingLevel);
    }

    public static float Combined(params float[] chances)
    {
        if (chances == null || chances.Length == 0)
        {
            return 0f;
        }

        float miss = 1f;
        for (int i = 0; i < chances.Length; i++)
        {
            miss *= 1f - Clamp01(chances[i]);
        }

        return 1f - miss;
    }

    public static float ApplyIngestionResistance(float baseChance, float ingestionResistanceFactor)
    {
        return Math.Max(0f, baseChance) * Math.Max(0f, ingestionResistanceFactor);
    }

    // Stored fecal-oral hotspot potency after time decay. Weather is deliberately excluded because
    // rain/freezing only suppress current infectivity and must not permanently delete the node.
    public static float HotspotPotencyAfterDecay(float potency, float decayPerDay, float elapsedDays)
    {
        float retentionPerDay = 1f - Clamp01(decayPerDay);
        return Math.Max(0f, potency)
            * (float)Math.Pow(retentionPerDay, Math.Max(0f, elapsedDays));
    }

    public static float HotspotEffectivePotency(
        float storedPotency,
        bool raining,
        float rainPotencyFactor,
        bool freezing,
        float freezingPotencyFactor)
    {
        float potency = Math.Max(0f, storedPotency);
        if (raining)
        {
            potency *= Clamp01(rainPotencyFactor);
        }

        if (freezing)
        {
            potency *= Clamp01(freezingPotencyFactor);
        }

        return potency;
    }

    public static float AddHotspotPotency(float decayedExistingPotency, float newPotency, float maxPotency)
    {
        return Math.Min(
            Math.Max(0f, decayedExistingPotency) + Math.Max(0f, newPotency),
            Math.Max(0f, maxPotency));
    }

    public static bool ShouldCleanupHotspot(
        float potency,
        float decayPerDay,
        float elapsedDays,
        float minimumPotency)
    {
        return HotspotPotencyAfterDecay(potency, decayPerDay, elapsedDays) <= Math.Max(0f, minimumPotency);
    }

    public static float VisibleChanceMaximum(float currentMaximum, float chance, bool visible, float minimumChance)
    {
        float maximum = Math.Max(0f, currentMaximum);
        return visible && chance >= Math.Max(0f, minimumChance)
            ? Math.Max(maximum, Math.Max(0f, chance))
            : maximum;
    }

    // Protection from one channel against one vector (design §3.2). At seal = 0 protection is
    // coverage * unsealedEff (mere fabric); at seal = 1 it rises to full coverage. Naked (coverage 0)
    // is always 0 regardless of unsealedEff.
    public static float ChannelProtection(float coverage, float seal, float unsealedEffectiveness)
    {
        float floor = Clamp01(unsealedEffectiveness);
        return Clamp01(coverage) * (floor + (1f - floor) * Clamp01(seal));
    }

    // Weighted sum of per-channel protection (design §3.2). Each entry is (weight, coverage, seal).
    // Weights need not sum to 1; the result is clamped 0-1. unsealedEffectiveness applies per channel.
    public static float WeightedProtection((float weight, float coverage, float seal)[] channels, float unsealedEffectiveness)
    {
        if (channels == null || channels.Length == 0)
        {
            return 0f;
        }

        float sum = 0f;
        for (int i = 0; i < channels.Length; i++)
        {
            float weight = Math.Max(0f, channels[i].weight);
            if (weight <= 0f)
            {
                continue;
            }

            sum += weight * ChannelProtection(channels[i].coverage, channels[i].seal, unsealedEffectiveness);
        }

        return Clamp01(sum);
    }

    // Banded seal-integrity multiplier from a worn item's HP fraction (design §7), keyed to the vanilla
    // ratty (<50%) / tattered (<20%) states. Full seal until ratty, strong-partial when ratty, moderate
    // when tattered. Dedicated PPE is exempt by the caller, not here.
    public static float SealIntegrity(float hpFraction)
    {
        if (hpFraction >= 0.5f)
        {
            return 1f;
        }

        if (hpFraction >= 0.2f)
        {
            return 0.85f;
        }

        return 0.55f;
    }

    private static float Clamp(float value, float min, float max)
    {
        return Math.Min(Math.Max(value, min), max);
    }

    private static float Clamp01(float value)
    {
        return Clamp(value, 0f, 1f);
    }

    private static float Lerp(float from, float to, float t)
    {
        return from + (to - from) * Clamp01(t);
    }
}
