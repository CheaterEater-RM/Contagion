using System;
using System.Collections.Generic;
using Contagion;

// Renders the exploratory table and the non-fatal "ALERT" lines. Bands are suggestions to look at,
// never a hard gate — the sim always exits 0.
internal static class Reporter
{
    public static void PrintHeader()
    {
        Console.WriteLine();
        Console.WriteLine("Contagion spread simulation — live pawn-to-pawn vectors (default 500-tick interval = 120 passes/day; --check-interval to vary).");
        Console.WriteLine("ever% = mean % ever-infected · peak = mean peak simultaneous cases (% of suppression cap)");
        Console.WriteLine("t1st/t50/tSat = median days to first secondary / 50% / 100% infected · sat% = trials reaching 100% · burn% = trials that burned out");
        Console.WriteLine();
        Console.WriteLine(string.Format(
            "{0,-11} {1,-7} {2,-7} {3,-7} {4,-4} {5,-4} {6,3} {7,4}  {8,6} {9,14}  {10,6} {11,6} {12,6} {13,5} {14,5}",
            "scenario", "disease", "supp", "diff", "ppe", "in", "N", "cap",
            "ever%", "peak(%cap)", "t1st", "t50", "tSat", "sat%", "burn%"));
        Console.WriteLine(new string('-', 118));
    }

    public static void PrintConfig(RunConditions c)
    {
        Console.WriteLine($"config: interval={c.CheckIntervalTicks}t ({60000 / c.CheckIntervalTicks}/day) · base-mult={c.BaseChanceMult:0.##} · outdoor-mult={c.OutdoorMult:0.##} · difficulty={c.Difficulty} · trials={c.Trials} · days={c.Days}");
        Console.WriteLine();
    }

    public static void PrintEnvironmentalHeader()
    {
        Console.WriteLine();
        Console.WriteLine("Contagion spread simulation — environmental infectivity during an open window (default 2500-tick interval = 24 passes/day).");
        Console.WriteLine("inf% = mean % infected by window close · any% = trials with at least one infection · budget% = trials that hit the active window budget");
        Console.WriteLine("p/pass = daily-MEAN per-target chance at zero load (time-of-day + schedule averaged) · t1st/t50/tBud = median days to first / 50% / budget reached");
        Console.WriteLine("sched = when outdoors: always · day-out (sheltered at night) · night-out (sheltered by day)");
        Console.WriteLine();
        Console.WriteLine(string.Format(
            "{0,-16} {1,-7} {2,-7} {3,-9} {4,3} {5,4} {6,6} {7,5} {8,7} {9,7} {10,6} {11,6} {12,6} {13,6} {14,7} {15,7}",
            "disease", "target", "supp", "sched", "N", "cap", "budget", "win", "p/pass", "inf%", "mean", "any%", "budget%", "t1st", "t50", "tBud"));
        Console.WriteLine(new string('-', 138));
    }

    public static void PrintEnvironmentalConfig(RunConditions c, DiseaseModel disease)
    {
        float temp = c.EnvironmentalTemperature ?? disease.EnvironmentalVector?.PeakTemperature ?? 0f;
        Console.WriteLine($"config: env-interval={c.EnvironmentalCheckIntervalTicks}t ({60000 / c.EnvironmentalCheckIntervalTicks}/day) · base-mult={c.BaseChanceMult:0.##} · water-factor={c.EnvironmentalWaterFactor:0.##} · temp={temp:0.#}C · {(c.Outdoor ? "outdoor" : $"indoor/{c.EnvironmentalCellsFromUnroofed} cells")} · schedule={c.EnvironmentalScheduleLabel} · trials={c.Trials}");
        Console.WriteLine();
    }

