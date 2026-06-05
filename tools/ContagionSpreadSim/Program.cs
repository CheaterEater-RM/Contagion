using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Contagion;

// Offline multi-pawn spread simulator (companion to tools/ContagionRiskAudit). Where the audit
// checks single-pair chances pointwise, this steps a GROUP of virtual pawns over time so we can
// tune how fast a disease saturates a caravan / room and whether it burns out. It reuses the exact
// production risk math (ContagionRiskMath.*) and reads the live XML profiles, and reimplements only
// the per-pass aggregation + vanilla severity/immunity progression as scenario-driven inputs.
// The transmission pass cadence is a dial (--check-interval, default 500 ticks = 120 passes/day,
// matching ContagionTransmissionTuningDef); per-check chances compound across that many passes/day.
//
// A SCENARIO is purely a pawn-placement layout. Everything else (disease, PPE, suppression
// difficulty, transmission difficulty, indoor/outdoor, initial infected, trials) is an orthogonal
// toggle in RunConditions. Run with no args for the canonical matrix, or pass --flags for one run.
internal static class Program
{
    private static readonly string Root = FindRepoRoot();

    private static int Main(string[] args)
    {
        XDocument profiles = XDocument.Load(Path.Combine(Root, "1.6", "Patches", "Contagion_Profiles.xml"));
        Dictionary<string, DiseaseModel> diseases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Flu"] = DiseaseModel.Load(profiles, "Flu", incubationDays: 1.0f, immunityPerDaySick: 0.2388f, severityPerDayNotImmune: 0.2488f),
            ["Plague"] = DiseaseModel.Load(profiles, "Plague", incubationDays: 2.0f, immunityPerDaySick: 0.5224f, severityPerDayNotImmune: 0.666f),
        };

        Dictionary<string, Scenario> scenarios = Scenario.Catalog();

        if (args.Length > 0)
        {
            RunConditions single = RunConditions.Parse(args, out string scenarioName);
            if (!scenarios.TryGetValue(scenarioName, out Scenario scenario))
            {
                Console.Error.WriteLine($"Unknown scenario '{scenarioName}'. Known: {string.Join(", ", scenarios.Keys)}");
                return 2;
            }

            RunResult result = Simulator.Run(scenario, single, diseases[single.Disease]);
            Reporter.PrintHeader();
            Reporter.PrintConfig(single);
            Reporter.PrintRow(scenario, single, result);
            Reporter.PrintAlerts(scenario, single, result);
            return 0;
        }

        RunCanonicalMatrix(scenarios, diseases);
        return 0;
    }

    // The default report: a curated matrix exercising the caravan blow-up across suppression modes,
    // the two-pawn baseline (cross-check vs the pointwise audit), and the four room presets.
    private static void RunCanonicalMatrix(Dictionary<string, Scenario> scenarios, Dictionary<string, DiseaseModel> diseases)
    {
        Reporter.PrintHeader();
        List<(Scenario s, RunConditions c, RunResult r)> rows = new();

        // Caravan plague blow-up across the four suppression difficulties, outdoors, no PPE.
        foreach (string suppression in new[] { "off", "weak", "medium", "strong" })
        {
            RunConditions c = RunConditions.Defaults("Plague");
            c.Suppression = ParseSuppression(suppression);
            c.Outdoor = true;
            c.Ppe = false;
            Add(rows, scenarios["caravan"], c, diseases);
        }

        // Caravan plague: medium suppression with PPE on, to show mask leverage.
        {
            RunConditions c = RunConditions.Defaults("Plague");
            c.Suppression = ContagionSuppressionMode.Medium;
            c.Outdoor = true;
            c.Ppe = true;
            Add(rows, scenarios["caravan"], c, diseases);
        }

        // Two-pawn baseline (indoor, no suppression so the raw per-pair pressure shows), plague + flu.
        foreach (string disease in new[] { "Plague", "Flu" })
        {
            RunConditions c = RunConditions.Defaults(disease);
            c.Suppression = ContagionSuppressionMode.LetErRip;
            c.Outdoor = false;
            Add(rows, scenarios["two-pawn"], c, diseases);
        }

        // Room presets, flu (airborne+social), medium suppression, indoor, no PPE.
        foreach (string room in new[] { "barracks", "hospital", "dining-rec", "workshop" })
        {
            RunConditions c = RunConditions.Defaults("Flu");
            c.Suppression = ContagionSuppressionMode.Medium;
            c.Outdoor = false;
            Add(rows, scenarios[room], c, diseases);
        }

        // Burn-out demonstration: tightly-packed barracks, plague, medium suppression. Plague's fast
        // immunity race (immunityPerDaySick 0.5224) should let it burn out rather than stay endemic.
        {
            RunConditions c = RunConditions.Defaults("Plague");
            c.Suppression = ContagionSuppressionMode.Medium;
            c.Outdoor = false;
            Add(rows, scenarios["barracks"], c, diseases);
        }

        Console.WriteLine();
        foreach ((Scenario s, RunConditions c, RunResult r) in rows)
        {
            Reporter.PrintAlerts(s, c, r);
        }
    }

    private static void Add(
        List<(Scenario, RunConditions, RunResult)> rows,
        Scenario scenario,
        RunConditions conditions,
        Dictionary<string, DiseaseModel> diseases)
    {
        RunResult result = Simulator.Run(scenario, conditions, diseases[conditions.Disease]);
        Reporter.PrintRow(scenario, conditions, result);
        rows.Add((scenario, conditions, result));
    }

    internal static ContagionSuppressionMode ParseSuppression(string value) => value.ToLowerInvariant() switch
    {
        "strong" => ContagionSuppressionMode.Strong,
        "medium" => ContagionSuppressionMode.Medium,
        "weak" => ContagionSuppressionMode.Weak,
        "off" or "leterrip" or "let-er-rip" => ContagionSuppressionMode.LetErRip,
        _ => throw new ArgumentException($"Unknown suppression mode '{value}'."),
    };

    private static string FindRepoRoot()
    {
        DirectoryInfo directory = new(AppDomain.CurrentDomain.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Source", "Contagion.csproj"))
                && File.Exists(Path.Combine(directory.FullName, "1.6", "Patches", "Contagion_Profiles.xml")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find Contagion repo root from sim output directory.");
    }
}
