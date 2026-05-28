using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

public sealed class CompProperties_ContaminatedFood : CompProperties
{
    public CompProperties_ContaminatedFood()
    {
        compClass = typeof(Comp_ContaminatedFood);
    }
}

public sealed class Comp_ContaminatedFood : ThingComp
{
    private const float MinCleanlinessFactor = 0.1f;

    private const float MaxCleanlinessFactor = 3f;

    private HediffDef _contaminatedDiseaseDef;

    private float _contaminationFactor = 1f;

    public override void Notify_RecipeProduced(Pawn pawn)
    {
        base.Notify_RecipeProduced(pawn);
        ClearContamination();

        if (pawn == null)
        {
            return;
        }

        foreach (ResolvedTransmissionProfile resolvedProfile in ContagionDiseaseUtility.GetContagiousProfiles(pawn))
        {
            if (!ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Foodborne foodborneVector))
            {
                continue;
            }

            float sourceInfectivity = ContagionTransmissionUtility.GetSourceInfectivity(pawn, resolvedProfile, foodborneVector);
            if (sourceInfectivity <= 0f)
            {
                continue;
            }

            _contaminatedDiseaseDef = resolvedProfile.DiseaseDef;
            _contaminationFactor = sourceInfectivity * GetCleanlinessFactor(pawn.GetRoom(), foodborneVector.cleanlinessImpact);
            ContagionDiagnostics.Record(ContagionDiagnosticCounter.MealsContaminated);
            ContagionDiagnostics.Trace($"Meal contaminated: {_contaminatedDiseaseDef.defName} by {pawn.LabelShortCap}.");
            return;
        }
    }

    public override void PreAbsorbStack(Thing otherStack, int count)
    {
        base.PreAbsorbStack(otherStack, count);

        Comp_ContaminatedFood otherComp = otherStack.TryGetComp<Comp_ContaminatedFood>();
        if (otherComp?._contaminatedDiseaseDef == null)
        {
            return;
        }

        if (_contaminatedDiseaseDef == null)
        {
            _contaminatedDiseaseDef = otherComp._contaminatedDiseaseDef;
            _contaminationFactor = otherComp._contaminationFactor;
            return;
        }

        if (_contaminatedDiseaseDef == otherComp._contaminatedDiseaseDef)
        {
            _contaminationFactor = Mathf.Max(_contaminationFactor, otherComp._contaminationFactor);
            return;
        }

        if (!TryGetDiseaseSeverity(_contaminatedDiseaseDef, out float currentSeverity)
            || !TryGetDiseaseSeverity(otherComp._contaminatedDiseaseDef, out float otherSeverity))
        {
            return;
        }

        if (otherSeverity > currentSeverity
            || (Mathf.Approximately(otherSeverity, currentSeverity) && otherComp._contaminationFactor > _contaminationFactor))
        {
            _contaminatedDiseaseDef = otherComp._contaminatedDiseaseDef;
            _contaminationFactor = otherComp._contaminationFactor;
        }
    }

    public override void PostIngested(Pawn ingester)
    {
        base.PostIngested(ingester);

        if (ingester == null || _contaminatedDiseaseDef == null)
        {
            return;
        }

        if (!DiseaseProfileCache.TryGetResolvedProfile(_contaminatedDiseaseDef, out ResolvedTransmissionProfile resolvedProfile)
            || !ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Foodborne foodborneVector))
        {
            ClearContamination();
            return;
        }

        ContagionDiagnostics.Record(ContagionDiagnosticCounter.FoodborneAttempted);
        // No spread suppression here: foodborne infection comes from a contaminated-food source, not
        // herd transmission between pawns, so it does not scale with the colony infection fraction.
        float transmissionMultiplier = Contagion_Mod.Settings?.EffectiveTransmissionMultiplier ?? 1f;
        float chance = ContagionTransmissionUtility.BuildSeederChance(
            foodborneVector.baseChancePerMeal * _contaminationFactor,
            ingester,
            resolvedProfile,
            ingester.MapHeld,
            transmissionMultiplier,
            out HediffDef _);
        if (Rand.Chance(Mathf.Clamp01(chance)))
        {
            if (ContagionDiseaseUtility.TrySeedIncubation(
                ingester,
                resolvedProfile.DiseaseDef,
                resolvedProfile.PartsToAffect,
                ContagionDiagnosticOrigin.Spread,
                out HediffDef _))
            {
                ContagionDiagnostics.Record(ContagionDiagnosticCounter.FoodborneSeeded);
                ContagionDiagnostics.Trace($"Foodborne transmission: {resolvedProfile.DiseaseDef.defName} to {ingester.LabelShortCap}.");
            }
        }
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Defs.Look(ref _contaminatedDiseaseDef, "contaminatedDiseaseDef");
        Scribe_Values.Look(ref _contaminationFactor, "contaminationFactor", 1f);
        _contaminationFactor = Mathf.Clamp(_contaminationFactor, MinCleanlinessFactor, MaxCleanlinessFactor);
    }

    private void ClearContamination()
    {
        _contaminatedDiseaseDef = null;
        _contaminationFactor = 1f;
    }

    private static float GetCleanlinessFactor(Room room, float cleanlinessImpact)
    {
        if (room == null || cleanlinessImpact <= 0f || room.PsychologicallyOutdoors)
        {
            return 1f;
        }

        float cleanliness = room.GetStat(RoomStatDefOf.Cleanliness);
        return Mathf.Clamp(1f - cleanliness * cleanlinessImpact, MinCleanlinessFactor, MaxCleanlinessFactor);
    }

    private static bool TryGetDiseaseSeverity(HediffDef diseaseDef, out float severity)
    {
        severity = 0f;
        if (diseaseDef == null || diseaseDef.lethalSeverity < 0f)
        {
            return false;
        }

        severity = diseaseDef.lethalSeverity;
        return true;
    }
}
