using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Contagion;

// Persisted via Scribe_Values, which serializes enums by name (ToString/Parse) — reordering is
// safe, but never rename or remove a member (that breaks old saves). Append new values freely.
public enum ContagionDifficulty
{
    Easy,
    Medium,
    Hard
}

// Persisted via Scribe_Values, which serializes enums by name (ToString/Parse) — reordering is
// safe, but never rename or remove a member (that breaks old saves). Append new values freely.
public enum ContagionSuppressionMode
{
    Strong,
    Medium,
    Weak,
    LetErRip
}

// Persisted via Scribe_Values, which serializes enums by name (ToString/Parse) — reordering is
// safe, but never rename or remove a member (that breaks old saves). Append new values freely.
public enum ContagionSeedingMode
{
    Storyteller,
    Contagion
}

public sealed class Contagion_Settings : ModSettings
{
    private const ContagionDifficulty DefaultDifficulty = ContagionDifficulty.Medium;

    private const ContagionSuppressionMode DefaultSuppressionMode = ContagionSuppressionMode.Medium;

    private const ContagionSeedingMode DefaultSeedingMode = ContagionSeedingMode.Storyteller;

    private const bool DefaultMaskProtection = true;

    private const bool DefaultEnableLogging = false;

    private const bool DefaultDeveloperMode = false;

    private const bool DefaultSuppressLowProbabilityLogs = true;

    private const bool DefaultSuppressAnimalClusterNotifications = true;

    public ContagionDifficulty difficulty = DefaultDifficulty;

    public ContagionSuppressionMode suppressionMode = DefaultSuppressionMode;

    public ContagionSeedingMode seedingMode = DefaultSeedingMode;

    public bool maskProtection = DefaultMaskProtection;

    public bool enableLogging = DefaultEnableLogging;

    public bool developerMode = DefaultDeveloperMode;

    public bool suppressLowProbabilityLogs = DefaultSuppressLowProbabilityLogs;

    public bool suppressAnimalClusterNotifications = DefaultSuppressAnimalClusterNotifications;

    public bool LoggingEnabled => enableLogging;

    // The mod's developer mode is a self-contained toggle, deliberately independent of
    // RimWorld's Prefs.DevMode: enabling it turns on every Contagion dev feature (overlays,
    // tracing, infected indicators, seed/force-arrival controls) without further gating.
    public bool DeveloperDiagnosticsEnabled => developerMode;

    public float EffectiveTransmissionMultiplier => difficulty switch
    {
        ContagionDifficulty.Easy => 0.7f,
        ContagionDifficulty.Hard => 1.35f,
        _ => 1f
    };

