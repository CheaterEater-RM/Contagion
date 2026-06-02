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

    // When affectsAnimals is true and this is set, non-humanlike targets receive this hediff
    // instead of the primary disease def. Lets a single profile own both the human variant
    // (e.g. Plague, 12 h tend) and the animal variant (e.g. Animal_Plague, 48 h tend) while
    // sharing all transmission logic, seeder config, and carrier scanning.
    public HediffDef animalVariantDef;

    public float crossSpeciesTransmissionFactor;

    // Applied when both source and target are animals but of different races (e.g. chicken -> pig).
    // The x-axis is the number of player-colony animal races already carrying this disease on the
    // map. Null = no inter-animal species barrier.
    public SimpleCurve animalCrossSpeciesFactorCurve;

    public List<TransmissionVector> vectors;

    public List<TransmissionSeeder> seeders;

    // When true and no hand-authored disease IncidentDef already targets this hediff, Contagion generates
    // one at startup so the vanilla storyteller schedules this disease in Storyteller mode (Mode 1), with
    // full biome weighting, difficulty scaling, and event spacing. Requires a Seeder_Storyteller in this
    // profile to route the storyteller's pick through incubation. If you are not authoring your own
    // IncidentDef, set this true.
    public bool selfSchedules;

    // Biome commonality used for the generated incident, applied to every biome that rolls diseases.
    // 100 ~= vanilla flu; higher is picked more often by the storyteller. Only used when selfSchedules
    // generates an incident.
    public float selfScheduleCommonality = 100f;

    public bool useScaledActiveCaseCap = true;

    public float maxActiveCaseChanceOffset;

    // Scales how strongly the global spread-suppression exponent applies to this disease.
    // 1 = normal, 0 = this disease ignores suppression (e.g. environmental diseases with no
    // person-to-person spread), >1 = suppresses faster than other diseases.
    public float spreadSuppressionScale = 1f;

    // How much an active/incubating case of this disease weighs toward the Mode 2 director's
    // colony "disease burden". Higher = the director backs off seeding harder while this disease
    // is present (dangerous diseases buy more breathing room); lower = mild diseases suppress less.
    // 1 = normal. Replaces a hardcoded per-disease table so modders can tune it from XML.
    public float dangerWeight = 1f;

    public OutbreakNotificationMode outbreakNotification = OutbreakNotificationMode.FirstCase;

    // How long (in days) after the most recent case before an outbreak is considered over and
    // the next case resets back to a red first-case letter. Defaults to 3 days.
    public float outbreakEndDays = 3f;

    // Convenience property used by Contagion_MapTransmissionComponent.IsOutbreakActive.
    public int OutbreakEndTicks => (int)(outbreakEndDays * 60000f);

    // Incubation duration in ticks. Used by outbreak-origin tracking to keep a recorded origin
    // alive across the pre-symptomatic window before the first visible case appears.
    public int IncubationTicks => (int)(incubationDays * 60000f);

    public bool corpseContagious;

    public float corpseInfectivityDecayPerDay = 0.5f;

    // When true, infected animals display a visible "sick" signal hediff that handlers can notice
    // (Animals skill roll) and doctors can diagnose (Medical skill roll). Used by gut worms and plague
    // to make animal carriers legible without requiring the player to inspect every animal's health tab.
    public bool showsSickSignal;

    // Per-check probability curve (evaluated against current severity) for a hidden active
    // disease to spontaneously present visible sick-signal symptoms. Checked every half game-day.
    // Only meaningful when showsSickSignal is true; null = no passive presentation.
    // Tuned so the cumulative chance over a full disease course is roughly 20-30%.
    public SimpleCurve passiveSymptomPresentationCurve;

    // One-time roll when an animal dies while carrying a hidden active disease: chance the
    // corpse shows as infected post-mortem (disease apparent upon inspection after death).
    // Applied per-disease so some diseases (e.g. obvious lesions) can be set higher than
    // subtle systemic ones. 0 = never, 1 = always. Default 10%.
    public float posthumousSymptomChance = 0.1f;

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
        if (target == null || !CanAffect(target))
        {
            speciesFactor = 0f;
            return false;
        }

        // When the disease affects both species (e.g. gut worms, muscle parasites), pawn-to-pawn
        // vectors must still respect the cross-species barrier so animals don't catch gut worms from
        // human vomit or vice versa. Apply crossSpeciesTransmissionFactor whenever source and target
        // are different species categories. Source null = environmental/acausal seeding, no barrier.
        if (source != null && CrossesHumanAnimalBoundary(source, target))
        {
            speciesFactor = crossSpeciesTransmissionFactor;
            return speciesFactor > 0f;
        }

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

    public float roomAirBaseChanceFactor = 0.25f;

    public int roomAirMaxRange = 10;

    public int roomAirMaxCells = 100;
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

    // Human pawns can have a lower ambient/environmental exposure rate than animals to
    // reflect hygiene and less direct contact with contaminated water, soil, and feces.
    public float humanExposureFactor = 1f;

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

    // Contamination baked into Comp_ContaminatedFood at production time expires after this many
    // days, preventing very old preserved food from triggering new outbreaks.
    // 0 = no expiry.
    public float contaminationExpiryDays = 30f;
}

