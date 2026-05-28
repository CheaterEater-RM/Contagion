using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

public enum OutbreakNotificationMode
{
    None,
    FirstCase,
    EveryCase
}

public sealed class TransmissionProfile : DefModExtension
{
    public float incubationDays = 1f;

    // Storyteller mode: how long a queued disease-start request waits for fulfillment
    // before falling back to an acausal seed. Environmental windows use Seeder_Environmental.windowDays.
    public float pendingWindowDays = 5f;

    public float immunityDurationDays;

    public HediffDef immunityHediffDef;

    public List<BodyPartDef> targetBodyParts;

    public SimpleCurve incubationInfectivityCurve;

    public SimpleCurve activeInfectivityCurve;

    public SeasonalInfectivity seasonalInfectivity;

    public List<SusceptibilityFactor> susceptibilityFactors;

    public List<SourceInfectivityFactor> sourceInfectivityFactors;

    public bool affectsHumans = true;

    public bool affectsAnimals;

    public float crossSpeciesTransmissionFactor;

    public List<TransmissionVector> vectors;

    public List<TransmissionSeeder> seeders;

    public int maxActiveCases;

    // Scales how strongly the global spread-suppression exponent applies to this disease.
    // 1 = normal, 0 = this disease ignores suppression (e.g. environmental diseases with no
    // person-to-person spread), >1 = suppresses faster than other diseases.
    public float spreadSuppressionScale = 1f;

    // Contagion mode: each successful disease start increases pressure, which damps later
    // start chances for the same disease until it decays back to zero.
    public float pressureGain = 1f;

    public float pressureDecayDays = 5f;

    public OutbreakNotificationMode outbreakNotification = OutbreakNotificationMode.FirstCase;

    public bool corpseContagious;

    public float corpseInfectivityDecayPerDay = 0.5f;

    public float carrierChance;

    public HediffDef carrierHediffDef;

    public bool spreadsDuringCaravan;

    public bool UsesPartTargeting => !targetBodyParts.NullOrEmpty();

    public bool HasVectors => !vectors.NullOrEmpty();

    public bool HasSeeders => !seeders.NullOrEmpty();

    public bool UsesTemporaryImmunity => immunityHediffDef != null || immunityDurationDays > 0f;

    public bool CanAffect(Pawn pawn)
    {
        if (pawn == null)
        {
            return false;
        }

        if (pawn.RaceProps.Humanlike)
        {
            return affectsHumans;
        }

        if (pawn.RaceProps.Animal)
        {
            return affectsAnimals;
        }

        return false;
    }

    public bool CanTransmitBetween(Pawn source, Pawn target, out float speciesFactor)
    {
        speciesFactor = 1f;
        if (target == null)
        {
            return false;
        }

        if (CanAffect(target))
        {
            return true;
        }

        if (source == null || crossSpeciesTransmissionFactor <= 0f || !CrossesHumanAnimalBoundary(source, target))
        {
            speciesFactor = 0f;
            return false;
        }

        speciesFactor = crossSpeciesTransmissionFactor;
        return true;
    }

    private static bool CrossesHumanAnimalBoundary(Pawn first, Pawn second)
    {
        return (first.RaceProps.Humanlike && second.RaceProps.Animal)
            || (first.RaceProps.Animal && second.RaceProps.Humanlike);
    }
}

public sealed class SeasonalInfectivity
{
    public float spring = 1f;

    public float summer = 1f;

    public float fall = 1f;

    public float winter = 1f;

    public float permanentSummer = 1f;

    public float permanentWinter = 1f;
}

public abstract class TransmissionVector
{
    public SimpleCurve activeInfectivityCurveOverride;

    public SimpleCurve incubationInfectivityCurveOverride;
}

// Vectors that spread through the air / respiration, where face masks can reduce transmission.
// Effectiveness values are how much of a worn mask's apparel ToxicEnvironmentResistance (0-1)
// is applied. The final per-side factor is (1 - apparelResistance * effectiveness).
public abstract class RespiratoryVector : TransmissionVector
{
    // How much a mask on the susceptible target blocks inhaling the pathogen.
    public float maskTargetEffectiveness = 0.7f;

    // How much a mask on the infectious source blocks emitting the pathogen.
    public float maskSourceEffectiveness = 0.5f;

    // How airway-dependent this vector is, gating gene-based airway immunity (e.g. breathless).
    // 1 = fully respiratory (airborne, talking); 0 = not airway-based (contact/flea spread), where
    // airway immunity should not help. Physical masks still apply via the effectiveness fields.
    public float airwayImmunityFactor = 1f;
}

public sealed class Vector_Airborne : RespiratoryVector
{
    public float baseChancePerCheck = 0.03f;

    public float outdoorFactor = 0.15f;

    public int maxRange = 15;

    public float distanceFalloffRate = 0.25f;

    public float obstructedFactor;
}

public sealed class Vector_Social : RespiratoryVector
{
    public float baseChancePerInteraction = 0.02f;

    public float outdoorFactor = 0.5f;
}

public sealed class Vector_Proximity : RespiratoryVector
{
    public float baseChancePerCheck = 0.025f;

    public int maxRange = 6;

    public float distanceFalloffRate = 0.35f;

    public float cleanlinessImpact = 1f;

    public float outdoorFactor = 0.75f;

    public int outdoorFilthRadius = 4;
}

