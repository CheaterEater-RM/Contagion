using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

public sealed class Contagion_MapTransmissionComponent : MapComponent
{
    // Cadence between live pawn-to-pawn / corpse transmission passes. Sourced from the global
    // ContagionTransmissionTuningDef (XML-tunable) and cached for the session — the value is constant
    // for a run, so we avoid a DefDatabase lookup on every tick's interval gate.
    private int _transmissionCheckInterval;

    private int TransmissionCheckInterval =>
        _transmissionCheckInterval != 0 ? _transmissionCheckInterval : (_transmissionCheckInterval = ContagionTransmissionTuningDef.CheckIntervalTicks);

    private const int EnvironmentalCheckInterval = 2500;

    private const int DirectorUpdateInterval = 60000;

    private readonly ContagionMapDeveloperDiagnosticsController _developerDiagnosticsController;

    private readonly ContagionPawnTransmissionProcessor _pawnTransmissionProcessor;

    private readonly ContagionEnvironmentalExposureProcessor _environmentalExposureProcessor;

    private readonly ContagionCorpseExposureProcessor _corpseExposureProcessor;

    private readonly List<Pawn> _transmissionDuePawns = new List<Pawn>();

    private ContagionVomitFomiteTracker _vomitFomiteTracker = new();

    private ContagionFecalOralTracker _fecalOralTracker = new();

    private ContagionMapSeedingState _seedingState = new();

    // Per-disease outbreak tracking (human track and animal track are separate so an animal
    // case never suppresses the human first-case letter and vice versa).
    // lastCaseTick: tick of the most recent disease activation for an active outbreak.
    // clusterLetterId: Letter.ID of the current undismissed cluster letter (absent = no active
    // letter). We persist the ID, not the Letter reference: a dismissed-and-culled letter would
    // throw an unresolved-reference error on load, whereas an ID simply resolves to nothing.
    // An outbreak is considered over when TicksGame - lastCaseTick > profile.OutbreakEndTicks.
    private Dictionary<HediffDef, int> _humanOutbreakLastCaseTick = new();

    private Dictionary<HediffDef, int> _humanOutbreakClusterLetterId = new();

    private Dictionary<HediffDef, int> _animalOutbreakLastCaseTick = new();

    private Dictionary<HediffDef, int> _animalOutbreakClusterLetterId = new();

    // Outbreak origin: the seed source that began the current wave, tracked once per disease and
    // deliberately NOT split by species. Diseases that cross between humans and animals (e.g. Plague
    // arriving on a caravan or a wild animal) have one true origin regardless of which species shows
    // symptoms first, so both the human and animal first-case letters read from the same record.
    // originTick is the tick of the first seed, used to expire a stale origin once the wave is over.
    private Dictionary<HediffDef, ContagionSeedSource> _outbreakOriginSource = new();

    private Dictionary<HediffDef, int> _outbreakOriginTick = new();

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
        Scribe_Deep.Look(ref _fecalOralTracker, "fecalOralTracker");
        Scribe_Deep.Look(ref _seedingState, "seedingState");
        Scribe_Collections.Look(ref _humanOutbreakLastCaseTick, "humanOutbreakLastCaseTick", LookMode.Def, LookMode.Value);
        Scribe_Collections.Look(ref _animalOutbreakLastCaseTick, "animalOutbreakLastCaseTick", LookMode.Def, LookMode.Value);
        Scribe_Collections.Look(ref _humanOutbreakClusterLetterId, "humanOutbreakClusterLetterId", LookMode.Def, LookMode.Value);
        Scribe_Collections.Look(ref _animalOutbreakClusterLetterId, "animalOutbreakClusterLetterId", LookMode.Def, LookMode.Value);
        Scribe_Collections.Look(ref _outbreakOriginSource, "outbreakOriginSource", LookMode.Def, LookMode.Value);
        Scribe_Collections.Look(ref _outbreakOriginTick, "outbreakOriginTick", LookMode.Def, LookMode.Value);

        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            _vomitFomiteTracker ??= new ContagionVomitFomiteTracker();
            _fecalOralTracker ??= new ContagionFecalOralTracker();
            _seedingState ??= new ContagionMapSeedingState();
            _humanOutbreakLastCaseTick ??= new Dictionary<HediffDef, int>();
            _humanOutbreakClusterLetterId ??= new Dictionary<HediffDef, int>();
            _animalOutbreakLastCaseTick ??= new Dictionary<HediffDef, int>();
            _animalOutbreakClusterLetterId ??= new Dictionary<HediffDef, int>();
            _outbreakOriginSource ??= new Dictionary<HediffDef, ContagionSeedSource>();
            _outbreakOriginTick ??= new Dictionary<HediffDef, int>();
            _vomitFomiteTracker.Cleanup(map);
            _fecalOralTracker.Cleanup(map);
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
        bool runEnvironmental = ticksGame % EnvironmentalCheckInterval == 0;
        bool runDirector = ContagionSeedingCoordinator.CurrentMode == ContagionSeedingMode.Contagion
            && ticksGame % DirectorUpdateInterval == 0;

