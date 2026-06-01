using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

public sealed class Contagion_MapTransmissionComponent : MapComponent
{
    private const int TransmissionCheckInterval = 250;

    private const int EnvironmentalCheckInterval = 2500;

    private const int DirectorUpdateInterval = 60000;

    private readonly ContagionMapDeveloperDiagnosticsController _developerDiagnosticsController;

    private readonly ContagionPawnTransmissionProcessor _pawnTransmissionProcessor;

    private readonly ContagionEnvironmentalExposureProcessor _environmentalExposureProcessor;

    private readonly ContagionCorpseExposureProcessor _corpseExposureProcessor;

    private ContagionVomitFomiteTracker _vomitFomiteTracker = new();

    private ContagionMapSeedingState _seedingState = new();

    // Per-disease outbreak tracking (human track and animal track are separate so an animal
    // case never suppresses the human first-case letter and vice versa).
    // lastCaseTick: tick of the most recent disease activation for an active outbreak.
    // clusterLetter: the current undismissed yellow cluster letter (null = no active letter).
    // An outbreak is considered over when TicksGame - lastCaseTick > profile.OutbreakEndTicks.
    private Dictionary<HediffDef, int> _humanOutbreakLastCaseTick = new();

    private Dictionary<HediffDef, Letter> _humanOutbreakClusterLetter = new();

    private Dictionary<HediffDef, int> _animalOutbreakLastCaseTick = new();

    private Dictionary<HediffDef, Letter> _animalOutbreakClusterLetter = new();

    public Contagion_MapTransmissionComponent(Map map)
        : base(map)
    {
        _developerDiagnosticsController = new ContagionMapDeveloperDiagnosticsController(map);
        _pawnTransmissionProcessor = new ContagionPawnTransmissionProcessor(map, _developerDiagnosticsController);
        _environmentalExposureProcessor = new ContagionEnvironmentalExposureProcessor(this);
        _corpseExposureProcessor = new ContagionCorpseExposureProcessor(map);
    }

    public Map Map => map;

    public IReadOnlyList<PendingDiseaseEvent> PendingEvents => _seedingState.PendingEvents;

    public ContagionDiseaseDirector DiseaseDirector => _seedingState.DiseaseDirector;

