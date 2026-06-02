using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

// Unified disease-notice chance used by alive-animal diagnosis and butchery discovery.
//
// Medical is the primary skill. Animals supplements when examining an animal subject
// (weight 0.60 for clinical diagnosis, 0.25 for butchery — ranching intuition helps
// more when a live animal is in front of you than when cutting meat). Cooking
// supplements during butchery (knowing what bad meat looks like). Support skills have
// diminishing returns as Medical rises and are capped so they cannot dominate it.
//
// Sight scales the whole score: 30% floor (blind is impaired, not useless), 140% cap
// (bionic eyes). Medical Specialist role (Ideology DLC) gives a 1.5× Medical bonus.
//
// Passive butchery notice is deliberately weak now that players can run a dedicated post-mortem
// inspection (ComputeInspectionChance) to diagnose suspicious corpses on demand. This is the
// "happened to spot it while cutting" fallback, not a reliable screen.
// Sigmoid: ~15% at score 6, ~50% at score 12, ~75% at score 16, ~88% at score 20.
//   e.g. medical 2 / animals 7 / cooking 4 → ~15% (was ~34%); medical 10 → ~50% (was ~75%).
internal static class ContagionDiagnosticSkillUtility
{
    private const float SigmoidK = 0.275f;
    private const float SigmoidX0 = 12f;
    private const float MaxChance = 0.995f;

    internal static float ComputeDiagnosticChance(Pawn observer, bool isAnimalSubject, bool isButchery)
    {
        float animalsWeight = isButchery ? 0.25f : 0.60f;
        float cookingWeight = isButchery ? 0.60f : 0f;
        float score = ComputeSkillScore(observer, isAnimalSubject, animalsWeight, cookingWeight, useMedicalGap: true);
        return SigmoidChance(score, SigmoidK, SigmoidX0, MaxChance);
    }

    // Dedicated post-mortem inspection at a butchery table — more powerful than the
    // passive butchery notice check. Medical is primary (full weight, no gap penalty).
    // Animals supplements when examining an animal corpse (0.60, same as clinical).
    // No Cooking (inspection ≠ butchery). Sight scaling and Medical Specialist apply.
    //
    // Improved sigmoid: x0=5.0, k=0.40 (vs butchery notice x0=7.0, k=0.37).
    // Skill 3≈30%, 5≈50%, 8≈77%, 12≈94%, 15≈98%.
    private const float InspectionSigmoidK = 0.40f;
    private const float InspectionSigmoidX0 = 5f;
    private const float InspectionMaxChance = 0.995f;

    internal static float ComputeInspectionChance(Pawn observer, bool isAnimalSubject)
    {
        float score = ComputeSkillScore(observer, isAnimalSubject, animalsWeight: 0.60f, cookingWeight: 0f, useMedicalGap: false);
        return SigmoidChance(score, InspectionSigmoidK, InspectionSigmoidX0, InspectionMaxChance);
    }

    private static float ComputeSkillScore(Pawn observer, bool isAnimalSubject, float animalsWeight, float cookingWeight, bool useMedicalGap)
    {
        if (observer?.skills == null || observer.health?.capacities == null)
        {
            return 0f;
        }

        float medical = observer.skills.GetSkill(SkillDefOf.Medicine).Level;
        if (ModsConfig.IdeologyActive && observer.Ideo != null)
        {
            Precept_Role role = observer.Ideo.GetRole(observer);
            if (role?.def.roleTags?.Contains("MedicalSpecialist") == true)
            {
                medical *= 1.5f;
            }
        }

        float support = 0f;
        if (isAnimalSubject)
        {
            support += observer.skills.GetSkill(SkillDefOf.Animals).Level * animalsWeight;
        }

        if (cookingWeight > 0f)
        {
            support += observer.skills.GetSkill(SkillDefOf.Cooking).Level * cookingWeight;
        }

        support = Mathf.Min(support, 14f);
        float supportFactor = useMedicalGap ? Mathf.Max(0f, 1f - medical / 20f) : 1f;
        float sight = Mathf.Clamp(observer.health.capacities.GetLevel(PawnCapacityDefOf.Sight), 0.3f, 1.4f);
        return (medical + support * supportFactor) * sight;
    }

    private static float SigmoidChance(float score, float k, float x0, float maxChance)
    {
        float sigmoid = 1f / (1f + Mathf.Exp(-k * (score - x0)));
        return Mathf.Min(sigmoid, maxChance);
    }
}
