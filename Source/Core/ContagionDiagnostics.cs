using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;
using Verse;

namespace Contagion;

public enum ContagionDiagnosticOrigin
{
    Unknown,
    Incidence,
    Spread
}

internal enum ContagionDiagnosticCounterCategory
{
    None,
    Incidence,
    Spread
}

[AttributeUsage(AttributeTargets.Field)]
internal sealed class DiagnosticCounterCategoryAttribute : Attribute
{
    public DiagnosticCounterCategoryAttribute(ContagionDiagnosticCounterCategory category)
    {
        Category = category;
    }

    public ContagionDiagnosticCounterCategory Category { get; }
}

public enum ContagionDiagnosticCounter
{
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Incidence)]
    IncidenceApplicationSeeded,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Incidence)]
    IncidenceApplicationBlocked,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Incidence)]
    IncidenceApplicationBlockedByImmunity,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Spread)]
    SpreadApplicationSeeded,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Spread)]
    SpreadApplicationBlocked,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Spread)]
    SpreadApplicationBlockedByImmunity,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Incidence)]
    StorytellerAttempted,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Incidence)]
    StorytellerSeeded,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Incidence)]
    StorytellerCancelled,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Incidence)]
    ArrivalGroupChecked,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Incidence)]
    ArrivalGroupSkippedEmpty,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Incidence)]
    ArrivalNoDiseaseCandidates,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Incidence)]
    ArrivalNoEligibleCarriers,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Incidence)]
    ArrivalExposureSucceeded,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Incidence)]
    ArrivalSeeded,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Incidence)]
    ArrivalCarrierSeeded,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Incidence)]
    ArrivalCarrierLatent,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Incidence)]
    ArrivalCarrierMildVisible,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Incidence)]
    EnvironmentalAttempted,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Incidence)]
    EnvironmentalSeeded,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Incidence)]
    ContinuousSeederAttempted,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Incidence)]
    ContinuousSeederSeeded,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Incidence)]
    ContinuousSeederNoEligiblePawn,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Incidence)]
    PendingQueued,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Incidence)]
    PendingDroppedAtCap,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Incidence)]
    PendingDroppedDuplicate,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Incidence)]
    PendingResolvedArrival,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Incidence)]
    PendingResolvedAnimal,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Incidence)]
    PendingExpiredToAcausal,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Incidence)]
    EnvironmentalWindowOpened,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Incidence)]
    EnvironmentalWindowClosedBudget,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Incidence)]
    EnvironmentalWindowClosedExpiry,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Spread)]
    AirborneAttempted,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Spread)]
    AirborneSeeded,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Spread)]
    ProximityAttempted,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Spread)]
    ProximitySeeded,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Spread)]
    SocialAttempted,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Spread)]
    SocialSeeded,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Spread)]
    MealsContaminated,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Spread)]
    FoodborneAttempted,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Spread)]
    FoodborneSeeded,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Spread)]
    VomitFilthContaminated,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Spread)]
    FomiteAttempted,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Spread)]
    FomiteSeeded,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Spread)]
    FecalOralEatingHotspotCreated,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Spread)]
    FecalOralEatingAttempted,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Spread)]
    FecalOralEatingSeeded,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Spread)]
    FecalOralLivingFilthContaminated,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Spread)]
    FecalOralLivingAttempted,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Spread)]
    FecalOralLivingSeeded,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Spread)]
    CorpseFleaAttempted,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Spread)]
    CorpseFleaSeeded,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Spread)]
    CorpseFluidAttempted,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Spread)]
    CorpseFluidSeeded,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Spread)]
    CookingExposureAttempted,
    [DiagnosticCounterCategory(ContagionDiagnosticCounterCategory.Spread)]
    CookingExposureSeeded,
    Count
}

public static class ContagionDiagnostics
{
    private static readonly long[] Counters = new long[(int)ContagionDiagnosticCounter.Count];

    private static readonly ContagionDiagnosticCounter[] IncidenceCounters = GetCounters(ContagionDiagnosticCounterCategory.Incidence);

    private static readonly ContagionDiagnosticCounter[] SpreadCounters = GetCounters(ContagionDiagnosticCounterCategory.Spread);

    private static bool HasDirectorSummary;

    private static float DirectorHumanPressureDebt;

    private static float DirectorAnimalPressureDebt;

    private static float DirectorRecentSeeding;

    private static float DirectorHumanBurden;

    private static float DirectorAnimalBurden;

    private static float DirectorMultiplier;