    internal ContagionMapDeveloperDiagnosticsController DeveloperDiagnostics => _developerDiagnosticsController;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Deep.Look(ref _vomitFomiteTracker, "vomitFomiteTracker");
        Scribe_Deep.Look(ref _seedingState, "seedingState");
        Scribe_Collections.Look(ref _humanOutbreakLastCaseTick, "humanOutbreakLastCaseTick", LookMode.Def, LookMode.Value);
        Scribe_Collections.Look(ref _animalOutbreakLastCaseTick, "animalOutbreakLastCaseTick", LookMode.Def, LookMode.Value);

        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            _vomitFomiteTracker ??= new ContagionVomitFomiteTracker();
            _seedingState ??= new ContagionMapSeedingState();
            _humanOutbreakLastCaseTick ??= new Dictionary<HediffDef, int>();
            _humanOutbreakClusterLetter ??= new Dictionary<HediffDef, Letter>();
            _animalOutbreakLastCaseTick ??= new Dictionary<HediffDef, int>();
            _animalOutbreakClusterLetter ??= new Dictionary<HediffDef, Letter>();
            _vomitFomiteTracker.Cleanup(map);
        }
    }

    public bool IsAtActiveCaseLimit(ResolvedTransmissionProfile resolvedProfile, TransmissionSeeder seeder)
    {
        return _seedingState.IsAtActiveCaseLimit(map, resolvedProfile, seeder);
    }

    public bool CanRunSeeder(ResolvedTransmissionProfile resolvedProfile, TransmissionSeeder seeder)
    {
        return _seedingState.CanRunSeeder(map, resolvedProfile, seeder);
    }

    public PendingDiseaseEvent GetPendingEvent(HediffDef diseaseDef)
    {
        return _seedingState.GetPendingEvent(diseaseDef);
    }

    public void AddPendingEvent(PendingDiseaseEvent pendingEvent)
    {
        _seedingState.AddPendingEvent(pendingEvent);
    }

    public void RemovePendingEvent(PendingDiseaseEvent pendingEvent)
    {
        _seedingState.RemovePendingEvent(pendingEvent);
    }

    public void NotifySeederFired(ResolvedTransmissionProfile resolvedProfile, TransmissionSeeder seeder)
    {
        _seedingState.NotifySeederFired(resolvedProfile, seeder);
    }

    public override void MapComponentUpdate()
    {
        base.MapComponentUpdate();
        _developerDiagnosticsController.Update();
    }

    public override void MapComponentTick()
    {
        base.MapComponentTick();

        int ticksGame = Find.TickManager.TicksGame;
        bool runTransmission = ticksGame % TransmissionCheckInterval == 0;
        bool runEnvironmental = ticksGame % EnvironmentalCheckInterval == 0;
        bool runDirector = ContagionSeedingCoordinator.CurrentMode == ContagionSeedingMode.Contagion
            && ticksGame % DirectorUpdateInterval == 0;
        if (!runTransmission && !runEnvironmental && !runDirector)
        {
            return;
        }

        IReadOnlyList<Pawn> spawnedPawns = map?.mapPawns?.AllPawnsSpawned;
        if (spawnedPawns == null || spawnedPawns.Count == 0)
        {
            return;
        }

        if (runDirector)
        {
            _seedingState.DailyTick(map);
        }

        if (!runTransmission && !runEnvironmental)
        {
            return;
        }

        _vomitFomiteTracker.Cleanup(map);

        if (runEnvironmental)
        {
            RunGeneralSeederPass(spawnedPawns);
            _environmentalExposureProcessor.RunEnvironmentalExposurePass(spawnedPawns);
            ContagionSeedingCoordinator.RunSpontaneousFalsePositives(spawnedPawns, EnvironmentalCheckInterval);
            PruneStaleOutbreaks();
        }

        if (!runTransmission)
        {
            return;
        }

        _vomitFomiteTracker.RunFomiteExposurePass(spawnedPawns, map);
        _corpseExposureProcessor.RunCorpseExposurePass(spawnedPawns, TransmissionCheckInterval);

        if (spawnedPawns.Count >= 2)
        {
            _pawnTransmissionProcessor.RunPawnTransmissionPass(spawnedPawns);
        }
    }

    public void NotifyVomitFilthCreated(Filth filth, Pawn sourcePawn)
    {
        _vomitFomiteTracker.NotifyVomitFilthCreated(filth, sourcePawn, map);
    }

    // ── Outbreak tracking ──────────────────────────────────────────────────────────────────────
    // Human and animal tracks are separate: an animal case never suppresses a human first-case
    // letter. ContagionDiseaseNotifier picks the right track based on pawn.RaceProps.Animal.

    public bool IsHumanOutbreakActive(ResolvedTransmissionProfile resolvedProfile)
        => IsOutbreakActiveIn(_humanOutbreakLastCaseTick, resolvedProfile);

    public bool IsAnimalOutbreakActive(ResolvedTransmissionProfile resolvedProfile)
        => IsOutbreakActiveIn(_animalOutbreakLastCaseTick, resolvedProfile);

    public void RecordHumanOutbreakCase(ResolvedTransmissionProfile resolvedProfile)
        => RecordOutbreakCaseIn(_humanOutbreakLastCaseTick, resolvedProfile);

    public void RecordAnimalOutbreakCase(ResolvedTransmissionProfile resolvedProfile)
        => RecordOutbreakCaseIn(_animalOutbreakLastCaseTick, resolvedProfile);

    public Letter GetHumanClusterLetter(ResolvedTransmissionProfile resolvedProfile)
        => GetClusterLetterFrom(_humanOutbreakClusterLetter, resolvedProfile);

    public Letter GetAnimalClusterLetter(ResolvedTransmissionProfile resolvedProfile)
        => GetClusterLetterFrom(_animalOutbreakClusterLetter, resolvedProfile);

    public void SetHumanClusterLetter(ResolvedTransmissionProfile resolvedProfile, Letter letter)
        => SetClusterLetterIn(_humanOutbreakClusterLetter, resolvedProfile, letter);

    public void SetAnimalClusterLetter(ResolvedTransmissionProfile resolvedProfile, Letter letter)
        => SetClusterLetterIn(_animalOutbreakClusterLetter, resolvedProfile, letter);

    private static bool IsOutbreakActiveIn(Dictionary<HediffDef, int> dict, ResolvedTransmissionProfile resolvedProfile)
    {
        if (resolvedProfile?.Profile == null)
        {
            return false;
        }

        if (!dict.TryGetValue(resolvedProfile.DiseaseDef, out int lastTick))
        {
            return false;
        }

        return Find.TickManager.TicksGame - lastTick <= resolvedProfile.Profile.OutbreakEndTicks;
    }

    private static void RecordOutbreakCaseIn(Dictionary<HediffDef, int> dict, ResolvedTransmissionProfile resolvedProfile)
    {
        if (resolvedProfile?.DiseaseDef != null)
        {
            dict[resolvedProfile.DiseaseDef] = Find.TickManager.TicksGame;
        }
    }

    private static Letter GetClusterLetterFrom(Dictionary<HediffDef, Letter> dict, ResolvedTransmissionProfile resolvedProfile)
    {
        if (resolvedProfile?.DiseaseDef == null)
        {
            return null;
        }

        dict.TryGetValue(resolvedProfile.DiseaseDef, out Letter letter);
        return letter;
    }

    private static void SetClusterLetterIn(Dictionary<HediffDef, Letter> dict, ResolvedTransmissionProfile resolvedProfile, Letter letter)
    {
        if (resolvedProfile?.DiseaseDef == null)
        {
            return;
        }

        if (letter == null)
        {
            dict.Remove(resolvedProfile.DiseaseDef);
        }
        else
        {
            dict[resolvedProfile.DiseaseDef] = letter;
        }
    }

    private void RunGeneralSeederPass(IReadOnlyList<Pawn> spawnedPawns)
    {
        ContagionSeedingCoordinator.RunGeneralSeeding(this, spawnedPawns);
    }

    // Removes stale entries from both outbreak tracking dictionaries so they don't accumulate
    // across a long save. Called every EnvironmentalCheckInterval ticks.
    private void PruneStaleOutbreaks()
    {
        PruneStaleEntriesFrom(_humanOutbreakLastCaseTick, _humanOutbreakClusterLetter);
        PruneStaleEntriesFrom(_animalOutbreakLastCaseTick, _animalOutbreakClusterLetter);
    }

    private static void PruneStaleEntriesFrom(Dictionary<HediffDef, int> tickDict, Dictionary<HediffDef, Letter> letterDict)
    {
        if (tickDict.Count == 0)
        {
            return;
        }

        int ticksNow = Find.TickManager.TicksGame;
        List<HediffDef> toRemove = null;

        foreach (KeyValuePair<HediffDef, int> kv in tickDict)
        {
            if (!DiseaseProfileCache.TryGetResolvedProfile(kv.Key, out ResolvedTransmissionProfile resolvedProfile))
            {
                (toRemove ??= new List<HediffDef>()).Add(kv.Key);
                continue;
            }

            if (ticksNow - kv.Value > resolvedProfile.Profile.OutbreakEndTicks)
            {
                (toRemove ??= new List<HediffDef>()).Add(kv.Key);
            }
        }

        if (toRemove != null)
        {
            foreach (HediffDef def in toRemove)
            {
                tickDict.Remove(def);
                letterDict.Remove(def);
            }
        }
    }
}