public sealed class Vector_FecalOralEating : TransmissionVector
{
    public float baseChancePerIngestion = 0.006f;

    public float hotspotShedChancePerCheck = 0.015f;

    public int hotspotRadius = 8;

    public int hotspotMergeRadius = 4;

    public int maxHotspotsPerDisease = 24;

    public float hotspotDurationDays = 8f;

    public float hotspotDecayPerDay = 0.35f;

    public float distanceFalloffRate = 0.25f;

    public float rainPotencyFactor = 0.35f;

    public float freezingPotencyFactor = 0.35f;

    public float freezingTemperature = 0f;

    public float grazingFactor = 1f;

    public float rawOutdoorGroundFoodFactor = 0.55f;

    public float preparedOutdoorGroundFoodFactor = 0.25f;

    public float storedOrIndoorFoodFactor = 0.05f;
}

public sealed class Vector_FecalOralLiving : TransmissionVector
{
    public float baseChancePerCheck = 0.0012f;

    public float filthContaminationChance = 1f;

    public int maxFilthPerDisease = 48;

    public float potencyDecayPerDay = 0.12f;

    public float thicknessFactor = 0.25f;

    public float roomCleanlinessImpact = 0.5f;
}

public sealed class Vector_CookingExposure : TransmissionVector
{
    public float baseChancePerRecipe = 0.004f;

    public float lowSkillFactor = 2f;

    public float highSkillFactor = 0.5f;
}

public sealed class Vector_CorpseFlea : TransmissionVector
{
    public float baseChancePerCheck = 0.006f;

    public float carriedBaseChancePerCheck = 0.025f;

    public float butcherBaseChance = 0.035f;

    public int maxRange = 12;

    public int carriedRange = 4;

    public float distanceFalloffRate = 0.25f;

    public float frozenTemperature = 0f;

    public float frozenViabilityLossPerDay = 4f;

    public SimpleCurve corpseAgePotencyCurve;
}

public sealed class Vector_CorpseFluid : TransmissionVector
{
    public float pickupChance = 0.015f;

    public float putdownChance = 0.015f;

    public float carriedChancePerCheck = 0.003f;

    public float butcherChance = 0.08f;

    public SimpleCurve corpseAgePotencyCurve;
}

public sealed class Vector_Lovin : TransmissionVector
{
    public float baseChancePerAct = 0.15f;
}

public abstract class TransmissionSeeder
{
    public float cooldownDays;
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

    public int degree = int.MinValue;

    public override float Evaluate(Pawn pawn)
    {
        if (pawn?.story?.traits == null || trait == null)
        {
            return 1f;
        }

        return degree != int.MinValue
            ? (pawn.story.traits.HasTrait(trait, degree) ? factor : 1f)
            : (pawn.story.traits.HasTrait(trait) ? factor : 1f);
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