    // Logging gate: the single "enable logging" toggle. Controls every Log.Message the mod emits.
    public static bool LoggingEnabled => Contagion_Mod.Settings?.LoggingEnabled ?? false;

    // Counter gate: developer mode. The incidence/spread tallies and director summary live here.
    public static bool CountersEnabled => Contagion_Mod.Settings?.DeveloperDiagnosticsEnabled ?? false;

    public static void Reset()
    {
        for (int i = 0; i < Counters.Length; i++)
        {
            Interlocked.Exchange(ref Counters[i], 0);
        }

        HasDirectorSummary = false;
    }

    public static void Record(ContagionDiagnosticCounter counter, int amount = 1)
    {
        if (!CountersEnabled || amount == 0)
        {
            return;
        }

        Interlocked.Add(ref Counters[(int)counter], amount);
    }

    public static void RecordApplicationResult(ContagionDiagnosticOrigin origin, bool seeded, HediffDef immunityCause)
    {
        if (origin == ContagionDiagnosticOrigin.Incidence)
        {
            Record(seeded ? ContagionDiagnosticCounter.IncidenceApplicationSeeded : ContagionDiagnosticCounter.IncidenceApplicationBlocked);
            if (!seeded && immunityCause != null)
            {
                Record(ContagionDiagnosticCounter.IncidenceApplicationBlockedByImmunity);
            }
        }
        else if (origin == ContagionDiagnosticOrigin.Spread)
        {
            Record(seeded ? ContagionDiagnosticCounter.SpreadApplicationSeeded : ContagionDiagnosticCounter.SpreadApplicationBlocked);
            if (!seeded && immunityCause != null)
            {
                Record(ContagionDiagnosticCounter.SpreadApplicationBlockedByImmunity);
            }
        }
    }

    public static void Trace(string message)
    {
        if (!LoggingEnabled || message.NullOrEmpty())
        {
            return;
        }

        Log.Message($"[Contagion] {message}");
    }

    // Structured per-roll log line. Always emits passes; suppresses sub-10% failures when the
    // "suppress low-probability roll logs" toggle is on, so the log isn't drowned in 0.02% misses.
    public static void LogRoll(
        ContagionDebugVectorKind vector,
        Thing source,
        Thing target,
        HediffDef disease,
        float chance,
        bool passed)
    {
        if (!LoggingEnabled)
        {
            return;
        }

        if (!passed
            && (Contagion_Mod.Settings?.suppressLowProbabilityLogs ?? true)
            && chance < 0.10f)
        {
            return;
        }

        Log.Message(
            $"[Contagion] ROLL {vector} {(disease != null ? disease.defName : "?")} "
            + $"{DescribeThing(source)}→{DescribeThing(target)}: "
            + $"chance={chance:P2} → {(passed ? "PASS" : "fail")}");
    }

    // Verbose variant that dumps the factor breakdown for airborne/proximity rolls.
    public static void LogRoll(Thing source, Thing target, ContagionSpreadBreakdown breakdown, bool passed)
    {
        if (breakdown == null)
        {
            return;
        }

        if (!LoggingEnabled)
        {
            return;
        }

        float chance = Mathf.Clamp01(breakdown.FinalChance);
        if (!passed
            && (Contagion_Mod.Settings?.suppressLowProbabilityLogs ?? true)
            && chance < 0.10f)
        {
            return;
        }

        Log.Message(
            $"[Contagion] ROLL {breakdown.VectorKind} {(breakdown.DiseaseDef != null ? breakdown.DiseaseDef.defName : "?")} "
            + $"{DescribeThing(source)}→{DescribeThing(target)}: chance={chance:P2} → {(passed ? "PASS" : "fail")} "
            + $"[base={breakdown.BaseChance:0.###} infectivity={breakdown.Infectivity:0.###} "
            + $"dist={breakdown.Distance:0.#}/{breakdown.DistanceFactor:0.##} enclosure={breakdown.EnclosureFactor:0.##} "
            + $"obstruction={breakdown.ObstructionFactor:0.##} outdoor={breakdown.OutdoorFactor:0.##} "
            + $"cleanliness={breakdown.CleanlinessFactor:0.##} mask={breakdown.MaskFactor:0.##} "
            + $"suppression={breakdown.SuppressionFactor:0.##} eligibility={breakdown.TargetEligibilityFactor:0.##} "
            + $"seasonal={breakdown.SeasonalMultiplier:0.##} settings={breakdown.SettingsMultiplier:0.##}]");
    }

