using System;
using System.Collections.Generic;
using System.Linq;
using Contagion;

// Steps a group of virtual pawns over time. Each step = one real 250-tick transmission pass
// (240/day). Per-pair chances reuse the production product (ContagionRiskMath.PreSeederBonusChance),
// distance falloff (ContagionRiskMath.DistanceFactor), suppression (ActiveCaseCapacity +
// SpreadSuppressionFactor) and the XML infectivity curves. Vanilla severity/immunity progression is
// reimplemented from the disease's documented HediffDef rates.
internal static class Simulator
{
    private enum Phase { Susceptible, Incubating, Active, Recovered, Dead }

    private sealed class PawnState
    {
        public Phase Phase = Phase.Susceptible;
        public float IncubationProgress;
        public float Severity;
        public float Immunity;
        public bool EverInfected;

        public bool Infectious => Phase == Phase.Incubating || Phase == Phase.Active;
    }

    public static RunResult Run(Scenario scenario, RunConditions conditions, DiseaseModel disease)
    {
        float stepDays = conditions.CheckIntervalTicks / (float)TicksPerDayInline;
        int steps = Math.Max(1, (int)Math.Round(conditions.Days * (double)TicksPerDayInline / conditions.CheckIntervalTicks));
        List<TrialMetrics> trials = new();

        for (int t = 0; t < conditions.Trials; t++)
        {
            Random rng = new(unchecked(conditions.Seed * 100003 + t * 7919 + 17));
            trials.Add(RunTrial(scenario, conditions, disease, rng, steps, stepDays));
        }

        int n = trials.Count > 0 ? trials[0].PawnCount : 0;
        int capacity = ContagionRiskMath.ActiveCaseCapacity(n, disease.MaxActiveCaseChanceOffset);
        return RunResult.Summarize(trials, conditions.Days, n, capacity);
    }

    private const int TicksPerDayInline = 60000;

    private static TrialMetrics RunTrial(Scenario scenario, RunConditions conditions, DiseaseModel disease, Random rng, int steps, float stepDays)
    {
        Layout layout = scenario.BuildLayout(conditions, rng);
        int n = layout.Positions.Count;
        PawnState[] pawns = new PawnState[n];
        for (int i = 0; i < n; i++)
        {
            pawns[i] = new PawnState();
        }

        int initialInfected = Math.Min(n, conditions.InitialInfected >= 0 ? conditions.InitialInfected : scenario.DefaultInitialInfected);
        for (int i = 0; i < initialInfected; i++)
        {
            pawns[i].Phase = Phase.Incubating;
            pawns[i].EverInfected = true;
        }

        int capacity = ContagionRiskMath.ActiveCaseCapacity(n, disease.MaxActiveCaseChanceOffset);
        float socialPerStep = conditions.SocialInteractionsPerPawnPerDay * stepDays;

        TrialMetrics m = new() { PawnCount = n, Capacity = capacity };
        int peakActive = 0;
        int clearedStep = -1;
        bool anyInfectionBeyondInitial = false;

        for (int step = 0; step < steps; step++)
        {
            int cases = pawns.Count(p => p.Infectious);
            float suppression = SuppressionFactor(conditions.Suppression, disease, cases, capacity);

            // Collect this step's new infections, applied after all rolls so suppression uses the
            // step-start case count (one consistent load per pass).
            List<int> newlyInfected = new();

            for (int target = 0; target < n; target++)
            {
                if (pawns[target].Phase != Phase.Susceptible)
                {
                    continue;
                }

                if (TryInfectFromAnySource(pawns, layout, conditions, disease, suppression, target, rng))
                {
                    newlyInfected.Add(target);
                }
            }

            // Social vector (flu): each infectious source initiates interactions at its per-step rate.
            if (disease.HasVector(VectorKind.Social))
            {
                RollSocial(pawns, layout, conditions, disease, suppression, socialPerStep, newlyInfected, rng);
            }

            foreach (int target in newlyInfected)
            {
                pawns[target].Phase = Phase.Incubating;
                pawns[target].EverInfected = true;
                anyInfectionBeyondInitial = true;
            }

            AdvanceProgression(pawns, disease, stepDays);

            int everInfected = pawns.Count(p => p.EverInfected);
            int activeNow = pawns.Count(p => p.Infectious);
            peakActive = Math.Max(peakActive, activeNow);

            float day = (step + 1) * stepDays;
            if (anyInfectionBeyondInitial && m.DaysToFirst < 0f)
            {
                m.DaysToFirst = day;
            }

            if (m.DaysTo50 < 0f && everInfected >= Math.Ceiling(n * 0.5))
            {
                m.DaysTo50 = day;
            }

            if (m.DaysToSaturation < 0f && everInfected >= n)
            {
                m.DaysToSaturation = day;
            }

            if (clearedStep < 0 && anyInfectionBeyondInitial && activeNow == 0)
            {
                clearedStep = step;
                m.ClearedDay = day;
            }
        }

        m.EverInfectedPct = 100f * pawns.Count(p => p.EverInfected) / n;
        m.PeakActive = peakActive;
        m.PeakActivePctCap = capacity > 0 ? 100f * peakActive / capacity : 0f;
        m.Cleared = clearedStep >= 0 || pawns.All(p => !p.Infectious);
        m.BurnedOut = m.Cleared && m.EverInfectedPct < 99.999f;
        return m;
    }

