using System;
using Contagion;

// Orthogonal toggles applied on top of a placement Scenario. Mirrors the mod-settings knobs that
// affect live transmission: suppression difficulty (Contagion_Settings.suppressionMode), the
// transmission difficulty multiplier (Contagion_Settings.EffectiveTransmissionMultiplier), plus
// per-run conditions (disease, PPE, indoor/outdoor, initial carriers).
internal sealed class RunConditions
{
    public string Disease = "Plague";
    public ContagionSuppressionMode Suppression = ContagionSuppressionMode.Medium;

    // Transmission difficulty multiplier: Easy 0.7 / Medium 1.0 / Hard 1.35 (matches EffectiveTransmissionMultiplier).
    public string Difficulty = "medium";
    public float TransmissionMultiplier = 1f;

    public bool Ppe;                       // mask worn -> applies the vector's two-sided respiratory reduction
    public float MaskSeal = 1f;            // 0..1 seal quality of the worn mask (1 = a clean, well-fitted mask)
    public bool Outdoor = true;            // open layouts only; Room scenarios are always enclosed

    public int InitialInfected = -1;       // -1 => use the scenario default
    public int Trials = 200;
    public int Days = 14;
    public int Seed = 1;

    public float SocialInteractionsPerPawnPerDay = 12f; // drives the flu Social vector cadence
    public float CleanlinessFactor = 1f;   // proximity local-cleanliness scalar (1 = clean)
    public float TargetSusceptibility = 1f; // vanilla DiseaseContractChanceFactor for a fresh, never-exposed pawn
    public bool SeederBonus;               // external->colony first-jump cube-root (off for single-group runs)

    // ── Global tuning dials ────────────────────────────────────────────────────────────────────
    // Ticks between transmission passes. Mirrors the production default in ContagionTransmissionTuningDef
    // (500 = 120 passes/day). Raising it slows pawn-to-pawn spread proportionally AND cuts CPU. 60000 ticks = 1 day.
    public int CheckIntervalTicks = 500;
    public float BaseChanceMult = 1f;      // scales every live vector's base chance (raw infectivity dial)
    public float OutdoorMult = 1f;         // scales every vector's outdoor/enclosure penalty (outdoor exposure dial)

    public static RunConditions Defaults(string disease)
    {
        RunConditions c = new() { Disease = disease };
        return c;
    }

    public static RunConditions Parse(string[] args, out string scenarioName)
    {
        scenarioName = "caravan";
        RunConditions c = new();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => ++i < args.Length ? args[i] : throw new ArgumentException($"Missing value after {a}.");
            switch (a)
            {
                case "--scenario": scenarioName = Next(); break;
                case "--disease": c.Disease = Next(); break;
                case "--suppression": c.Suppression = Program.ParseSuppression(Next()); break;
                case "--difficulty": c.SetDifficulty(Next()); break;
                case "--ppe": c.Ppe = ParseBool(Next()); break;
                case "--mask-seal": c.MaskSeal = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--indoor": c.Outdoor = false; break;
                case "--outdoor": c.Outdoor = true; break;
                case "--initial": c.InitialInfected = int.Parse(Next()); break;
                case "--trials": c.Trials = int.Parse(Next()); break;
                case "--days": c.Days = int.Parse(Next()); break;
                case "--seed": c.Seed = int.Parse(Next()); break;
                case "--social": c.SocialInteractionsPerPawnPerDay = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--cleanliness": c.CleanlinessFactor = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--seeder": c.SeederBonus = ParseBool(Next()); break;
                case "--check-interval": c.CheckIntervalTicks = int.Parse(Next()); break;
                case "--base-mult": c.BaseChanceMult = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--outdoor-mult": c.OutdoorMult = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                default: throw new ArgumentException($"Unknown argument '{a}'.");
            }
        }

        return c;
    }

    private void SetDifficulty(string value)
    {
        Difficulty = value.ToLowerInvariant();
        TransmissionMultiplier = Difficulty switch
        {
            "easy" => 0.7f,
            "medium" => 1f,
            "hard" => 1.35f,
            _ => throw new ArgumentException($"Unknown difficulty '{value}'."),
        };
    }

    private static bool ParseBool(string value) => value.ToLowerInvariant() switch
    {
        "on" or "true" or "yes" or "1" => true,
        "off" or "false" or "no" or "0" => false,
        _ => throw new ArgumentException($"Expected on/off, got '{value}'."),
    };

    public string SuppressionLabel => Suppression == ContagionSuppressionMode.LetErRip ? "off" : Suppression.ToString().ToLowerInvariant();
}
