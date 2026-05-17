using System.Collections.Generic;
using Verse;

namespace Contagion;

public sealed class TransmissionProfile : DefModExtension
{
    public float contagiousMinSeverity = 0.05f;

    public float contagiousMaxSeverity = 0.8f;

    public bool contagiousDuringIncubation;

    public float incubationDays = 1f;

    public float immunityDurationDays;

    public bool affectsHumans = true;

    public bool affectsAnimals;

    public bool crossSpeciesTransmission;

    public List<BodyPartDef> partsToAffect;

    public List<TransmissionVector> vectors;

    public List<TransmissionSeeder> seeders;

    public bool HasContagiousWindow => contagiousMaxSeverity > contagiousMinSeverity;

    public bool UsesPartTargeting => !partsToAffect.NullOrEmpty();

    public bool HasVectors => !vectors.NullOrEmpty();

    public bool HasSeeders => !seeders.NullOrEmpty();

    public bool UsesTemporaryImmunity => immunityDurationDays > 0f;

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
}

public abstract class TransmissionVector
{
}

public sealed class Vector_Airborne : TransmissionVector
{
    public float baseChancePerCheck = 0.03f;

    public float outdoorFactor = 0.15f;

    public int maxRange = 15;

    public float distanceFalloffRate = 0.25f;
}

public sealed class Vector_Social : TransmissionVector
{
    public float baseChancePerInteraction = 0.02f;

    public float outdoorFactor = 0.5f;
}

public sealed class Vector_Proximity : TransmissionVector
{
    public float baseChancePerCheck = 0.025f;

    public int maxRange = 6;

    public float distanceFalloffRate = 0.35f;

    public float cleanlinessImpact = 1f;

    public float outdoorFactor = 0.75f;
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