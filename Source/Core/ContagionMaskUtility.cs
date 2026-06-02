using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

// Respiratory protection piggybacks on the ToxicEnvironmentResistance stat that masks, antitoxic
// lungs, and similar already provide. We deliberately decompose the contributions rather than read
// the pawn's aggregate stat value, because the source of the resistance matters:
//
//   - Worn APPAREL (gas mask, etc.) and BODY-PART HEDIFFS (detoxifier lung, fleshmass lung) form a
//     physical barrier over the airways and count at FULL effect, scaled by how respiratory the
//     vector is (a mask's maskTargetEffectiveness / maskSourceEffectiveness).
//   - GENES count for NOTHING by default, because a gene like "tox-resistant" represents metabolic
//     tolerance, not an airway barrier. Specific genes can be opted back in via RespiratoryImmunityDef
//     (e.g. breathless, which physiologically cannot inhale a pathogen -> full airway immunity).
public static class ContagionMaskUtility
{
    // Sum of ToxicEnvironmentResistance granted by worn apparel (equippedStatOffsets only).
    public static float GetApparelToxicResistance(Pawn pawn)
    {
        List<Apparel> worn = pawn?.apparel?.WornApparel;
        if (worn == null || worn.Count == 0)
        {
            return 0f;
        }

        float sum = 0f;
        for (int i = 0; i < worn.Count; i++)
        {
            List<StatModifier> offsets = worn[i]?.def?.equippedStatOffsets;
            if (offsets != null)
            {
                sum += offsets.GetStatOffsetFromList(StatDefOf.ToxicEnvironmentResistance);
            }
        }

        return sum;
    }

    // Sum of ToxicEnvironmentResistance granted by body-part / implant hediffs (current stage
    // statOffsets). Restricted to installed parts / mutations (detoxifier lung, fleshmass lung, etc.)
    // so a transient drug or disease hediff that happens to offset the stat does not masquerade as a
    // physical airway barrier. Gene contributions are not applied through hediffs at all.
    public static float GetBodyPartToxicResistance(Pawn pawn)
    {
        List<Hediff> hediffs = pawn?.health?.hediffSet?.hediffs;
        if (hediffs == null)
        {
            return 0f;
        }

        float sum = 0f;
        for (int i = 0; i < hediffs.Count; i++)
        {
            Hediff hediff = hediffs[i];
            if (!IsBodyPartHediff(hediff))
            {
                continue;
            }

            List<StatModifier> offsets = hediff.CurStage?.statOffsets;
            if (offsets != null)
            {
                sum += offsets.GetStatOffsetFromList(StatDefOf.ToxicEnvironmentResistance);
            }
        }

        return sum;
    }

    private static bool IsBodyPartHediff(Hediff hediff)
    {
        return hediff?.def != null
            && (hediff is Hediff_AddedPart
                || hediff.def.countsAsAddedPartOrImplant
                || hediff.def.addedPartProps != null);
    }

    // Full-effect physical airway barrier (0-1) from apparel + body parts. Genes excluded.
    public static float GetAirwayBarrierResistance(Pawn pawn)
    {
        return Mathf.Clamp01(GetApparelToxicResistance(pawn) + GetBodyPartToxicResistance(pawn));
    }

    // Combined reduction factor from respiratory protection worn/carried by the source and target.
    // Returns 1 when neither side blocks anything.
    public static float GetRespiratoryMaskFactor(Pawn source, Pawn target, RespiratoryVector vector)
    {
        if (vector == null)
        {
            return 1f;
        }

        return GetSideFactor(source, vector, vector.maskSourceEffectiveness)
            * GetSideFactor(target, vector, vector.maskTargetEffectiveness);
    }

    private static float GetSideFactor(Pawn pawn, RespiratoryVector vector, float barrierEffectiveness)
    {
        if (pawn == null)
        {
            return 1f;
        }

        // Physical barrier (mask / lung), scaled by how respiratory the vector is.
        float barrierFactor = 1f;
        if (barrierEffectiveness > 0f)
        {
            barrierFactor = 1f - GetAirwayBarrierResistance(pawn) * barrierEffectiveness;
        }

        // Whitelisted gene airway immunity, scaled by the vector's airway dependence so that, e.g.,
        // breathless blocks airborne flu fully but does nothing against contact-spread plague.
        float immunityFactor = 1f;
        if (vector.airwayImmunityFactor > 0f && RespiratoryProtectionCache.HasAnyGeneProtections)
        {
            immunityFactor = 1f - RespiratoryProtectionCache.GetGeneProtection(pawn) * vector.airwayImmunityFactor;
        }

        return Mathf.Clamp01(barrierFactor * immunityFactor);
    }
}
