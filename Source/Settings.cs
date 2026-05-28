using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Contagion;

public enum ContagionDiagnosticsMode
{
    Off,
    Summary,
    Verbose,
    Developer
}

// Order is persisted by Scribe as ordinal values. Never reorder; append new values only.
public enum ContagionDifficulty
{
    Easier,
    Normal,
    Harder
}

// Order is persisted by Scribe as ordinal values. Never reorder; append new values only.
public enum ContagionSeedingMode
{
    Storyteller,
    Contagion
}

public sealed class Contagion_Settings : ModSettings
{
    public const float MinMultiplier = 0.25f;

    public const float MaxMultiplier = 2f;

    private const float DefaultTransmissionRateMultiplier = 1f;

    private const float DefaultOutbreakFrequencyMultiplier = 1f;

    private const float DefaultIncubationLengthMultiplier = 1f;

    private const ContagionDifficulty DefaultDifficulty = ContagionDifficulty.Normal;

    private const ContagionSeedingMode DefaultSeedingMode = ContagionSeedingMode.Storyteller;

    private const bool DefaultMaskProtection = true;

    private const ContagionDiagnosticsMode DefaultDiagnosticsMode = ContagionDiagnosticsMode.Off;

    private const bool DefaultShowPerformanceStats = false;

    public float transmissionRateMultiplier = DefaultTransmissionRateMultiplier;

    public float outbreakFrequencyMultiplier = DefaultOutbreakFrequencyMultiplier;

    public float incubationLengthMultiplier = DefaultIncubationLengthMultiplier;

    public ContagionDifficulty difficulty = DefaultDifficulty;

    public ContagionSeedingMode seedingMode = DefaultSeedingMode;

    public bool maskProtection = DefaultMaskProtection;

    public ContagionDiagnosticsMode diagnosticsMode = DefaultDiagnosticsMode;

    public bool showPerformanceStats = DefaultShowPerformanceStats;

    public bool DiagnosticsEnabled => diagnosticsMode != ContagionDiagnosticsMode.Off;

    public bool VerboseDiagnosticsEnabled => diagnosticsMode == ContagionDiagnosticsMode.Verbose && Prefs.DevMode;

    public bool DeveloperDiagnosticsEnabled => diagnosticsMode == ContagionDiagnosticsMode.Developer && Prefs.DevMode;

    // Difficulty scales person-to-person transmission on top of the user slider.
    public float DifficultyTransmissionScale => difficulty switch
    {
        ContagionDifficulty.Easier => 0.7f,
        ContagionDifficulty.Harder => 1.35f,
        _ => 1f
    };

    // Suppression exponent: chance is multiplied by (1 - infectedColonyFraction)^strength.
    // Easier slows spread hard as the colony fills up; Harder disables suppression entirely.
    public float SpreadSuppressionStrength => difficulty switch
    {
        ContagionDifficulty.Easier => 3.5f,
        ContagionDifficulty.Harder => 0f,
        _ => 2f
    };

    // Effective multiplier applied at every person-to-person transmission roll.
    public float EffectiveTransmissionMultiplier => transmissionRateMultiplier * DifficultyTransmissionScale;

    public void Reset()
    {
        transmissionRateMultiplier = DefaultTransmissionRateMultiplier;
        outbreakFrequencyMultiplier = DefaultOutbreakFrequencyMultiplier;
        incubationLengthMultiplier = DefaultIncubationLengthMultiplier;
        difficulty = DefaultDifficulty;
        seedingMode = DefaultSeedingMode;
        maskProtection = DefaultMaskProtection;
        diagnosticsMode = DefaultDiagnosticsMode;
        showPerformanceStats = DefaultShowPerformanceStats;
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref transmissionRateMultiplier, "transmissionRateMultiplier", DefaultTransmissionRateMultiplier);
        Scribe_Values.Look(ref outbreakFrequencyMultiplier, "outbreakFrequencyMultiplier", DefaultOutbreakFrequencyMultiplier);
        Scribe_Values.Look(ref incubationLengthMultiplier, "incubationLengthMultiplier", DefaultIncubationLengthMultiplier);
        Scribe_Values.Look(ref difficulty, "difficulty", DefaultDifficulty);
        Scribe_Values.Look(ref seedingMode, "seedingMode", DefaultSeedingMode);
        Scribe_Values.Look(ref maskProtection, "maskProtection", DefaultMaskProtection);
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