public sealed class Vector_Environmental : TransmissionVector
{
    public float baseChancePerCheck = 0.02f;

    public float minTemperature = 15f;

    public float peakTemperature = 30f;

    public int waterProximityRadius = 10;

    public float waterProximityWeight = 0.02f;

    public float indoorReductionPerCellFromEdge = 0.1f;

    public float coolRoomThreshold = 18f;
}

public sealed class Vector_Fomite : TransmissionVector
{
    public bool contaminatesVomit = true;

    public float baseChancePerContact = 0.03f;

    public float potencyDecayPerHour = 0.1f;
}

public sealed class Vector_Foodborne : TransmissionVector
{
    public float baseChancePerMeal = 0.08f;

    public float cleanlinessImpact = 1f;
}

public sealed class Vector_Lovin : TransmissionVector
{
    public float baseChancePerAct = 0.15f;
}

public abstract class TransmissionSeeder
{
    public float cooldownDays;

    public int maxActiveCases;
}

public sealed class Seeder_Storyteller : TransmissionSeeder
{
    public IntRange seedCountRange = new IntRange(1, 1);
}

public sealed class Seeder_Arrival : TransmissionSeeder
{
    public float arrivalChance = 0.01f;
}

public sealed class Seeder_Environmental : TransmissionSeeder
{
    public float baseChanceMultiplier = 1f;

    public float windowDays = 14f;

    public IntRange infectionBudget = new IntRange(2, 5);
}

public sealed class Seeder_AnimalLinked : TransmissionSeeder
{
    public float mtbDays = 120f;

    public bool requiresAnimalsOnMap = true;

    public float handlerBias = 2f;
}

public sealed class Seeder_Acausal : TransmissionSeeder
{
    public float mtbDays = 90f;
}

public abstract class PawnFactor
{
    public float factor = 1f;

    public abstract float Evaluate(Pawn pawn);
}

public abstract class SusceptibilityFactor : PawnFactor
{
}

public abstract class SourceInfectivityFactor : PawnFactor
{
}

public sealed class Factor_Hediff : SusceptibilityFactor
{
    public HediffDef hediff;

    public override float Evaluate(Pawn pawn)
    {
        return pawn?.health?.hediffSet != null && hediff != null && pawn.health.hediffSet.HasHediff(hediff)
            ? factor
            : 1f;
    }
}

public sealed class Factor_Gene : SusceptibilityFactor
{
    public GeneDef gene;

    public override float Evaluate(Pawn pawn)
    {
        return pawn?.genes != null && gene != null && pawn.genes.HasActiveGene(gene) ? factor : 1f;
    }
}

public sealed class Factor_Trait : SusceptibilityFactor
{
    public TraitDef trait;

    public override float Evaluate(Pawn pawn)
    {
        return pawn?.story?.traits != null && trait != null && pawn.story.traits.HasTrait(trait) ? factor : 1f;
    }
}

public sealed class Factor_AgeRange : SusceptibilityFactor
{
    public float minAge;

    public float maxAge = float.MaxValue;

    public override float Evaluate(Pawn pawn)
    {
        if (pawn?.ageTracker == null)
        {
            return 1f;
        }

        float age = pawn.ageTracker.AgeBiologicalYearsFloat;
        return age >= minAge && age <= maxAge ? factor : 1f;
    }
}

public sealed class Factor_Stat : SusceptibilityFactor
{
    public StatDef stat;

    public SimpleCurve curve;

    public override float Evaluate(Pawn pawn)
    {
        if (pawn == null || stat == null || curve == null)
        {
            return 1f;
        }

        return Mathf.Max(0f, curve.Evaluate(pawn.GetStatValue(stat)));
    }
}

public sealed class Factor_HasInjury : SusceptibilityFactor
{
    public override float Evaluate(Pawn pawn)
    {
        List<Hediff> hediffs = pawn?.health?.hediffSet?.hediffs;
        if (hediffs == null)
        {
            return 1f;
        }

        for (int i = 0; i < hediffs.Count; i++)
        {
            if (hediffs[i] is Hediff_Injury injury && !injury.IsPermanent())
            {
                return factor;
            }
        }

        return 1f;
    }
}

public sealed class SourceFactor_Hediff : SourceInfectivityFactor
{
    public HediffDef hediff;

    public override float Evaluate(Pawn pawn)
    {
        return pawn?.health?.hediffSet != null && hediff != null && pawn.health.hediffSet.HasHediff(hediff)
            ? factor
            : 1f;
    }
}

public sealed class SourceFactor_Gene : SourceInfectivityFactor
{
    public GeneDef gene;

    public override float Evaluate(Pawn pawn)
    {
        return pawn?.genes != null && gene != null && pawn.genes.HasActiveGene(gene) ? factor : 1f;
    }
}

public sealed class SourceFactor_Trait : SourceInfectivityFactor
{
    public TraitDef trait;

    public override float Evaluate(Pawn pawn)
    {
        return pawn?.story?.traits != null && trait != null && pawn.story.traits.HasTrait(trait) ? factor : 1f;
    }
}

public sealed class SourceFactor_Stat : SourceInfectivityFactor
{
    public StatDef stat;

    public SimpleCurve curve;

    public override float Evaluate(Pawn pawn)
    {
        if (pawn == null || stat == null || curve == null)
        {
            return 1f;
        }

        return Mathf.Max(0f, curve.Evaluate(pawn.GetStatValue(stat)));
    }
}
