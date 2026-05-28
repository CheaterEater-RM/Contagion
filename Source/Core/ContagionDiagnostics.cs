using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Verse;

namespace Contagion;

public enum ContagionDiagnosticOrigin
{
    Unknown,
    Incidence,
    Spread
}

public enum ContagionDiagnosticCounter
{
    IncidenceApplicationSeeded,
    IncidenceApplicationBlocked,
    IncidenceApplicationBlockedByImmunity,
    SpreadApplicationSeeded,
    SpreadApplicationBlocked,
    SpreadApplicationBlockedByImmunity,
    StorytellerAttempted,
    StorytellerSeeded,
    StorytellerCancelled,
    ArrivalGroupChecked,
    ArrivalGroupSkippedEmpty,
    ArrivalNoDiseaseCandidates,
    ArrivalNoEligibleCarriers,
    ArrivalExposureSucceeded,
    ArrivalSeeded,
    ArrivalCarrierSeeded,
    ArrivalCarrierLatent,
    ArrivalCarrierMildVisible,
    EnvironmentalAttempted,
    EnvironmentalSeeded,
    ContinuousSeederAttempted,
    ContinuousSeederSeeded,
    ContinuousSeederNoEligiblePawn,
    PendingQueued,
    PendingDroppedAtCap,
    PendingDroppedDuplicate,
    PendingResolvedArrival,
    PendingResolvedAnimal,
    PendingExpiredToAcausal,
    EnvironmentalWindowOpened,
    EnvironmentalWindowClosedBudget,
    EnvironmentalWindowClosedExpiry,
    AirborneAttempted,
    AirborneSeeded,
    ProximityAttempted,
    ProximitySeeded,
    SocialAttempted,
    SocialSeeded,
    MealsContaminated,
    FoodborneAttempted,
    FoodborneSeeded,
    VomitFilthContaminated,
    FomiteAttempted,
    FomiteSeeded,
    Count
}

public enum ContagionPerformanceMetric
{
    TransmissionPass,
    EnvironmentalPass,
    Count
}

public static class ContagionDiagnostics
{
    private static readonly long[] Counters = new long[(int)ContagionDiagnosticCounter.Count];

    private static readonly long[] PerformanceCounts = new long[(int)ContagionPerformanceMetric.Count];

    private static readonly double[] PerformanceTotalMilliseconds = new double[(int)ContagionPerformanceMetric.Count];

    private static readonly double[] PerformanceMaxMilliseconds = new double[(int)ContagionPerformanceMetric.Count];

    private static readonly ContagionDiagnosticCounter[] IncidenceCounters =
    {
        ContagionDiagnosticCounter.IncidenceApplicationSeeded,
        ContagionDiagnosticCounter.IncidenceApplicationBlocked,
        ContagionDiagnosticCounter.IncidenceApplicationBlockedByImmunity,
        ContagionDiagnosticCounter.StorytellerAttempted,
        ContagionDiagnosticCounter.StorytellerSeeded,
        ContagionDiagnosticCounter.StorytellerCancelled,
        ContagionDiagnosticCounter.ArrivalGroupChecked,
        ContagionDiagnosticCounter.ArrivalGroupSkippedEmpty,
        ContagionDiagnosticCounter.ArrivalNoDiseaseCandidates,
        ContagionDiagnosticCounter.ArrivalNoEligibleCarriers,
        ContagionDiagnosticCounter.ArrivalExposureSucceeded,
        ContagionDiagnosticCounter.ArrivalSeeded,
        ContagionDiagnosticCounter.ArrivalCarrierSeeded,
        ContagionDiagnosticCounter.ArrivalCarrierLatent,
        ContagionDiagnosticCounter.ArrivalCarrierMildVisible,
        ContagionDiagnosticCounter.EnvironmentalAttempted,
        ContagionDiagnosticCounter.EnvironmentalSeeded,
        ContagionDiagnosticCounter.ContinuousSeederAttempted,
        ContagionDiagnosticCounter.ContinuousSeederSeeded,
        ContagionDiagnosticCounter.ContinuousSeederNoEligiblePawn,
        ContagionDiagnosticCounter.PendingQueued,
        ContagionDiagnosticCounter.PendingDroppedAtCap,
        ContagionDiagnosticCounter.PendingDroppedDuplicate,
        ContagionDiagnosticCounter.PendingResolvedArrival,
        ContagionDiagnosticCounter.PendingResolvedAnimal,
        ContagionDiagnosticCounter.PendingExpiredToAcausal,
        ContagionDiagnosticCounter.EnvironmentalWindowOpened,
        ContagionDiagnosticCounter.EnvironmentalWindowClosedBudget,
        ContagionDiagnosticCounter.EnvironmentalWindowClosedExpiry
    };

    private static readonly ContagionDiagnosticCounter[] SpreadCounters =
    {
        ContagionDiagnosticCounter.SpreadApplicationSeeded,
        ContagionDiagnosticCounter.SpreadApplicationBlocked,
        ContagionDiagnosticCounter.SpreadApplicationBlockedByImmunity,
        ContagionDiagnosticCounter.AirborneAttempted,
        ContagionDiagnosticCounter.AirborneSeeded,
        ContagionDiagnosticCounter.ProximityAttempted,
        ContagionDiagnosticCounter.ProximitySeeded,
        ContagionDiagnosticCounter.SocialAttempted,
        ContagionDiagnosticCounter.SocialSeeded,
        ContagionDiagnosticCounter.MealsContaminated,
        ContagionDiagnosticCounter.FoodborneAttempted,
        ContagionDiagnosticCounter.FoodborneSeeded,
        ContagionDiagnosticCounter.VomitFilthContaminated,
        ContagionDiagnosticCounter.FomiteAttempted,
        ContagionDiagnosticCounter.FomiteSeeded
    };

