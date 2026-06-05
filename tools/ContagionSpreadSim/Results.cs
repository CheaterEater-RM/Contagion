using System.Collections.Generic;
using System.Linq;

// Per-trial outcome. Day fields default to -1 ("never reached within the sim window").
internal sealed class TrialMetrics
{
    public int PawnCount;
    public int Capacity;
    public float DaysToFirst = -1f;
    public float DaysTo50 = -1f;
    public float DaysToSaturation = -1f;
    public float ClearedDay = -1f;
    public float EverInfectedPct;
    public int PeakActive;
    public float PeakActivePctCap;
    public bool Cleared;
    public bool BurnedOut;
}

// Aggregated across Monte-Carlo trials.
internal sealed class RunResult
{
    public int PawnCount;
    public int Capacity;
    public int Days;
    public float MeanEverInfectedPct;
    public float MeanPeakActive;
    public float MeanPeakActivePctCap;
    public float SaturationRate;       // fraction of trials that reached 100% ever-infected
    public float MedianDaysToFirst;    // -1 if fewer than half the trials reached it
    public float MedianDaysTo50;
    public float MedianDaysToSaturation;
    public float BurnedOutRate;        // fraction of trials that cleared with survivors still susceptible

    public static RunResult Summarize(List<TrialMetrics> trials, int days, int pawnCount, int capacity)
    {
        RunResult r = new()
        {
            PawnCount = pawnCount,
            Capacity = capacity,
            Days = days,
            MeanEverInfectedPct = trials.Average(t => t.EverInfectedPct),
            MeanPeakActive = (float)trials.Average(t => t.PeakActive),
            MeanPeakActivePctCap = trials.Average(t => t.PeakActivePctCap),
            SaturationRate = trials.Count(t => t.DaysToSaturation >= 0f) / (float)trials.Count,
            BurnedOutRate = trials.Count(t => t.BurnedOut) / (float)trials.Count,
            MedianDaysToFirst = MedianOfReached(trials.Select(t => t.DaysToFirst), trials.Count),
            MedianDaysTo50 = MedianOfReached(trials.Select(t => t.DaysTo50), trials.Count),
            MedianDaysToSaturation = MedianOfReached(trials.Select(t => t.DaysToSaturation), trials.Count),
        };
        return r;
    }

    // Median over the trials that actually reached the milestone; -1 when fewer than half did, so a
    // rarely-reached milestone reads "—" rather than a misleadingly small number.
    private static float MedianOfReached(IEnumerable<float> values, int totalTrials)
    {
        List<float> reached = values.Where(v => v >= 0f).OrderBy(v => v).ToList();
        if (reached.Count * 2 < totalTrials)
        {
            return -1f;
        }

        int mid = reached.Count / 2;
        return reached.Count % 2 == 1 ? reached[mid] : (reached[mid - 1] + reached[mid]) / 2f;
    }
}