    private static bool TryInfectFromAnySource(
        PawnState[] pawns, Layout layout, RunConditions conditions, DiseaseModel disease, float suppression, int target, Random rng)
    {
        for (int source = 0; source < pawns.Length; source++)
        {
            if (source == target || !pawns[source].Infectious)
            {
                continue;
            }

            float infectivity = Infectivity(pawns[source], disease);
            if (infectivity <= 0f)
            {
                continue;
            }

            float dist = layout.Distance(source, target);

            // Production iterates the profile's vectors and returns on the first vector that seeds:
            // airborne tries direct then room, then proximity. Each is its own independent roll.
            foreach (LiveVector v in disease.Vectors)
            {
                if (v.Kind == VectorKind.Airborne)
                {
                    if (RollDirectAirborne(v, conditions, layout, infectivity, dist, suppression, rng)
                        || RollRoomAirborne(v, conditions, layout, infectivity, suppression, dist, rng))
                    {
                        return true;
                    }
                }
                else if (v.Kind == VectorKind.Proximity)
                {
                    if (RollProximity(v, conditions, layout, infectivity, dist, suppression, rng))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static void RollSocial(
        PawnState[] pawns, Layout layout, RunConditions conditions, DiseaseModel disease, float suppression,
        float socialPerStep, List<int> newlyInfected, Random rng)
    {
        LiveVector social = disease.Vectors.First(v => v.Kind == VectorKind.Social);
        int n = pawns.Length;
        for (int source = 0; source < n; source++)
        {
            if (!pawns[source].Infectious || rng.NextDouble() >= socialPerStep)
            {
                continue;
            }

            int target = rng.Next(n);
            if (target == source || pawns[target].Phase != Phase.Susceptible || newlyInfected.Contains(target))
            {
                continue;
            }

            float infectivity = Infectivity(pawns[source], disease);
            float mask = MaskFactor(social, conditions);
            float enclosure = layout.Indoor ? 1f : EffectiveOutdoor(social, conditions);
            float context = enclosure * mask * suppression;
            float chance = ContagionRiskMath.PreSeederBonusChance(
                social.BaseChance * conditions.BaseChanceMult, infectivity, conditions.TargetSusceptibility, context, conditions.TransmissionMultiplier);
            if (Roll(chance, conditions, rng))
            {
                newlyInfected.Add(target);
            }
        }
    }

    private static bool RollDirectAirborne(LiveVector v, RunConditions conditions, Layout layout, float infectivity, float dist, float suppression, Random rng)
    {
        if (dist > v.MaxRange)
        {
            return false;
        }

        float enclosure = layout.Indoor ? 1f : EffectiveOutdoor(v, conditions);
        float context = ContagionRiskMath.DistanceFactor(dist, v.DistanceFalloffRate) * enclosure * v.ObstructedFactor
            * MaskFactor(v, conditions) * suppression;
        float chance = ContagionRiskMath.PreSeederBonusChance(
            v.BaseChance * conditions.BaseChanceMult, infectivity, conditions.TargetSusceptibility, context, conditions.TransmissionMultiplier);
        return Roll(chance, conditions, rng);
    }

    private static bool RollRoomAirborne(LiveVector v, RunConditions conditions, Layout layout, float infectivity, float suppression, float dist, Random rng)
    {
        if (!layout.Indoor || layout.RoomCells <= 0 || v.RoomAirBaseChanceFactor <= 0f
            || layout.RoomCells > v.RoomAirMaxCells || dist > v.RoomAirMaxRange)
        {
            return false;
        }

        float effectiveRoomDistance = (float)Math.Sqrt(Math.Max(1, layout.RoomCells));
        float roomAirFactor = ContagionRiskMath.DistanceFactor(effectiveRoomDistance, v.DistanceFalloffRate);
        float context = roomAirFactor * MaskFactor(v, conditions) * suppression;
        float chance = ContagionRiskMath.PreSeederBonusChance(
            v.BaseChance * v.RoomAirBaseChanceFactor * conditions.BaseChanceMult, infectivity, conditions.TargetSusceptibility, context, conditions.TransmissionMultiplier);
        return Roll(chance, conditions, rng);
    }

    private static bool RollProximity(LiveVector v, RunConditions conditions, Layout layout, float infectivity, float dist, float suppression, Random rng)
    {
        if (dist > v.MaxRange)
        {
            return false;
        }

        float outdoor = layout.Indoor ? 1f : EffectiveOutdoor(v, conditions);
        float context = ContagionRiskMath.DistanceFactor(dist, v.DistanceFalloffRate) * outdoor * conditions.CleanlinessFactor
            * MaskFactor(v, conditions) * suppression;
        float chance = ContagionRiskMath.PreSeederBonusChance(
            v.BaseChance * conditions.BaseChanceMult, infectivity, conditions.TargetSusceptibility, context, conditions.TransmissionMultiplier);
        return Roll(chance, conditions, rng);
    }

    // Outdoor/enclosure penalty scaled by the OutdoorMult dial, clamped to a valid 0..1 factor.
    private static float EffectiveOutdoor(LiveVector v, RunConditions conditions)
        => Math.Min(1f, Math.Max(0f, v.OutdoorFactor * conditions.OutdoorMult));

    private static bool Roll(float chance, RunConditions conditions, Random rng)
    {
        if (conditions.SeederBonus)
        {
            chance = ContagionRiskMath.SeederBonusChance(chance);
        }

        return rng.NextDouble() < Math.Min(1f, Math.Max(0f, chance));
    }

    // Two-sided respiratory reduction when a mask is worn; otherwise no reduction. Mirrors
    // ContagionApparelProtectionUtility.GetRespiratoryMaskFactor with a uniform seal quality.
    private static float MaskFactor(LiveVector v, RunConditions conditions)
    {
        if (!conditions.Ppe)
        {
            return 1f;
        }

        float source = 1f - conditions.MaskSeal * v.MaskSourceEffectiveness;
        float target = 1f - conditions.MaskSeal * v.MaskTargetEffectiveness;
        return Math.Max(0f, source) * Math.Max(0f, target);
    }

    private static float SuppressionFactor(ContagionSuppressionMode mode, DiseaseModel disease, int cases, int capacity)
    {
        if (mode == ContagionSuppressionMode.LetErRip || disease.SpreadSuppressionScale <= 0f || cases <= 0 || capacity <= 0)
        {
            return 1f;
        }

        float load = cases / (float)capacity;
        float factor = ContagionRiskMath.SpreadSuppressionFactor(mode, load);
        float scale = Math.Min(1f, Math.Max(0f, disease.SpreadSuppressionScale));
        return 1f + (factor - 1f) * scale;
    }

    private static float Infectivity(PawnState pawn, DiseaseModel disease)
    {
        if (pawn.Phase == Phase.Incubating)
        {
            return disease.IncubationCurve == null ? 0f : Math.Max(0f, disease.IncubationCurve.Evaluate(pawn.IncubationProgress));
        }

        if (pawn.Phase == Phase.Active)
        {
            return Math.Max(0f, disease.ActiveCurve.Evaluate(pawn.Severity));
        }

        return 0f;
    }

    // Untended vanilla immunity race: severity and immunity both climb; recovery when immunity reaches
    // 1.0, death if severity reaches the lethal threshold first. Freshly-infected pawns added this step
    // do not progress until the next step (they are advanced here only if already past Susceptible).
    private static void AdvanceProgression(PawnState[] pawns, DiseaseModel disease, float stepDays)
    {
        foreach (PawnState p in pawns)
        {
            if (p.Phase == Phase.Incubating)
            {
                p.IncubationProgress += stepDays / Math.Max(0.0001f, disease.IncubationDays);
                if (p.IncubationProgress >= 1f)
                {
                    p.Phase = Phase.Active;
                    p.Severity = 0f;
                }
            }
            else if (p.Phase == Phase.Active)
            {
                p.Severity += disease.SeverityPerDayNotImmune * stepDays;
                p.Immunity += disease.ImmunityPerDaySick * stepDays;
                if (p.Immunity >= 1f)
                {
                    p.Phase = Phase.Recovered;
                }
                else if (p.Severity >= disease.LethalSeverity)
                {
                    p.Phase = Phase.Dead;
                }
            }
        }
    }
}
