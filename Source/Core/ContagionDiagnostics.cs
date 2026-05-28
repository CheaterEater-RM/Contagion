using System.Diagnostics;
using System.Text;
using Verse;

namespace Contagion;

public enum ContagionDiagnosticCounter
{
    IncubationSeeded,
    IncubationBlocked,
    IncubationBlockedByImmunity,
    StorytellerAttempted,
    StorytellerSeeded,
    ArrivalAttempted,
    ArrivalSeeded,
    EnvironmentalAttempted,
    EnvironmentalSeeded,
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

    public static string BuildSummaryReport()
    {
        if (!Enabled)
        {
            return string.Empty;
        }

        if (!HasAnyRecordedCounters() && !HasDirectorSummary)
        {
            return "Contagion_DiagnosticsNoEvents".Translate().Resolve();
        }

        StringBuilder stringBuilder = new StringBuilder();
        if (HasAnyRecordedCounters())
        {
            stringBuilder.AppendLine("Contagion_DiagnosticsSummaryIncubation".Translate(
                GetCounter(ContagionDiagnosticCounter.IncubationSeeded),
                GetCounter(ContagionDiagnosticCounter.IncubationBlocked),
                GetCounter(ContagionDiagnosticCounter.IncubationBlockedByImmunity)).Resolve());
            stringBuilder.AppendLine("Contagion_DiagnosticsSummarySeeding".Translate(
                FormatSuccessAttempts(ContagionDiagnosticCounter.StorytellerSeeded, ContagionDiagnosticCounter.StorytellerAttempted),
                FormatSuccessAttempts(ContagionDiagnosticCounter.ArrivalSeeded, ContagionDiagnosticCounter.ArrivalAttempted),
                FormatSuccessAttempts(ContagionDiagnosticCounter.EnvironmentalSeeded, ContagionDiagnosticCounter.EnvironmentalAttempted)).Resolve());
            stringBuilder.AppendLine("Contagion_DiagnosticsSummarySpread".Translate(
                FormatSuccessAttempts(ContagionDiagnosticCounter.AirborneSeeded, ContagionDiagnosticCounter.AirborneAttempted),
                FormatSuccessAttempts(ContagionDiagnosticCounter.ProximitySeeded, ContagionDiagnosticCounter.ProximityAttempted),
                FormatSuccessAttempts(ContagionDiagnosticCounter.SocialSeeded, ContagionDiagnosticCounter.SocialAttempted)).Resolve());
            stringBuilder.AppendLine("Contagion_DiagnosticsSummaryContamination".Translate(
                GetCounter(ContagionDiagnosticCounter.MealsContaminated),
                FormatSuccessAttempts(ContagionDiagnosticCounter.FoodborneSeeded, ContagionDiagnosticCounter.FoodborneAttempted),
                GetCounter(ContagionDiagnosticCounter.VomitFilthContaminated),
                FormatSuccessAttempts(ContagionDiagnosticCounter.FomiteSeeded, ContagionDiagnosticCounter.FomiteAttempted)).Resolve());
        }

        AppendDirectorSummary(stringBuilder);
        return stringBuilder.ToString();
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

    private static bool HasAnyRecordedCounters()
    {
        for (int i = 0; i < Counters.Length; i++)
        {
            if (Counters[i] != 0)
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