        IReadOnlyList<Pawn> spawnedPawns = map?.mapPawns?.AllPawnsSpawned;
        if (spawnedPawns == null || spawnedPawns.Count == 0)
        {
            return;
        }

        if (runDirector)
        {
            _seedingState.DailyTick(map);
        }

        if (!runEnvironmental)
        {
            int transmissionBucket = ticksGame % TransmissionCheckInterval;
            RunStaggeredTransmissionPass(spawnedPawns, transmissionBucket);
            return;
        }

        _vomitFomiteTracker.Cleanup(map);
        _fecalOralTracker.Cleanup(map);

        if (runEnvironmental)
        {
            RunGeneralSeederPass(spawnedPawns);
            _environmentalExposureProcessor.RunEnvironmentalExposurePass(spawnedPawns);
            _fecalOralTracker.RunFecalOralEatingSheddingPass(spawnedPawns, map, EnvironmentalCheckInterval);
            ContagionSeedingCoordinator.RunSpontaneousFalsePositives(spawnedPawns, EnvironmentalCheckInterval);
            PruneStaleOutbreaks();
        }

        int bucket = ticksGame % TransmissionCheckInterval;
        RunStaggeredTransmissionPass(spawnedPawns, bucket);
    }

    private void RunStaggeredTransmissionPass(IReadOnlyList<Pawn> spawnedPawns, int bucket)
    {
        BuildDuePawnList(spawnedPawns, bucket);

        _vomitFomiteTracker.RunFomiteExposurePass(_transmissionDuePawns, map);
        _fecalOralTracker.RunFecalOralLivingExposurePass(_transmissionDuePawns, map);
        _corpseExposureProcessor.RunCorpseExposurePass(spawnedPawns, _transmissionDuePawns, TransmissionCheckInterval, bucket);

        if (spawnedPawns.Count >= 2 && _transmissionDuePawns.Count > 0)
        {
            _pawnTransmissionProcessor.RunPawnTransmissionPass(spawnedPawns, _transmissionDuePawns);
        }
    }

    private void BuildDuePawnList(IReadOnlyList<Pawn> spawnedPawns, int bucket)
    {
        _transmissionDuePawns.Clear();

        for (int i = 0; i < spawnedPawns.Count; i++)
        {
            Pawn pawn = spawnedPawns[i];
            if (ContagionTransmissionStaggerUtility.IsDueThisTick(pawn, map, TransmissionCheckInterval, bucket))
            {
                _transmissionDuePawns.Add(pawn);
            }
        }
    }

    public void NotifyVomitFilthCreated(Filth filth, Pawn sourcePawn)
    {
        _vomitFomiteTracker.NotifyVomitFilthCreated(filth, sourcePawn, map);
    }

    internal void NotifyAnimalFilthCreated(Filth filth, Pawn sourcePawn)
    {
        _fecalOralTracker.NotifyAnimalFilthCreated(filth, sourcePawn, map);
    }

    internal void NotifyAnimalIngested(Pawn ingester, ContagionIngestionContext context)
    {
        _fecalOralTracker.NotifyAnimalIngested(ingester, context);
    }

    // Dev-overlay read models for the fecal-oral eating ("if it grazed here") danger heatmap and its
    // mouseover breakdown. Pure reads — no rolling or seeding.
    internal void BuildEatingRiskOverlay(Pawn ingester, Dictionary<int, float> chanceByCell)
    {
        _fecalOralTracker.BuildEatingRiskOverlay(ingester, map, chanceByCell);
    }

    internal List<ContagionEatingRiskEntry> GetEatingRiskBreakdown(Pawn ingester, IntVec3 cell)
    {
        return _fecalOralTracker.GetEatingRiskBreakdown(ingester, cell, map);
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

    // Records the seed source that began the current outbreak wave. The first seed of a fresh wave
    // wins regardless of species. While the wave is still live — a visible outbreak active on either
    // track, or within one incubation + the outbreak window of the first seed — the recorded origin
    // is not overwritten by downstream contact seeds. This lets both the human and animal first-case
    // letters attribute a crossover wave to its true origin even when a contact-infected pawn of
    // either species shows symptoms before the still-incubating index case.
    public void RecordOutbreakOrigin(ResolvedTransmissionProfile resolvedProfile, ContagionSeedSource source)
    {
        if (resolvedProfile?.DiseaseDef == null)
        {
            return;
        }

        HediffDef key = resolvedProfile.DiseaseDef;
        int now = Find.TickManager.TicksGame;
        int pendingTicks = resolvedProfile.Profile.IncubationTicks + resolvedProfile.Profile.OutbreakEndTicks;

        bool originPending = _outbreakOriginTick.TryGetValue(key, out int originTick)
            && (IsHumanOutbreakActive(resolvedProfile)
                || IsAnimalOutbreakActive(resolvedProfile)
                || now - originTick <= pendingTicks);

        if (!originPending)
        {
            _outbreakOriginSource[key] = source;
            _outbreakOriginTick[key] = now;
        }
    }

    // Returns the recorded origin for the wave, or fallback if none is tracked (e.g. a developer-forced
    // disease added without going through TrySeedIncubation, or a save from before origin tracking).
    public ContagionSeedSource GetOutbreakOrigin(ResolvedTransmissionProfile resolvedProfile, ContagionSeedSource fallback)
    {
        if (resolvedProfile?.DiseaseDef != null
            && _outbreakOriginSource.TryGetValue(resolvedProfile.DiseaseDef, out ContagionSeedSource source))
        {
            return source;
        }

        return fallback;
    }

    public Letter GetHumanClusterLetter(ResolvedTransmissionProfile resolvedProfile)
        => GetClusterLetterFrom(_humanOutbreakClusterLetterId, resolvedProfile);

    public Letter GetAnimalClusterLetter(ResolvedTransmissionProfile resolvedProfile)
        => GetClusterLetterFrom(_animalOutbreakClusterLetterId, resolvedProfile);

    public void SetHumanClusterLetter(ResolvedTransmissionProfile resolvedProfile, Letter letter)
        => SetClusterLetterIn(_humanOutbreakClusterLetterId, resolvedProfile, letter);

    public void SetAnimalClusterLetter(ResolvedTransmissionProfile resolvedProfile, Letter letter)
        => SetClusterLetterIn(_animalOutbreakClusterLetterId, resolvedProfile, letter);

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

    // Resolves the stored cluster-letter ID against the live letter stack. Returns null if the
    // outbreak has no recorded letter or the letter has since been dismissed/culled — both of which
    // the notifier already treats as "no active cluster letter", so it falls back to a fresh letter.
    private static Letter GetClusterLetterFrom(Dictionary<HediffDef, int> dict, ResolvedTransmissionProfile resolvedProfile)
    {
        if (resolvedProfile?.DiseaseDef == null || !dict.TryGetValue(resolvedProfile.DiseaseDef, out int letterId))
        {
            return null;
        }

        List<Letter> letters = Find.LetterStack.LettersListForReading;
        for (int i = 0; i < letters.Count; i++)
        {
            if (letters[i].ID == letterId)
            {
                return letters[i];
            }
        }

        return null;
    }

    private static void SetClusterLetterIn(Dictionary<HediffDef, int> dict, ResolvedTransmissionProfile resolvedProfile, Letter letter)
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
            dict[resolvedProfile.DiseaseDef] = letter.ID;
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
        PruneStaleEntriesFrom(_humanOutbreakLastCaseTick, _humanOutbreakClusterLetterId);
        PruneStaleEntriesFrom(_animalOutbreakLastCaseTick, _animalOutbreakClusterLetterId);
        PruneStaleOrigins();
    }

    // Drops a recorded origin once its wave is definitively over: the pending window (one incubation
    // plus the outbreak window) has lapsed and no visible outbreak is active on either track. A
    // fizzled seed that never produced a visible case is cleaned up the same way. Lingering entries
    // are otherwise harmless — RecordOutbreakOrigin overwrites an expired origin on the next wave.
    private void PruneStaleOrigins()
    {
        if (_outbreakOriginTick.Count == 0)
        {
            return;
        }

        int ticksNow = Find.TickManager.TicksGame;
        List<HediffDef> toRemove = null;

        foreach (KeyValuePair<HediffDef, int> kv in _outbreakOriginTick)
        {
            if (!DiseaseProfileCache.TryGetResolvedProfile(kv.Key, out ResolvedTransmissionProfile resolvedProfile))
            {
                (toRemove ??= new List<HediffDef>()).Add(kv.Key);
                continue;
            }

            int pendingTicks = resolvedProfile.Profile.IncubationTicks + resolvedProfile.Profile.OutbreakEndTicks;
            bool waveOver = ticksNow - kv.Value > pendingTicks
                && !IsHumanOutbreakActive(resolvedProfile)
                && !IsAnimalOutbreakActive(resolvedProfile);

            if (waveOver)
            {
                (toRemove ??= new List<HediffDef>()).Add(kv.Key);
            }
        }

        if (toRemove != null)
        {
            foreach (HediffDef def in toRemove)
            {
                _outbreakOriginTick.Remove(def);
                _outbreakOriginSource.Remove(def);
            }
        }
    }

    private static void PruneStaleEntriesFrom(Dictionary<HediffDef, int> tickDict, Dictionary<HediffDef, int> letterDict)
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