    private static string DescribeThing(Thing thing)
    {
        if (thing == null)
        {
            return "env";
        }

        return $"{thing.LabelShort}#{thing.thingIDNumber}";
    }

    public static void UpdateDirectorSummary(
        float humanPressureDebt,
        float animalPressureDebt,
        float recentSeeding,
        float humanBurden,
        float animalBurden,
        float multiplier)
    {
        if (!CountersEnabled)
        {
            return;
        }

        HasDirectorSummary = true;
        DirectorHumanPressureDebt = humanPressureDebt;
        DirectorAnimalPressureDebt = animalPressureDebt;
        DirectorRecentSeeding = recentSeeding;
        DirectorHumanBurden = humanBurden;
        DirectorAnimalBurden = animalBurden;
        DirectorMultiplier = multiplier;
    }

    public static string BuildIncidenceReport()
    {
        if (!CountersEnabled)
        {
            return string.Empty;
        }

        if (!HasAnyRecordedCounters(IncidenceCounters) && !HasDirectorSummary)
        {
            return "Contagion_DiagnosticsNoIncidence".Translate().Resolve();
        }

        StringBuilder stringBuilder = new StringBuilder();
        if (HasAnyRecordedCounters(IncidenceCounters))
        {
            stringBuilder.AppendLine("Contagion_DiagnosticsIncidenceApplications".Translate(
                GetCounter(ContagionDiagnosticCounter.IncidenceApplicationSeeded),
                GetCounter(ContagionDiagnosticCounter.IncidenceApplicationBlocked),
                GetCounter(ContagionDiagnosticCounter.IncidenceApplicationBlockedByImmunity)).Resolve());
            stringBuilder.AppendLine("Contagion_DiagnosticsIncidenceIntroductions".Translate(
                FormatSuccessAttempts(ContagionDiagnosticCounter.StorytellerSeeded, ContagionDiagnosticCounter.StorytellerAttempted),
                GetCounter(ContagionDiagnosticCounter.ArrivalExposureSucceeded),
                FormatSuccessAttempts(ContagionDiagnosticCounter.EnvironmentalSeeded, ContagionDiagnosticCounter.EnvironmentalAttempted),
                FormatSuccessAttempts(ContagionDiagnosticCounter.ContinuousSeederSeeded, ContagionDiagnosticCounter.ContinuousSeederAttempted),
                GetCounter(ContagionDiagnosticCounter.StorytellerCancelled)).Resolve());
            stringBuilder.AppendLine("Contagion_DiagnosticsIncidencePending".Translate(
                GetCounter(ContagionDiagnosticCounter.PendingQueued),
                GetCounter(ContagionDiagnosticCounter.PendingDroppedAtCap),
                GetCounter(ContagionDiagnosticCounter.PendingDroppedDuplicate),
                GetCounter(ContagionDiagnosticCounter.PendingResolvedArrival),
                GetCounter(ContagionDiagnosticCounter.PendingResolvedAnimal),
                GetCounter(ContagionDiagnosticCounter.PendingExpiredToAcausal),
                GetCounter(ContagionDiagnosticCounter.EnvironmentalWindowOpened),
                GetCounter(ContagionDiagnosticCounter.EnvironmentalWindowClosedBudget)
                    + GetCounter(ContagionDiagnosticCounter.EnvironmentalWindowClosedExpiry)).Resolve());
            stringBuilder.AppendLine("Contagion_DiagnosticsIncidenceArrivals".Translate(
                GetCounter(ContagionDiagnosticCounter.ArrivalGroupChecked),
                GetCounter(ContagionDiagnosticCounter.ArrivalGroupSkippedEmpty),
                GetCounter(ContagionDiagnosticCounter.ArrivalNoDiseaseCandidates),
                GetCounter(ContagionDiagnosticCounter.ArrivalNoEligibleCarriers),
                GetCounter(ContagionDiagnosticCounter.ArrivalCarrierSeeded),
                GetCounter(ContagionDiagnosticCounter.ArrivalCarrierLatent),
                GetCounter(ContagionDiagnosticCounter.ArrivalCarrierMildVisible)).Resolve());
        }

        AppendDirectorSummary(stringBuilder);
        return stringBuilder.ToString();
    }