        listing.Label("Contagion_SettingDifficulty".Translate());
        if (listing.RadioButton(
            "Contagion_DifficultyEasier".Translate().Resolve(),
            settings.difficulty == ContagionDifficulty.Easier,
            tooltip: "Contagion_DifficultyEasierTooltip".Translate().Resolve()))
        {
            settings.difficulty = ContagionDifficulty.Easier;
        }

        if (listing.RadioButton(
            "Contagion_DifficultyNormal".Translate().Resolve(),
            settings.difficulty == ContagionDifficulty.Normal,
            tooltip: "Contagion_DifficultyNormalTooltip".Translate().Resolve()))
        {
            settings.difficulty = ContagionDifficulty.Normal;
        }

        if (listing.RadioButton(
            "Contagion_DifficultyHarder".Translate().Resolve(),
            settings.difficulty == ContagionDifficulty.Harder,
            tooltip: "Contagion_DifficultyHarderTooltip".Translate().Resolve()))
        {
            settings.difficulty = ContagionDifficulty.Harder;
        }

        listing.Gap(6f);
        listing.Label("Contagion_SettingSeedingMode".Translate());
        if (listing.RadioButton(
            "Contagion_SeedingModeStoryteller".Translate().Resolve(),
            settings.seedingMode == ContagionSeedingMode.Storyteller,
            tooltip: "Contagion_SeedingModeStorytellerTooltip".Translate().Resolve()))
        {
            settings.seedingMode = ContagionSeedingMode.Storyteller;
        }

        if (listing.RadioButton(
            "Contagion_SeedingModeContagion".Translate().Resolve(),
            settings.seedingMode == ContagionSeedingMode.Contagion,
            tooltip: "Contagion_SeedingModeContagionTooltip".Translate().Resolve()))
        {
            settings.seedingMode = ContagionSeedingMode.Contagion;
        }

        listing.Gap(6f);
        listing.CheckboxLabeled(
            "Contagion_SettingMaskProtection".Translate().Resolve(),
            ref settings.maskProtection,
            "Contagion_SettingMaskProtectionTooltip".Translate().Resolve());

        listing.Gap(12f);
        listing.Label("Contagion_SettingsTuningHeader".Translate());
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

        if (listing.RadioButton(
            "Contagion_DiagnosticsModeDeveloper".Translate().Resolve(),
            settings.diagnosticsMode == ContagionDiagnosticsMode.Developer,
            tooltip: "Contagion_DiagnosticsModeDeveloperTooltip".Translate().Resolve()))
        {
            settings.diagnosticsMode = ContagionDiagnosticsMode.Developer;
        }

        listing.CheckboxLabeled(
            "Contagion_ShowPerformanceStats".Translate().Resolve(),
            ref settings.showPerformanceStats,
            "Contagion_ShowPerformanceStatsTooltip".Translate().Resolve());

        if (settings.DiagnosticsEnabled)
        {
            listing.Gap(6f);
            listing.Label("Contagion_DiagnosticsRuntimeHeader".Translate());
            listing.Label("Contagion_DiagnosticsIncidenceHeader".Translate());
            listing.SubLabel(ContagionDiagnostics.BuildIncidenceReport(), 1f);
            listing.Gap(4f);
            listing.Label("Contagion_DiagnosticsSpreadHeader".Translate());
            listing.SubLabel(ContagionDiagnostics.BuildSpreadReport(), 1f);

            if (settings.showPerformanceStats)
            {
                listing.Gap(4f);
                listing.Label("Contagion_DiagnosticsPerformanceHeader".Translate());
                listing.SubLabel(ContagionDiagnostics.BuildPerformanceReport(), 1f);
            }

            if (settings.diagnosticsMode == ContagionDiagnosticsMode.Developer)
            {
                listing.Gap(6f);
                DrawDeveloperDiagnosticsControls(listing);
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

            if (settings.diagnosticsMode == ContagionDiagnosticsMode.Developer && !Prefs.DevMode)
            {
                listing.SubLabel("Contagion_DiagnosticsDeveloperDevMode".Translate().Resolve(), 1f);
            }
        }

        listing.Gap(12f);

        if (listing.ButtonText("Contagion_ResetDefaults".Translate()))
        {
            settings.Reset();
        }

        listing.End();
    }

