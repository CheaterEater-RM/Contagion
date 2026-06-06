using System;
using System.Collections.Generic;
using System.Linq;
using Contagion;

// Source-less environmental exposure during an already-open environmental window. This mirrors the
// production Vector_Environmental + Seeder_Environmental path closely enough for tuning: window cadence,
// temperature/shelter/water/human factors, target susceptibility, settings multiplier, spread
// suppression, profile/fixed/no budget, and the Contagion director's max per-pass chance.
internal static class EnvironmentalSimulator
{
    private const int TicksPerDayInline = 60000;
    private const float MaxContagionModeChance = 0.10f;
    private const float MaxEnvironmentalWaterFactor = 3f;

    public static EnvironmentalRunResult Run(RunConditions conditions, DiseaseModel disease)
    {
        if (disease.EnvironmentalVector == null || disease.EnvironmentalSeeder == null)
        {
            throw new InvalidOperationException($"{disease.Name} has no environmental vector/seeder.");
        }

        int targets = Math.Max(0, conditions.EnvironmentalTargets);
        float windowDays = conditions.EnvironmentalWindowDays > 0f
            ? conditions.EnvironmentalWindowDays
            : disease.EnvironmentalSeeder.EffectiveWindowDays;
        float stepDays = conditions.EnvironmentalCheckIntervalTicks / (float)TicksPerDayInline;
        int steps = Math.Max(1, (int)Math.Ceiling(windowDays / Math.Max(0.0001f, stepDays)));
        int capacity = ContagionRiskMath.ActiveCaseCapacity(targets, disease.MaxActiveCaseChanceOffset);

        List<EnvironmentalTrialMetrics> trials = new();
        for (int t = 0; t < conditions.Trials; t++)
        {
            Random rng = new(unchecked(conditions.Seed * 100003 + t * 7919 + 911));
            trials.Add(RunTrial(conditions, disease, rng, targets, capacity, steps, stepDays));
        }

        return EnvironmentalRunResult.Summarize(trials, windowDays, targets, capacity);
    }

    private static EnvironmentalTrialMetrics RunTrial(
        RunConditions conditions,
        DiseaseModel disease,
        Random rng,
        int targets,
        int capacity,
        int steps,
        float stepDays)
    {
        bool[] infected = new bool[targets];
        int budget = DetermineBudget(conditions, disease, rng, targets, capacity);
        int passesPerDay = Math.Max(1, (int)Math.Round(TicksPerDayInline / (float)conditions.EnvironmentalCheckIntervalTicks));
        EnvironmentalTrialMetrics m = new()
        {
            TargetCount = targets,
            Capacity = capacity,
            Budget = budget,
            PerPassChanceAtStart = MeanDailyChance(conditions, disease, capacity, passesPerDay),
        };

        int infectedCount = 0;
        for (int step = 0; step < steps; step++)
        {
            if (budget >= 0 && infectedCount >= budget)
            {
                break;
            }

            float hour = step % passesPerDay * (24f / passesPerDay);
            bool outdoors = ScheduleOutdoors(conditions, hour);

            for (int target = 0; target < targets; target++)
            {
                if (infected[target] || (budget >= 0 && infectedCount >= budget))
                {
                    continue;
                }

                float chance = ChancePerPass(conditions, disease, infectedCount, capacity, hour, outdoors);
                if (rng.NextDouble() >= chance)
                {
                    continue;
                }

                infected[target] = true;
                infectedCount++;
                float day = (step + 1) * stepDays;
                if (m.DaysToFirst < 0f)
                {
                    m.DaysToFirst = day;
                }

                if (m.DaysTo50 < 0f && infectedCount >= Math.Ceiling(targets * 0.5))
                {
                    m.DaysTo50 = day;
                }

                if (budget >= 0 && m.DaysToBudget < 0f && infectedCount >= budget)
                {
                    m.DaysToBudget = day;
                }
            }
        }

        m.Infected = infectedCount;
        m.InfectedPct = targets > 0 ? 100f * infectedCount / targets : 0f;
        return m;
    }

    private static int DetermineBudget(RunConditions conditions, DiseaseModel disease, Random rng, int targets, int capacity)
    {
        if (conditions.EnvironmentalBudgetMode == EnvironmentalBudgetMode.None)
        {
            return -1;
        }

        if (conditions.EnvironmentalBudgetMode == EnvironmentalBudgetMode.Fixed)
        {
            return Math.Max(0, conditions.EnvironmentalFixedBudget);
        }

        EnvironmentalSeederModel seeder = disease.EnvironmentalSeeder;
        int budget = conditions.EnvironmentalTargetKind switch
        {
            EnvironmentalTargetKind.ColonyAnimal => (int)Math.Ceiling(targets * seeder.ColonyAnimalBudgetFraction.RandomInRange(rng)),
            EnvironmentalTargetKind.WildAnimal => (int)Math.Ceiling(Math.Sqrt(targets) * Math.Max(0f, seeder.WildAnimalBudgetSqrtFactor)),
            _ => (int)Math.Ceiling(targets * seeder.ColonyHumanBudgetFraction.RandomInRange(rng)),
        };

        return Math.Min(Math.Max(0, budget), Math.Max(0, capacity));
    }

    // Whether the pawn is outdoors at this local hour given its schedule. Off-hours are sheltered.
    private static bool ScheduleOutdoors(RunConditions conditions, float hour)
    {
        switch (conditions.EnvironmentalSchedule)
        {
            case EnvironmentalSchedule.Day:
                return hour >= conditions.EnvironmentalDayStartHour && hour < conditions.EnvironmentalDayEndHour;
            case EnvironmentalSchedule.Night:
                return hour < conditions.EnvironmentalDayStartHour || hour >= conditions.EnvironmentalDayEndHour;
            default:
                return conditions.Outdoor; // Always: honour the static --indoor/--outdoor flag
        }
    }