    private static bool HasDirectorSummary;

    private static float DirectorHumanPressureDebt;

    private static float DirectorAnimalPressureDebt;

    private static float DirectorRecentSeeding;

    private static float DirectorHumanBurden;

    private static float DirectorAnimalBurden;

    private static float DirectorMultiplier;

    public static bool Enabled => Contagion_Mod.Settings?.DiagnosticsEnabled ?? false;

    public static bool PerformanceEnabled => Enabled && (Contagion_Mod.Settings?.showPerformanceStats ?? false);

    public static bool VerboseEnabled => Contagion_Mod.Settings?.VerboseDiagnosticsEnabled ?? false;

    public static void Reset()
    {
        System.Array.Clear(Counters, 0, Counters.Length);
        System.Array.Clear(PerformanceCounts, 0, PerformanceCounts.Length);
        System.Array.Clear(PerformanceTotalMilliseconds, 0, PerformanceTotalMilliseconds.Length);
        System.Array.Clear(PerformanceMaxMilliseconds, 0, PerformanceMaxMilliseconds.Length);
        HasDirectorSummary = false;
    }

    public static void Record(ContagionDiagnosticCounter counter, int amount = 1)
    {
        if (!Enabled || amount == 0)
        {
            return;
        }

        Counters[(int)counter] += amount;
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

    public static long BeginTiming()
    {
        return PerformanceEnabled ? Stopwatch.GetTimestamp() : 0L;
    }

    public static void EndTiming(ContagionPerformanceMetric metric, long startTimestamp)
    {
        if (!PerformanceEnabled || startTimestamp == 0L)
        {
            return;
        }

        double elapsedMilliseconds = (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;
        int index = (int)metric;
        PerformanceCounts[index]++;
        PerformanceTotalMilliseconds[index] += elapsedMilliseconds;
        if (elapsedMilliseconds > PerformanceMaxMilliseconds[index])
        {
            PerformanceMaxMilliseconds[index] = elapsedMilliseconds;
        }
    }

    public static void Trace(string message)
    {
        if (!VerboseEnabled || message.NullOrEmpty())
        {
            return;
        }

        Log.Message($"[Contagion] {message}");
    }

    public static void UpdateDirectorSummary(
        float humanPressureDebt,
        float animalPressureDebt,
        float recentSeeding,
        float humanBurden,
        float animalBurden,
        float multiplier)
    {
        if (!Enabled)
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
        if (!Enabled)
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
        if (!Enabled)
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
        return stringBuilder.ToString();
    }

    public static string BuildSummaryReport()
    {
        return BuildIncidenceReport();
    }

    public static string BuildPerformanceReport()
    {
        if (!PerformanceEnabled)
        {
            return string.Empty;
        }

        if (PerformanceCounts[(int)ContagionPerformanceMetric.TransmissionPass] == 0
            && PerformanceCounts[(int)ContagionPerformanceMetric.EnvironmentalPass] == 0)
        {
            return "Contagion_DiagnosticsNoPerformanceData".Translate().Resolve();
        }

        StringBuilder stringBuilder = new StringBuilder();
        stringBuilder.AppendLine("Contagion_DiagnosticsPerformanceTransmission".Translate(
            FormatAverageMilliseconds(ContagionPerformanceMetric.TransmissionPass),
            FormatMaxMilliseconds(ContagionPerformanceMetric.TransmissionPass),
            PerformanceCounts[(int)ContagionPerformanceMetric.TransmissionPass]).Resolve());
        stringBuilder.Append("Contagion_DiagnosticsPerformanceEnvironmental".Translate(
            FormatAverageMilliseconds(ContagionPerformanceMetric.EnvironmentalPass),
            FormatMaxMilliseconds(ContagionPerformanceMetric.EnvironmentalPass),
            PerformanceCounts[(int)ContagionPerformanceMetric.EnvironmentalPass]).Resolve());
        return stringBuilder.ToString();
    }

    private static long GetCounter(ContagionDiagnosticCounter counter)
    {
        return Counters[(int)counter];
    }

    private static bool HasAnyRecordedCounters(IEnumerable<ContagionDiagnosticCounter> counters)
    {
        foreach (ContagionDiagnosticCounter counter in counters)
        {
            if (Counters[(int)counter] != 0)
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

    private static string FormatAverageMilliseconds(ContagionPerformanceMetric metric)
    {
        int index = (int)metric;
        if (PerformanceCounts[index] <= 0)
        {
            return "0.00";
        }

        return (PerformanceTotalMilliseconds[index] / PerformanceCounts[index]).ToString("0.00");
    }

    private static string FormatMaxMilliseconds(ContagionPerformanceMetric metric)
    {
        return PerformanceMaxMilliseconds[(int)metric].ToString("0.00");
    }
}
