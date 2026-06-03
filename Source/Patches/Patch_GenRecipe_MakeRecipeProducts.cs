using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion.Patches;

// When a recipe uses contaminated raw ingredients, the produced meals inherit contamination
// reduced by the recipe's cooking factor (CookingContaminationExtension). Nutrient paste
// (factor 0) produces fully safe meals. Raw-meat bills skip this since Patch_Corpse_ButcherProducts
// stamps contamination directly; this patch covers the cook-path (ingredient → meal).
[HarmonyPatch(typeof(GenRecipe), nameof(GenRecipe.MakeRecipeProducts))]
internal static class Patch_GenRecipe_MakeRecipeProducts
{
    // Default factor when no CookingContaminationExtension is present. Matches fine meal level.
    private const float DefaultReductionFactor = 0.2f;

    public static IEnumerable<Thing> Postfix(
        IEnumerable<Thing> __result,
        RecipeDef recipeDef,
        Pawn worker,
        List<Thing> ingredients)
    {
        ApplyCookingExposure(worker, ingredients);

        // Find the worst contamination among all ingredients.
        HediffDef worstDisease = null;
        float worstFactor = 0f;
        int worstSourceNodeId = -1;

        if (ingredients != null)
        {
            for (int i = 0; i < ingredients.Count; i++)
            {
                Thing ingredient = ingredients[i];
                Comp_ContaminatedFood comp = ingredient?.TryGetComp<Comp_ContaminatedFood>();
                if (comp == null || !comp.IsContaminated)
                {
                    continue;
                }

                // Verify the disease still has a foodborne vector (rules out cross-contamination
                // from non-food-vector diseases that somehow ended up on a comp).
                if (!DiseaseProfileCache.TryGetResolvedProfile(comp.ContaminatedDiseaseDef, out ResolvedTransmissionProfile resolvedProfile)
                    || !ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Foodborne _))
                {
                    continue;
                }

                if (comp.ContaminationFactor > worstFactor)
                {
                    worstFactor = comp.ContaminationFactor;
                    worstDisease = comp.ContaminatedDiseaseDef;
                    worstSourceNodeId = ContagionTrace.GetContaminationSourceNode(
                        worker?.MapHeld,
                        ingredient,
                        comp.ContaminatedDiseaseDef,
                        comp.sourceTraceNodeId);
                }
            }
        }

        float cookingFactor = GetCookingFactor(recipeDef, worker);

