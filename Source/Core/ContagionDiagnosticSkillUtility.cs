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
// Sigmoid: ~50% at score 7, ~75% at score 10, ~95% at score 15, ~99% at score 20.
internal static class ContagionDiagnosticSkillUtility
{
    private const float SigmoidK = 0.37f;
    private const float SigmoidX0 = 7f;
    private const float MaxChance = 0.995f;

    internal static float ComputeDiagnosticChance(Pawn observer, bool isAnimalSubject, bool isButchery)
    {
        if (observer?.skills == null || observer.health?.capacities == null)
            return 0f;

        float medical = observer.skills.GetSkill(SkillDefOf.Medicine).Level;

        if (ModsConfig.IdeologyActive && observer.Ideo != null)
        {
            Precept_Role role = observer.Ideo.GetRole(observer);
            if (role?.def.roleTags?.Contains("MedicalSpecialist") == true)
                medical *= 1.5f;
        }

        float gap = Mathf.Max(0f, 1f - medical / 20f);

        float support = 0f;
        if (isAnimalSubject)
            support += observer.skills.GetSkill(SkillDefOf.Animals).Level * (isButchery ? 0.25f : 0.60f);
        if (isButchery)
            support += observer.skills.GetSkill(SkillDefOf.Cooking).Level * 0.60f;
        support = Mathf.Min(support, 14f);

        float sight = Mathf.Clamp(observer.health.capacities.GetLevel(PawnCapacityDefOf.Sight), 0.3f, 1.4f);
        float score = (medical + support * gap) * sight;

        float sigmoid = 1f / (1f + Mathf.Exp(-SigmoidK * (score - SigmoidX0)));
        return Mathf.Min(sigmoid, MaxChance);
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
        if (observer?.skills == null || observer.health?.capacities == null)
            return 0f;

        float medical = observer.skills.GetSkill(SkillDefOf.Medicine).Level;

        if (ModsConfig.IdeologyActive && observer.Ideo != null)
        {
            Precept_Role role = observer.Ideo.GetRole(observer);
            if (role?.def.roleTags?.Contains("MedicalSpecialist") == true)
                medical *= 1.5f;
        }

        float support = 0f;
        if (isAnimalSubject)
            support += observer.skills.GetSkill(SkillDefOf.Animals).Level * 0.60f;
        support = Mathf.Min(support, 14f);

        float sight = Mathf.Clamp(observer.health.capacities.GetLevel(PawnCapacityDefOf.Sight), 0.3f, 1.4f);
        float score = (medical + support) * sight;

        float sigmoid = 1f / (1f + Mathf.Exp(-InspectionSigmoidK * (score - InspectionSigmoidX0)));
        return Mathf.Min(sigmoid, InspectionMaxChance);
    }
}
