using System.Collections.Generic;
using HarmonyLib;
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
        // Find the worst contamination among all ingredients.
        HediffDef worstDisease = null;
        float worstFactor = 0f;

        if (ingredients != null)
        {
            for (int i = 0; i < ingredients.Count; i++)
            {
                Comp_ContaminatedFood comp = ingredients[i]?.TryGetComp<Comp_ContaminatedFood>();
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
                }
            }
        }

        float cookingFactor = GetCookingFactor(recipeDef);

        foreach (Thing product in __result)
        {
            if (worstDisease != null && cookingFactor > 0f)
            {
                Comp_ContaminatedFood productComp = product?.TryGetComp<Comp_ContaminatedFood>();
                if (productComp != null && !productComp.IsContaminated)
                {
                    productComp.SetContamination(worstDisease, worstFactor * cookingFactor);
                    ContagionDiagnostics.Record(ContagionDiagnosticCounter.MealsContaminated);
                    ContagionDiagnostics.Trace($"Ingredient contamination: {worstDisease.defName} propagated to {product.def.defName} (factor {worstFactor * cookingFactor:F2}).");
                }
            }

            yield return product;
        }
    }

    private static float GetCookingFactor(RecipeDef recipeDef)
    {
        if (recipeDef == null)
        {
            return DefaultReductionFactor;
        }

        CookingContaminationExtension ext = recipeDef.GetModExtension<CookingContaminationExtension>();
        return ext != null ? Mathf.Clamp01(ext.reductionFactor) : DefaultReductionFactor;
    }
}
