using System;

namespace Contagion;

public static class ContagionRiskMath
{
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
