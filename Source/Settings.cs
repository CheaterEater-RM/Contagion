using UnityEngine;
using Verse;

namespace Contagion;

public enum ContagionDiagnosticsMode
{
    Off,
    Summary,
    Verbose
}

public sealed class Contagion_Settings : ModSettings
{
    public const float MinMultiplier = 0.25f;

    public const float MaxMultiplier = 2f;

    private const float DefaultTransmissionRateMultiplier = 1f;

    private const float DefaultOutbreakFrequencyMultiplier = 1f;

    private const float DefaultIncubationLengthMultiplier = 1f;

    private const ContagionDiagnosticsMode DefaultDiagnosticsMode = ContagionDiagnosticsMode.Off;

    private const bool DefaultShowPerformanceStats = false;

    public float transmissionRateMultiplier = DefaultTransmissionRateMultiplier;

    public float outbreakFrequencyMultiplier = DefaultOutbreakFrequencyMultiplier;

    public float incubationLengthMultiplier = DefaultIncubationLengthMultiplier;

    public ContagionDiagnosticsMode diagnosticsMode = DefaultDiagnosticsMode;

    public bool showPerformanceStats = DefaultShowPerformanceStats;

    public bool DiagnosticsEnabled => diagnosticsMode != ContagionDiagnosticsMode.Off;

    public bool VerboseDiagnosticsEnabled => diagnosticsMode == ContagionDiagnosticsMode.Verbose && Prefs.DevMode;

    public void Reset()
    {
        transmissionRateMultiplier = DefaultTransmissionRateMultiplier;
        outbreakFrequencyMultiplier = DefaultOutbreakFrequencyMultiplier;
        incubationLengthMultiplier = DefaultIncubationLengthMultiplier;
        diagnosticsMode = DefaultDiagnosticsMode;
        showPerformanceStats = DefaultShowPerformanceStats;
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref transmissionRateMultiplier, "transmissionRateMultiplier", DefaultTransmissionRateMultiplier);
        Scribe_Values.Look(ref outbreakFrequencyMultiplier, "outbreakFrequencyMultiplier", DefaultOutbreakFrequencyMultiplier);
        Scribe_Values.Look(ref incubationLengthMultiplier, "incubationLengthMultiplier", DefaultIncubationLengthMultiplier);
        Scribe_Values.Look(ref diagnosticsMode, "diagnosticsMode", DefaultDiagnosticsMode);
        Scribe_Values.Look(ref showPerformanceStats, "showPerformanceStats", DefaultShowPerformanceStats);

        ClampValues();
    }

    private void ClampValues()
    {
        transmissionRateMultiplier = Mathf.Clamp(transmissionRateMultiplier, MinMultiplier, MaxMultiplier);
        outbreakFrequencyMultiplier = Mathf.Clamp(outbreakFrequencyMultiplier, MinMultiplier, MaxMultiplier);
        incubationLengthMultiplier = Mathf.Clamp(incubationLengthMultiplier, MinMultiplier, MaxMultiplier);
    }
}

public sealed class Contagion_Mod : Mod
{
    public static Contagion_Settings Settings { get; private set; }

    public Contagion_Mod(ModContentPack content)
        : base(content)
    {
        Settings = GetSettings<Contagion_Settings>();
    }

    public override string SettingsCategory()
    {
        return "Contagion_SettingsCategory".Translate();
    }

    public override void DoSettingsWindowContents(Rect inRect)
    {
        Contagion_Settings settings = Settings;
        Listing_Standard listing = new Listing_Standard();
        listing.Begin(inRect);

        listing.Label("Contagion_SettingsGameplayHeader".Translate());
        listing.Gap();
        settings.transmissionRateMultiplier = listing.SliderLabeled(
            "Contagion_SettingTransmissionRate".Translate(settings.transmissionRateMultiplier.ToString("0.00x")).Resolve(),
            settings.transmissionRateMultiplier,
            Contagion_Settings.MinMultiplier,
            Contagion_Settings.MaxMultiplier,
            tooltip: "Contagion_SettingTransmissionRateTooltip".Translate().Resolve());
        settings.outbreakFrequencyMultiplier = listing.SliderLabeled(
            "Contagion_SettingOutbreakFrequency".Translate(settings.outbreakFrequencyMultiplier.ToString("0.00x")).Resolve(),
            settings.outbreakFrequencyMultiplier,
            Contagion_Settings.MinMultiplier,
            Contagion_Settings.MaxMultiplier,
            tooltip: "Contagion_SettingOutbreakFrequencyTooltip".Translate().Resolve());
        settings.incubationLengthMultiplier = listing.SliderLabeled(
            "Contagion_SettingIncubationLength".Translate(settings.incubationLengthMultiplier.ToString("0.00x")).Resolve(),
            settings.incubationLengthMultiplier,
            Contagion_Settings.MinMultiplier,
            Contagion_Settings.MaxMultiplier,
            tooltip: "Contagion_SettingIncubationLengthTooltip".Translate().Resolve());

        listing.Gap(12f);
        listing.Label("Contagion_SettingsDiagnosticsHeader".Translate());
        listing.Gap();

        if (listing.RadioButton(
            "Contagion_DiagnosticsModeOff".Translate().Resolve(),
            settings.diagnosticsMode == ContagionDiagnosticsMode.Off,
            tooltip: "Contagion_DiagnosticsModeOffTooltip".Translate().Resolve()))
        {
            settings.diagnosticsMode = ContagionDiagnosticsMode.Off;
        }

        if (listing.RadioButton(
            "Contagion_DiagnosticsModeSummary".Translate().Resolve(),
            settings.diagnosticsMode == ContagionDiagnosticsMode.Summary,
            tooltip: "Contagion_DiagnosticsModeSummaryTooltip".Translate().Resolve()))
        {
            settings.diagnosticsMode = ContagionDiagnosticsMode.Summary;
        }

        if (listing.RadioButton(
            "Contagion_DiagnosticsModeVerbose".Translate().Resolve(),
            settings.diagnosticsMode == ContagionDiagnosticsMode.Verbose,
            tooltip: "Contagion_DiagnosticsModeVerboseTooltip".Translate().Resolve()))
        {
            settings.diagnosticsMode = ContagionDiagnosticsMode.Verbose;
        }

        listing.CheckboxLabeled(
            "Contagion_ShowPerformanceStats".Translate().Resolve(),
            ref settings.showPerformanceStats,
            "Contagion_ShowPerformanceStatsTooltip".Translate().Resolve());

        if (settings.DiagnosticsEnabled)
        {
            listing.Gap(6f);
            listing.Label("Contagion_DiagnosticsRuntimeHeader".Translate());
            listing.SubLabel(ContagionDiagnostics.BuildSummaryReport(), 1f);

            if (settings.showPerformanceStats)
            {
                listing.Gap(4f);
                listing.Label("Contagion_DiagnosticsPerformanceHeader".Translate());
                listing.SubLabel(ContagionDiagnostics.BuildPerformanceReport(), 1f);
            }

            listing.Gap(6f);

            if (listing.ButtonText("Contagion_ClearDiagnostics".Translate()))
            {
                ContagionDiagnostics.Reset();
            }

            if (settings.diagnosticsMode == ContagionDiagnosticsMode.Verbose && !Prefs.DevMode)
            {
                listing.SubLabel("Contagion_DiagnosticsVerboseDevMode".Translate().Resolve(), 1f);
            }
        }

        listing.Gap(12f);

        if (listing.ButtonText("Contagion_ResetDefaults".Translate()))
        {
            settings.Reset();
        }

        listing.End();
    }
}