        foreach (Thing product in __result)
        {
            if (worstDisease != null && cookingFactor > 0f)
            {
                Comp_ContaminatedFood productComp = product?.TryGetComp<Comp_ContaminatedFood>();
                if (productComp != null && !productComp.IsContaminated)
                {
                    // Route the chain through the cooking bench: ingredient (meat) → stove → meal,
                    // so the trace reads logically and the meal links to the durable bench node
                    // rather than the meat node that's about to be consumed (and spliced out).
                    productComp.sourceTraceNodeId = ContagionTrace.RouteThroughBench(
                        worker, worstDisease, worstSourceNodeId, ContagionDebugVectorKind.Cooking);
                    productComp.SetContamination(worstDisease, worstFactor * cookingFactor);
                    ContagionDiagnostics.Record(ContagionDiagnosticCounter.MealsContaminated);
                    ContagionDiagnostics.Trace($"Ingredient contamination: {worstDisease.defName} propagated to {product.def.defName} (factor {worstFactor * cookingFactor:F2}).");
                }
            }

            yield return product;
        }
    }

    private static float GetCookingFactor(RecipeDef recipeDef, Pawn worker)
    {
        float reductionFactor;
        float lowSkillFactor;
        float skillAsymptoteFactor;
        float skillDecayRate;
        if (recipeDef == null)
        {
            reductionFactor = DefaultReductionFactor;
            lowSkillFactor = 1.5f;
            skillAsymptoteFactor = 0.25f;
            skillDecayRate = 0.18f;
        }
        else
        {
            CookingContaminationExtension ext = recipeDef.GetModExtension<CookingContaminationExtension>();
            reductionFactor = ext != null ? Mathf.Clamp01(ext.reductionFactor) : DefaultReductionFactor;
            lowSkillFactor = ext?.lowSkillFactor ?? 1.5f;
            skillAsymptoteFactor = ext?.skillAsymptoteFactor ?? 0.25f;
            skillDecayRate = ext?.skillDecayRate ?? 0.18f;
        }

        return reductionFactor * GetCookingSurvivalSkillFactor(worker, lowSkillFactor, skillAsymptoteFactor, skillDecayRate);
    }

    private static float GetCookingSurvivalSkillFactor(Pawn worker, float lowSkillFactor, float skillAsymptoteFactor, float skillDecayRate)
    {
        if (worker?.skills == null)
        {
            return 1f;
        }

        return ContagionRiskMath.CookingSurvivalFactor(
            worker.skills.GetSkill(SkillDefOf.Cooking).Level,
            lowSkillFactor,
            skillAsymptoteFactor,
            skillDecayRate);
    }

    private static void ApplyCookingExposure(Pawn cook, List<Thing> ingredients)
    {
        if (cook?.MapHeld == null || ingredients == null)
        {
            return;
        }

        HediffDef worstDisease = null;
        ResolvedTransmissionProfile worstProfile = null;
        Vector_CookingExposure worstVector = null;
        float worstFactor = 0f;
        int worstSourceNodeId = -1;

        for (int i = 0; i < ingredients.Count; i++)
        {
            Thing ingredient = ingredients[i];
            Comp_ContaminatedFood comp = ingredient?.TryGetComp<Comp_ContaminatedFood>();
            if (comp == null || !comp.IsContaminated)
            {
                continue;
            }

            if (!DiseaseProfileCache.TryGetResolvedProfile(comp.ContaminatedDiseaseDef, out ResolvedTransmissionProfile resolvedProfile)
                || !ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_CookingExposure cookingVector))
            {
                continue;
            }

            if (comp.ContaminationFactor > worstFactor)
            {
                worstDisease = comp.ContaminatedDiseaseDef;
                worstProfile = resolvedProfile;
                worstVector = cookingVector;
                worstFactor = comp.ContaminationFactor;
                worstSourceNodeId = ContagionTrace.GetContaminationSourceNode(
                    cook.MapHeld,
                    ingredient,
                    comp.ContaminatedDiseaseDef,
                    comp.sourceTraceNodeId);
            }
        }

        if (worstDisease == null || worstProfile == null || worstVector == null)
        {
            return;
        }

        float skillFactor = ContagionCorpseExposureUtility.GetCookingExposureFactor(cook, worstVector);
        // Food-handling PPE on the cook (gloves / apron / mask) reduces contracting disease off ingredients.
        float protectionFactor = ContagionApparelProtectionUtility.GetContactProtectionFactor(cook, worstVector.apparelProtection);
        float baseChance = worstVector.baseChancePerRecipe * worstFactor * skillFactor * protectionFactor;
        if (baseChance <= 0f)
        {
            return;
        }

        ContagionDiagnostics.Record(ContagionDiagnosticCounter.CookingExposureAttempted);
        float transmissionMultiplier = Contagion_Mod.Settings?.EffectiveTransmissionMultiplier ?? 1f;
        float chance = ContagionTransmissionUtility.BuildSeederChance(
            baseChance,
            cook,
            worstProfile,
            cook.MapHeld,
            transmissionMultiplier,
            out HediffDef _);

        float finalChance = Mathf.Clamp01(chance);
        bool passed = Rand.Chance(finalChance);
        bool seeded = false;
        if (passed)
        {
            seeded = ContagionDiseaseUtility.TrySeedIncubation(
                cook,
                worstProfile.DiseaseDef,
                worstProfile.PartsToAffect,
                null,
                ContagionDiagnosticOrigin.Spread,
                ContagionSeedSource.Cooking,
                out HediffDef _);
            if (seeded)
            {
                ContagionDiagnostics.Record(ContagionDiagnosticCounter.CookingExposureSeeded);
                ContagionDiagnostics.Trace($"Cooking exposure: {worstProfile.DiseaseDef.defName} to {cook.LabelShortCap}.");
                // Link the contaminated ingredient's node → the infected cook.
                int cookNodeId = ContagionTrace.EnsureNode(cook, worstProfile.DiseaseDef);
                ContagionTrace.Edge(cook.MapHeld, worstSourceNodeId, cookNodeId, ContagionDebugVectorKind.Cooking);
            }
        }

        ContagionDiagnostics.LogRoll(ContagionDebugVectorKind.Cooking, null, cook, worstProfile.DiseaseDef, finalChance, seeded);
    }
}