    private static void DrawDeveloperDiagnosticsControls(Listing_Standard listing)
    {
        listing.Label("Contagion_DeveloperDiagnosticsHeader".Translate());
        if (!Prefs.DevMode)
        {
            listing.SubLabel("Contagion_DiagnosticsDeveloperDevMode".Translate().Resolve(), 1f);
            return;
        }

        Map currentMap = Find.CurrentMap;
        if (currentMap == null)
        {
            listing.SubLabel("Contagion_DeveloperDiagnosticsNoMap".Translate().Resolve(), 1f);
            return;
        }

        Contagion_MapTransmissionComponent component = currentMap.GetComponent<Contagion_MapTransmissionComponent>();
        if (component == null)
        {
            listing.SubLabel("Contagion_DeveloperDiagnosticsUnavailable".Translate().Resolve(), 1f);
            return;
        }

        listing.Label("Contagion_DeveloperForceArrivalHeader".Translate());
        if (component.DeveloperForcedArrivalDisease != null)
        {
            listing.SubLabel(
                "Contagion_DeveloperForcedArrivalSummary".Translate(component.DeveloperForcedArrivalDisease.LabelCap).Resolve(),
                1f);
        }
        else
        {
            listing.SubLabel("Contagion_DeveloperForcedArrivalNone".Translate().Resolve(), 1f);
        }

        if (Settings.seedingMode != ContagionSeedingMode.Contagion)
        {
            listing.SubLabel("Contagion_DeveloperForceArrivalRequiresContagion".Translate().Resolve(), 1f);
        }
        else
        {
            List<ResolvedTransmissionProfile> arrivalProfiles = ContagionSeedingCoordinator.GetDeveloperArrivalProfiles();
            if (arrivalProfiles.Count == 0)
            {
                listing.SubLabel("Contagion_DeveloperForceArrivalNoDiseases".Translate().Resolve(), 1f);
            }
            else if (listing.ButtonText("Contagion_DeveloperForceArrivalDisease".Translate()))
            {
                ShowDeveloperForcedArrivalMenu(component, arrivalProfiles);
            }
        }

        if (component.DeveloperForcedArrivalDisease != null
            && listing.ButtonText("Contagion_DeveloperClearForcedArrival".Translate()))
        {
            component.DeveloperClearForcedArrival();
        }

        listing.Gap(4f);
        listing.Label("Contagion_DeveloperTraceHeader".Translate());
        listing.SubLabel(
            "Contagion_DeveloperTraceCount".Translate(component.DeveloperTransmissionTraces.Count).Resolve(),
            1f);
        if (component.DeveloperTransmissionTraces.Count > 0
            && listing.ButtonText("Contagion_DeveloperClearAllTraces".Translate()))
        {
            component.DeveloperClearAllTraces();
        }
    }

    private static void ShowDeveloperForcedArrivalMenu(
        Contagion_MapTransmissionComponent component,
        List<ResolvedTransmissionProfile> arrivalProfiles)
    {
        if (component == null || arrivalProfiles == null || arrivalProfiles.Count == 0)
        {
            return;
        }

        List<FloatMenuOption> options = new List<FloatMenuOption>();
        for (int i = 0; i < arrivalProfiles.Count; i++)
        {
            ResolvedTransmissionProfile resolvedProfile = arrivalProfiles[i];
            if (resolvedProfile?.DiseaseDef == null)
            {
                continue;
            }

            options.Add(new FloatMenuOption(
                resolvedProfile.DiseaseDef.LabelCap.Resolve(),
                delegate
                {
                    component.DeveloperArmForcedArrival(resolvedProfile.DiseaseDef);
                }));
        }

        if (options.Count > 0)
        {
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }
}