    public void Reset()
    {
        difficulty = DefaultDifficulty;
        suppressionMode = DefaultSuppressionMode;
        seedingMode = DefaultSeedingMode;
        maskProtection = DefaultMaskProtection;
        enableLogging = DefaultEnableLogging;
        developerMode = DefaultDeveloperMode;
        suppressLowProbabilityLogs = DefaultSuppressLowProbabilityLogs;
        suppressAnimalClusterNotifications = DefaultSuppressAnimalClusterNotifications;
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref difficulty, "difficulty", DefaultDifficulty);
        Scribe_Values.Look(ref suppressionMode, "suppressionMode", DefaultSuppressionMode);
        Scribe_Values.Look(ref seedingMode, "seedingMode", DefaultSeedingMode);
        Scribe_Values.Look(ref maskProtection, "maskProtection", DefaultMaskProtection);
        Scribe_Values.Look(ref enableLogging, "enableLogging", DefaultEnableLogging);
        Scribe_Values.Look(ref developerMode, "developerMode", DefaultDeveloperMode);
        Scribe_Values.Look(ref suppressLowProbabilityLogs, "suppressLowProbabilityLogs", DefaultSuppressLowProbabilityLogs);
        Scribe_Values.Look(ref suppressAnimalClusterNotifications, "suppressAnimalClusterNotifications", DefaultSuppressAnimalClusterNotifications);
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
            "Contagion_DifficultyEasy".Translate().Resolve(),
            settings.difficulty == ContagionDifficulty.Easy,
            tooltip: "Contagion_DifficultyEasyTooltip".Translate().Resolve()))
        {
            settings.difficulty = ContagionDifficulty.Easy;
        }

        if (listing.RadioButton(
            "Contagion_DifficultyMedium".Translate().Resolve(),
            settings.difficulty == ContagionDifficulty.Medium,
            tooltip: "Contagion_DifficultyMediumTooltip".Translate().Resolve()))
        {
            settings.difficulty = ContagionDifficulty.Medium;
        }

        if (listing.RadioButton(
            "Contagion_DifficultyHard".Translate().Resolve(),
            settings.difficulty == ContagionDifficulty.Hard,
            tooltip: "Contagion_DifficultyHardTooltip".Translate().Resolve()))
        {
            settings.difficulty = ContagionDifficulty.Hard;
        }

        listing.Gap(6f);
        listing.Label("Contagion_SettingSuppressionMode".Translate());
        if (listing.RadioButton(
            "Contagion_SuppressionStrong".Translate().Resolve(),
            settings.suppressionMode == ContagionSuppressionMode.Strong,
            tooltip: "Contagion_SuppressionStrongTooltip".Translate().Resolve()))
        {
            settings.suppressionMode = ContagionSuppressionMode.Strong;
        }

        if (listing.RadioButton(
            "Contagion_SuppressionMedium".Translate().Resolve(),
            settings.suppressionMode == ContagionSuppressionMode.Medium,
            tooltip: "Contagion_SuppressionMediumTooltip".Translate().Resolve()))
        {
            settings.suppressionMode = ContagionSuppressionMode.Medium;
        }

        if (listing.RadioButton(
            "Contagion_SuppressionWeak".Translate().Resolve(),
            settings.suppressionMode == ContagionSuppressionMode.Weak,
            tooltip: "Contagion_SuppressionWeakTooltip".Translate().Resolve()))
        {
            settings.suppressionMode = ContagionSuppressionMode.Weak;
        }

        if (listing.RadioButton(
            "Contagion_SuppressionLetErRip".Translate().Resolve(),
            settings.suppressionMode == ContagionSuppressionMode.LetErRip,
            tooltip: "Contagion_SuppressionLetErRipTooltip".Translate().Resolve()))
        {
            settings.suppressionMode = ContagionSuppressionMode.LetErRip;
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

        listing.CheckboxLabeled(
            "Contagion_SettingSuppressAnimalClusterNotifications".Translate().Resolve(),
            ref settings.suppressAnimalClusterNotifications,
            "Contagion_SettingSuppressAnimalClusterNotificationsTooltip".Translate().Resolve());

        listing.Gap(12f);
        listing.Label("Contagion_SettingsDiagnosticsHeader".Translate());
        listing.Gap();

        listing.CheckboxLabeled(
            "Contagion_SettingEnableLogging".Translate().Resolve(),
            ref settings.enableLogging,
            "Contagion_SettingEnableLoggingTooltip".Translate().Resolve());

        if (settings.enableLogging)
        {
            listing.CheckboxLabeled(
                "Contagion_SettingSuppressLowProbabilityLogs".Translate().Resolve(),
                ref settings.suppressLowProbabilityLogs,
                "Contagion_SettingSuppressLowProbabilityLogsTooltip".Translate().Resolve());
        }

        listing.Gap(6f);
        listing.CheckboxLabeled(
            "Contagion_SettingDeveloperMode".Translate().Resolve(),
            ref settings.developerMode,
            "Contagion_SettingDeveloperModeTooltip".Translate().Resolve());

        if (settings.developerMode)
        {
            listing.Gap(6f);
            listing.Label("Contagion_DiagnosticsRuntimeHeader".Translate());
            listing.Label("Contagion_DiagnosticsIncidenceHeader".Translate());
            listing.SubLabel(ContagionDiagnostics.BuildIncidenceReport(), 1f);
            listing.Gap(4f);
            listing.Label("Contagion_DiagnosticsSpreadHeader".Translate());
            listing.SubLabel(ContagionDiagnostics.BuildSpreadReport(), 1f);

            listing.Gap(6f);
            DrawDeveloperDiagnosticsControls(listing);

            listing.Gap(6f);

            if (listing.ButtonText("Contagion_ClearDiagnostics".Translate()))
            {
                ContagionDiagnostics.Reset();
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
        if (component.DeveloperDiagnostics.ForcedArrivalDisease != null)
        {
            listing.SubLabel(
                "Contagion_DeveloperForcedArrivalSummary".Translate(component.DeveloperDiagnostics.ForcedArrivalDisease.LabelCap).Resolve(),
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

        if (component.DeveloperDiagnostics.ForcedArrivalDisease != null
            && listing.ButtonText("Contagion_DeveloperClearForcedArrival".Translate()))
        {
            component.DeveloperDiagnostics.ClearForcedArrival();
        }

        listing.Gap(4f);
        listing.Label("Contagion_DeveloperTraceHeader".Translate());
        listing.SubLabel(
            "Contagion_DeveloperTraceCount".Translate(component.DeveloperDiagnostics.TraceElementCount).Resolve(),
            1f);
        if (component.DeveloperDiagnostics.TraceElementCount > 0
            && listing.ButtonText("Contagion_DeveloperClearAllTraces".Translate()))
        {
            component.DeveloperDiagnostics.ClearAllTraces();
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
                    component.DeveloperDiagnostics.ArmForcedArrival(resolvedProfile.DiseaseDef);
                }));
        }

        if (options.Count > 0)
        {
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }
}
