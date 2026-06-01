using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

public enum ContagionCorpseFluidExposureKind
{
    Pickup,
    Putdown,
    Carry,
    Butcher
}

public static class ContagionCorpseExposureUtility
{
    private static readonly SimpleCurve DefaultFleaAgePotencyCurve = new SimpleCurve
    {
        new CurvePoint(0f, 0.10f),
        new CurvePoint(0.08f, 0.40f),
        new CurvePoint(0.25f, 2.50f),
        new CurvePoint(0.75f, 1.50f),
        new CurvePoint(1.50f, 0.25f),
        new CurvePoint(2.00f, 0f)
    };

    private static readonly SimpleCurve DefaultFluidAgePotencyCurve = new SimpleCurve
    {
        new CurvePoint(0f, 0.10f),
        new CurvePoint(0.15f, 0.20f),
        new CurvePoint(0.50f, 0.45f),
        new CurvePoint(1.50f, 0.90f),
        new CurvePoint(3.00f, 1.30f),
        new CurvePoint(7.00f, 1.50f),
        new CurvePoint(12.00f, 0f)
    };

    public static bool TryGetCorpseFleaVector(Corpse corpse, out ResolvedTransmissionProfile resolvedProfile, out Vector_CorpseFlea vector)
    {
        vector = null;
        if (!TryGetCorpseProfile(corpse, out resolvedProfile))
        {
            return false;
        }

        return ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out vector);
    }

    public static bool TryGetCorpseFluidVector(Corpse corpse, out ResolvedTransmissionProfile resolvedProfile, out Vector_CorpseFluid vector)
    {
        vector = null;
        if (!TryGetCorpseProfile(corpse, out resolvedProfile))
        {
            return false;
        }

        return ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out vector);
    }

    public static Hediff_ContagionCorpseFleas EnsureCorpseFleas(Corpse corpse, ResolvedTransmissionProfile resolvedProfile)
    {
        if (corpse?.InnerPawn?.health == null || resolvedProfile?.Profile == null)
        {
            return null;
        }

        if (!ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_CorpseFlea _))
        {
            return null;
        }

        Hediff_ContagionCorpseFleas fleas = FindCorpseFleas(corpse);
        if (fleas == null)
        {
            fleas = HediffMaker.MakeHediff(ContagionDefOf.Contagion_CorpseFleas, corpse.InnerPawn) as Hediff_ContagionCorpseFleas;
            if (fleas == null)
            {
                Log.Error("[Contagion] Failed to create corpse flea hediff.");
                return null;
            }

            corpse.InnerPawn.health.AddHediff(fleas);
        }

        fleas.Configure(resolvedProfile.DiseaseDef);
        return fleas;
    }

    public static Hediff_ContagionCorpseFleas FindCorpseFleas(Corpse corpse)
    {
        if (corpse?.InnerPawn?.health?.hediffSet?.hediffs == null)
        {
            return null;
        }

        for (int i = 0; i < corpse.InnerPawn.health.hediffSet.hediffs.Count; i++)
        {
            if (corpse.InnerPawn.health.hediffSet.hediffs[i] is Hediff_ContagionCorpseFleas fleas)
            {
                return fleas;
            }
        }

        return null;
    }

    public static void UpdateCorpseFleas(Corpse corpse, ResolvedTransmissionProfile resolvedProfile, Vector_CorpseFlea vector, int deltaTicks)
    {
        Hediff_ContagionCorpseFleas fleas = EnsureCorpseFleas(corpse, resolvedProfile);
        fleas?.UpdateFromCorpse(corpse, vector, deltaTicks);
    }

    public static bool TryApplyFleaExposure(
        Pawn target,
        Corpse corpse,
        ResolvedTransmissionProfile resolvedProfile,
        Vector_CorpseFlea vector,
        float baseChance,
        float contextFactor)
    {
        if (target == null || corpse == null || resolvedProfile == null || vector == null)
        {
            return false;
        }

        Hediff_ContagionCorpseFleas fleas = EnsureCorpseFleas(corpse, resolvedProfile);
        float fleaPotency = Mathf.Max(0f, fleas?.Severity ?? 0f);
        if (fleaPotency <= 0f)
        {
            return false;
        }

        return TryApplyCorpseExposure(
            target,
            corpse,
            resolvedProfile,
            Mathf.Max(0f, baseChance) * fleaPotency * Mathf.Max(0f, contextFactor),
            ContagionDiagnosticCounter.CorpseFleaAttempted,
            ContagionDiagnosticCounter.CorpseFleaSeeded,
            ContagionDebugVectorKind.CorpseFlea,
            "Corpse flea");
    }

    public static bool TryApplyFluidExposure(
        Pawn target,
        Corpse corpse,
        ResolvedTransmissionProfile resolvedProfile,
        Vector_CorpseFluid vector,
        ContagionCorpseFluidExposureKind kind,
        float contextFactor = 1f)
    {
        if (target == null || corpse == null || resolvedProfile == null || vector == null)
        {
            return false;
        }

        float baseChance = kind switch
        {
            ContagionCorpseFluidExposureKind.Pickup => vector.pickupChance,
            ContagionCorpseFluidExposureKind.Putdown => vector.putdownChance,
            ContagionCorpseFluidExposureKind.Butcher => vector.butcherChance,
            _ => vector.carriedChancePerCheck
        };
        float potency = GetCorpseFluidPotency(corpse, vector);
        if (baseChance <= 0f || potency <= 0f)
        {
            return false;
        }

        return TryApplyCorpseExposure(
            target,
            corpse,
            resolvedProfile,
            baseChance * potency * Mathf.Max(0f, contextFactor),
            ContagionDiagnosticCounter.CorpseFluidAttempted,
            ContagionDiagnosticCounter.CorpseFluidSeeded,
            ContagionDebugVectorKind.CorpseFluid,
            "Corpse fluid");
    }

    public static float GetButcheryExposureFactor(Pawn butcher, Corpse corpse)
    {
        if (butcher?.skills == null)
        {
            return 1f;
        }

        return ContagionRiskMath.ButcheryExposureFactor(
            butcher.skills.GetSkill(SkillDefOf.Cooking).Level,
            butcher.skills.GetSkill(SkillDefOf.Medicine).Level,
            butcher.skills.GetSkill(SkillDefOf.Animals).Level,
            corpse?.InnerPawn?.RaceProps?.Animal == true);
    }

    public static float GetCookingExposureFactor(Pawn cook, Vector_CookingExposure vector)
    {
        if (cook?.skills == null || vector == null)
        {
            return 1f;
        }

        return ContagionRiskMath.CookingExposureFactor(
            cook.skills.GetSkill(SkillDefOf.Cooking).Level,
            vector.lowSkillFactor,
            vector.highSkillFactor);
    }

    public static float EvaluateCorpseFleaAgePotency(Vector_CorpseFlea vector, float corpseAgeDays)
    {
        return Mathf.Max(0f, (vector?.corpseAgePotencyCurve ?? DefaultFleaAgePotencyCurve).Evaluate(corpseAgeDays));
    }

    public static float GetCorpseFluidPotency(Corpse corpse, Vector_CorpseFluid vector)
    {
        if (corpse == null)
        {
            return 0f;
        }

        if (corpse.GetComp<CompRottable>()?.Stage == RotStage.Dessicated)
        {
            return 0f;
        }

        float ageDays = Mathf.Max(0f, corpse.Age / 60000f);
        return Mathf.Max(0f, (vector?.corpseAgePotencyCurve ?? DefaultFluidAgePotencyCurve).Evaluate(ageDays));
    }

    private static bool TryGetCorpseProfile(Corpse corpse, out ResolvedTransmissionProfile resolvedProfile)
    {
        resolvedProfile = null;
        if (!ContagionCorpseUtility.TryGetInfectedDisease(corpse, out HediffDef diseaseDef))
        {
            return false;
        }

        return DiseaseProfileCache.TryGetResolvedProfile(diseaseDef, out resolvedProfile);
    }

    private static bool TryApplyCorpseExposure(
        Pawn target,
        Corpse corpse,
        ResolvedTransmissionProfile resolvedProfile,
        float baseChance,
        ContagionDiagnosticCounter attemptedCounter,
        ContagionDiagnosticCounter seededCounter,
        ContagionDebugVectorKind vectorKind,
        string label)
    {
        if (target?.MapHeld == null || baseChance <= 0f)
        {
            return false;
        }

        ContagionDiagnostics.Record(attemptedCounter);
        float transmissionMultiplier = Contagion_Mod.Settings?.EffectiveTransmissionMultiplier ?? 1f;
        float chance = ContagionTransmissionUtility.BuildSeederChance(
            baseChance,
            target,
            resolvedProfile,
            target.MapHeld,
            transmissionMultiplier,
            out HediffDef _);

        float finalChance = Mathf.Clamp01(chance);
        bool passed = Rand.Chance(finalChance);
        bool seeded = false;
        if (passed)
        {
            seeded = ContagionDiseaseUtility.TrySeedIncubation(
                target,
                resolvedProfile.DiseaseDef,
                resolvedProfile.PartsToAffect,
                ContagionDiagnosticOrigin.Spread,
                ContagionSeedSource.Corpse,
                out HediffDef _);
            if (seeded)
            {
                ContagionDiagnostics.Record(seededCounter);
                ContagionDiagnostics.Trace($"{label} transmission: {resolvedProfile.DiseaseDef.defName} to {target.LabelShortCap}.");
                ContagionTrace.Transmission(corpse, target, resolvedProfile.DiseaseDef, vectorKind);
            }
        }

        ContagionDiagnostics.LogRoll(vectorKind, corpse, target, resolvedProfile.DiseaseDef, finalChance, seeded);
        return seeded;
    }
}