    public static void PrintRow(Scenario scenario, RunConditions c, RunResult r)
    {
        Console.WriteLine(string.Format(
            "{0,-11} {1,-7} {2,-7} {3,-7} {4,-4} {5,-4} {6,3} {7,4}  {8,5:0.0} {9,14}  {10,6} {11,6} {12,6} {13,4:0}% {14,4:0}%",
            scenario.Name,
            c.Disease.ToLowerInvariant(),
            c.SuppressionLabel,
            c.Difficulty,
            c.Ppe ? "on" : "off",
            scenario.Kind == PlacementKind.Room ? "in" : (c.Outdoor ? "out" : "in"),
            r.PawnCount,
            r.Capacity,
            r.MeanEverInfectedPct,
            string.Format("{0,4:0.0} ({1,3:0}%)", r.MeanPeakActive, r.MeanPeakActivePctCap),
            Days(r.MedianDaysToFirst),
            Days(r.MedianDaysTo50),
            Days(r.MedianDaysToSaturation),
            r.SaturationRate * 100f,
            r.BurnedOutRate * 100f));
    }

    public static void PrintEnvironmentalRow(RunConditions c, EnvironmentalRunResult r)
    {
        Console.WriteLine(string.Format(
            "{0,-16} {1,-7} {2,-7} {3,-9} {4,3} {5,4} {6,6} {7,5:0.#} {8,6:0.00}% {9,6:0.0}% {10,6:0.0} {11,5:0}% {12,6:0}% {13,6} {14,7} {15,7}",
            c.Disease.ToLowerInvariant(),
            c.EnvironmentalTargetKindLabel,
            c.SuppressionLabel,
            c.EnvironmentalScheduleLabel,
            r.TargetCount,
            r.Capacity,
            Budget(r),
            r.WindowDays,
            r.MeanPerPassChanceAtStart * 100f,
            r.MeanInfectedPct,
            r.MeanInfected,
            r.AnyInfectedRate * 100f,
            r.BudgetReachedRate * 100f,
            Days(r.MedianDaysToFirst),
            Days(r.MedianDaysTo50),
            Days(r.MedianDaysToBudget)));
    }

    public static void PrintAlerts(Scenario scenario, RunConditions c, RunResult r)
    {
        List<string> alerts = new();
        string tag = $"{scenario.Name}/{c.Disease.ToLowerInvariant()}/supp={c.SuppressionLabel}/ppe={(c.Ppe ? "on" : "off")}";
        bool suppressionOn = c.Suppression != ContagionSuppressionMode.LetErRip;

        // Band 1 — suppression should hold a group below full saturation within a realistic stay.
        // Trade caravans leave in ~1-2 days; if suppression is on but the whole group still saturates
        // that fast, suppression is too weak for this layout.
        if (suppressionOn && r.MedianDaysToSaturation >= 0f && r.MedianDaysToSaturation <= 2f && r.SaturationRate > 0.5f)
        {
            alerts.Add($"suppression looks too weak — 100% infected in a median {r.MedianDaysToSaturation:0.0}d ({r.SaturationRate * 100f:0}% of trials) despite '{c.SuppressionLabel}' suppression.");
        }

        // Band 2 — with suppression on, peak simultaneous cases should sit near the cap, not blow past it.
        if (suppressionOn && r.MeanPeakActivePctCap > 160f)
        {
            alerts.Add($"peak active load {r.MeanPeakActivePctCap:0}% of cap — suppression is not holding the active-case ceiling.");
        }

        // Band 3 — model sanity. With suppression OFF in a dense layout the disease is expected to run
        // away; if it doesn't, the placement/range/curve inputs probably look wrong.
        bool dense = scenario.Name is "caravan" or "barracks" or "dining-rec";
        if (!suppressionOn && dense && r.MeanEverInfectedPct < 60f)
        {
            alerts.Add($"model sanity — only {r.MeanEverInfectedPct:0}% infected with suppression OFF in a dense layout; expected near-saturation. Check ranges/curves.");
        }

        foreach (string a in alerts)
        {
            Console.WriteLine($"ALERT  {tag}: {a}");
        }
    }

    private static string Days(float d) => d < 0f ? "—" : $"{d:0.0}d";

    private static string Budget(EnvironmentalRunResult r)
        => r.MeanBudget < 0f ? "none" : r.MeanBudget.ToString("0.0");
}
