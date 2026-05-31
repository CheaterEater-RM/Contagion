using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Contagion;

public static class ContagionIngestionUtility
{
    private const float StrongStomachIngestionFactor = 0.1f;

    public static float ApplyTaintedFoodInfectionFactor(float baseChance, Pawn ingester)
    {
        return ContagionRiskMath.ApplyIngestionResistance(baseChance, GetTaintedFoodInfectionFactor(ingester));
    }

    public static float GetTaintedFoodInfectionFactor(Pawn ingester)
    {
        if (ingester == null)
        {
            return 1f;
        }

        if (ModsConfig.AnomalyActive && ingester.IsMutant && ingester.mutant.Def.preventIllnesses)
        {
            return 0f;
        }

        float factor = 1f;
        if (ingester.health?.hediffSet != null)
        {
            List<Hediff> hediffs = ingester.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                if (hediff?.def?.defName == "BionicStomach")
                {
                    return 0f;
                }

                HediffStage curStage = hediff?.CurStage;
                if (curStage != null)
                {
                    factor *= curStage.foodPoisoningChanceFactor;
                }
            }
        }

        if (ModsConfig.BiotechActive && ingester.genes != null)
        {
            List<Gene> genes = ingester.genes.GenesListForReading;
            for (int i = 0; i < genes.Count; i++)
            {
                GeneDef geneDef = genes[i].def;
                factor *= geneDef.defName == "StrongStomach"
                    ? StrongStomachIngestionFactor
                    : geneDef.foodPoisoningChanceFactor;
            }
        }

        return Mathf.Max(0f, factor);
    }
}