    public static string BuildSpreadReport()
    {
        if (!CountersEnabled)
        {
            return string.Empty;
        }

        if (!HasAnyRecordedCounters(SpreadCounters))
        {
            return "Contagion_DiagnosticsNoSpread".Translate().Resolve();
        }

        StringBuilder stringBuilder = new StringBuilder();
        stringBuilder.AppendLine("Contagion_DiagnosticsSpreadApplications".Translate(
            GetCounter(ContagionDiagnosticCounter.SpreadApplicationSeeded),
            GetCounter(ContagionDiagnosticCounter.SpreadApplicationBlocked),
            GetCounter(ContagionDiagnosticCounter.SpreadApplicationBlockedByImmunity)).Resolve());
        stringBuilder.AppendLine("Contagion_DiagnosticsSpreadVectors".Translate(
            FormatSuccessAttempts(ContagionDiagnosticCounter.AirborneSeeded, ContagionDiagnosticCounter.AirborneAttempted),
            FormatSuccessAttempts(ContagionDiagnosticCounter.ProximitySeeded, ContagionDiagnosticCounter.ProximityAttempted),
            FormatSuccessAttempts(ContagionDiagnosticCounter.SocialSeeded, ContagionDiagnosticCounter.SocialAttempted),
            FormatSuccessAttempts(ContagionDiagnosticCounter.FoodborneSeeded, ContagionDiagnosticCounter.FoodborneAttempted),
            FormatSuccessAttempts(ContagionDiagnosticCounter.FomiteSeeded, ContagionDiagnosticCounter.FomiteAttempted)).Resolve());
        stringBuilder.Append("Contagion_DiagnosticsSpreadContamination".Translate(
            GetCounter(ContagionDiagnosticCounter.MealsContaminated),
            GetCounter(ContagionDiagnosticCounter.VomitFilthContaminated)).Resolve());
        stringBuilder.AppendLine();
        stringBuilder.Append("Contagion_DiagnosticsSpreadFecalOral".Translate(
            FormatSuccessAttempts(ContagionDiagnosticCounter.FecalOralEatingSeeded, ContagionDiagnosticCounter.FecalOralEatingAttempted),
            FormatSuccessAttempts(ContagionDiagnosticCounter.FecalOralLivingSeeded, ContagionDiagnosticCounter.FecalOralLivingAttempted),
            GetCounter(ContagionDiagnosticCounter.FecalOralEatingHotspotCreated),
            GetCounter(ContagionDiagnosticCounter.FecalOralLivingFilthContaminated)).Resolve());
        return stringBuilder.ToString();
    }

    private static ContagionDiagnosticCounter[] GetCounters(ContagionDiagnosticCounterCategory category)
    {
        Array values = Enum.GetValues(typeof(ContagionDiagnosticCounter));
        List<ContagionDiagnosticCounter> counters = new List<ContagionDiagnosticCounter>();
        for (int i = 0; i < values.Length; i++)
        {
            ContagionDiagnosticCounter counter = (ContagionDiagnosticCounter)values.GetValue(i);
            if (GetCounterCategory(counter) == category)
            {
                counters.Add(counter);
            }
        }

        return counters.ToArray();
    }

    private static ContagionDiagnosticCounterCategory GetCounterCategory(ContagionDiagnosticCounter counter)
    {
        object[] attributes = typeof(ContagionDiagnosticCounter)
            .GetField(counter.ToString())
            ?.GetCustomAttributes(typeof(DiagnosticCounterCategoryAttribute), inherit: false);
        return attributes != null && attributes.Length > 0
            ? ((DiagnosticCounterCategoryAttribute)attributes[0]).Category
            : ContagionDiagnosticCounterCategory.None;
    }

    private static long GetCounter(ContagionDiagnosticCounter counter)
    {
        return Interlocked.Read(ref Counters[(int)counter]);
    }

    private static bool HasAnyRecordedCounters(IEnumerable<ContagionDiagnosticCounter> counters)
    {
        foreach (ContagionDiagnosticCounter counter in counters)
        {
            if (GetCounter(counter) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void AppendDirectorSummary(StringBuilder stringBuilder)
    {
        if (!HasDirectorSummary)
        {
            return;
        }

        stringBuilder.Append("Contagion_DiagnosticsSummaryDirector".Translate(
            DirectorHumanPressureDebt.ToString("0.##"),
            DirectorAnimalPressureDebt.ToString("0.##"),
            DirectorHumanBurden.ToString("0.###"),
            DirectorAnimalBurden.ToString("0.###"),
            DirectorRecentSeeding.ToString("0.##"),
            DirectorMultiplier.ToString("0.##")).Resolve());
    }

    private static string FormatSuccessAttempts(ContagionDiagnosticCounter successCounter, ContagionDiagnosticCounter attemptCounter)
    {
        return $"{GetCounter(successCounter)}/{GetCounter(attemptCounter)}";
    }
}