    // Daily-mean per-pass chance at zero load — the reported p/pass, since time-of-day and schedule
    // make a single pass unrepresentative.
    private static float MeanDailyChance(RunConditions conditions, DiseaseModel disease, int capacity, int passesPerDay)
    {
        double sum = 0;
        for (int p = 0; p < passesPerDay; p++)
        {
            float hour = p * (24f / passesPerDay);
            sum += ChancePerPass(conditions, disease, activeCases: 0, capacity, hour, ScheduleOutdoors(conditions, hour));
        }

        return (float)(sum / passesPerDay);
    }

    private static float ChancePerPass(RunConditions conditions, DiseaseModel disease, int activeCases, int capacity, float hour, bool outdoors)
    {
        EnvironmentalVectorModel vector = disease.EnvironmentalVector;
        EnvironmentalSeederModel seeder = disease.EnvironmentalSeeder;
        float temperature = conditions.EnvironmentalTemperature ?? vector.PeakTemperature;

        float chance = vector.BaseChancePerCheck
            * seeder.BaseChanceMultiplier
            * Math.Max(0f, conditions.BaseChanceMult)
            * TemperatureFactor(temperature, vector)
            * TimeOfDayFactor(vector, hour)
            * (vector.GroundContact != null
                ? GroundContactFactor(conditions, vector.GroundContact, outdoors)
                : ShelterFactor(conditions, temperature, vector, outdoors))
            * WaterFactor(conditions)
            * TargetKindFactor(conditions, vector);

        chance *= Math.Max(0f, conditions.TargetSusceptibility);
        chance *= SuppressionFactor(conditions.Suppression, disease, activeCases, capacity);
        chance *= Math.Max(0f, conditions.TransmissionMultiplier);

        return Clamp(chance, 0f, MaxContagionModeChance);
    }

    private static float TimeOfDayFactor(EnvironmentalVectorModel vector, float hour)
        => vector.TimeOfDayActivityCurve == null ? 1f : Math.Max(0f, vector.TimeOfDayActivityCurve.Evaluate(hour));

    private static float TemperatureFactor(float ambientTemperature, EnvironmentalVectorModel vector)
    {
        if (ambientTemperature <= vector.MinTemperature)
        {
            return 0f;
        }

        if (ambientTemperature >= vector.PeakTemperature)
        {
            return 1f;
        }

        return (ambientTemperature - vector.MinTemperature) / Math.Max(0.0001f, vector.PeakTemperature - vector.MinTemperature);
    }

    private static float ShelterFactor(RunConditions conditions, float ambientTemperature, EnvironmentalVectorModel vector, bool outdoors)
    {
        if (outdoors)
        {
            return 1f;
        }

        float shelterFactor = Clamp(1f - vector.IndoorReductionPerCellFromEdge * Math.Max(1, conditions.EnvironmentalCellsFromUnroofed), 0f, 1f);
        if (ambientTemperature < vector.CoolRoomThreshold)
        {
            shelterFactor *= Clamp(
                (ambientTemperature - vector.MinTemperature) / Math.Max(0.0001f, vector.CoolRoomThreshold - vector.MinTemperature),
                0f,
                1f);
        }

        return shelterFactor;
    }

    // Mirrors GetGroundContactFactor: terrain under the pawn, roof multiplier on non-breeding
    // surfaces. !Outdoor == roofed for sim purposes.
    private static float GroundContactFactor(RunConditions conditions, GroundContactModel ground, bool outdoors)
    {
        // Outdoors, the pawn is on the configured surface; sheltered, it stands on a clean roofed
        // built/stone floor (a mountain-base / barn floor).
        EnvironmentalSurface surface = outdoors ? conditions.EnvironmentalSurface : EnvironmentalSurface.Stone;

        if (surface == EnvironmentalSurface.Breeding)
        {
            return ground.BreedingFactor; // breeding source — roof does not help
        }

        float factor = surface switch
        {
            EnvironmentalSurface.Ice => ground.IceFactor,
            EnvironmentalSurface.Stone => ground.StoneFactor,
            _ => ground.DirtFactor,
        };

        if (!outdoors)
        {
            factor *= ground.RoofedMultiplier;
        }

        return factor;
    }

    private static float WaterFactor(RunConditions conditions)
        => Clamp(conditions.EnvironmentalWaterFactor, 1f, MaxEnvironmentalWaterFactor);

    private static float TargetKindFactor(RunConditions conditions, EnvironmentalVectorModel vector)
        => conditions.EnvironmentalTargetKind == EnvironmentalTargetKind.Human
            ? Math.Max(0f, vector.HumanExposureFactor)
            : 1f;

    private static float SuppressionFactor(ContagionSuppressionMode mode, DiseaseModel disease, int cases, int capacity)
    {
        if (mode == ContagionSuppressionMode.LetErRip || disease.SpreadSuppressionScale <= 0f || cases <= 0 || capacity <= 0)
        {
            return 1f;
        }

        float load = cases / (float)capacity;
        float factor = ContagionRiskMath.SpreadSuppressionFactor(mode, load);
        float scale = Clamp(disease.SpreadSuppressionScale, 0f, 1f);
        return 1f + (factor - 1f) * scale;
    }

    private static float Clamp(float value, float min, float max)
        => Math.Min(Math.Max(value, min), max);
}
