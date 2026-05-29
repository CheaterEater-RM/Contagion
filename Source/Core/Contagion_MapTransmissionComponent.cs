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

    private List<Filth> _contaminatedVomitFilth = new List<Filth>();

    private List<HediffDef> _contaminatedVomitDiseases = new List<HediffDef>();

    private List<int> _contaminatedVomitTicks = new List<int>();

    private List<HediffDef> _seederCooldownDiseases = new List<HediffDef>();

    private List<string> _seederCooldownKeys = new List<string>();

    private List<int> _seederCooldownTicks = new List<int>();

    private List<PendingDiseaseEvent> _pendingEvents = new List<PendingDiseaseEvent>();

    private ContagionDiseaseDirector _diseaseDirector = new ContagionDiseaseDirector();

    private readonly ContagionMapDeveloperDiagnosticsController _developerDiagnosticsController;

    private readonly ContagionMapSlaughterOverrideTracker _slaughterOverrideTracker;

    private readonly ContagionPawnTransmissionProcessor _pawnTransmissionProcessor;

    private readonly ContagionVomitFomiteTracker _vomitFomiteTracker;

    private readonly ContagionEnvironmentalExposureProcessor _environmentalExposureProcessor;

    private readonly ContagionMapSeedingState _seedingState;

    public Contagion_MapTransmissionComponent(Map map)
        : base(map)
    {
        _developerDiagnosticsController = new ContagionMapDeveloperDiagnosticsController(map);
        _slaughterOverrideTracker = new ContagionMapSlaughterOverrideTracker();
        _pawnTransmissionProcessor = new ContagionPawnTransmissionProcessor(map, _developerDiagnosticsController);
        _vomitFomiteTracker = new ContagionVomitFomiteTracker(map);
        _vomitFomiteTracker.Rebind(_contaminatedVomitFilth, _contaminatedVomitDiseases, _contaminatedVomitTicks);
        _environmentalExposureProcessor = new ContagionEnvironmentalExposureProcessor(this);
        _seedingState = new ContagionMapSeedingState();
        _seedingState.Rebind(_seederCooldownDiseases, _seederCooldownKeys, _seederCooldownTicks, _pendingEvents, _diseaseDirector);
    }

    public Map Map => map;

    public IReadOnlyList<PendingDiseaseEvent> PendingEvents => _seedingState.PendingEvents;

    public ContagionDiseaseDirector DiseaseDirector => _seedingState.DiseaseDirector;

    public HediffDef DeveloperForcedArrivalDisease => _developerDiagnosticsController.ForcedArrivalDisease;

    public IReadOnlyList<ContagionTransmissionTrace> DeveloperTransmissionTraces => _developerDiagnosticsController.TransmissionTraces;

    public bool DeveloperTraceCaptureEnabled => _developerDiagnosticsController.TraceCaptureEnabled;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Collections.Look(ref _contaminatedVomitFilth, "contaminatedVomitFilth", LookMode.Reference);
        Scribe_Collections.Look(ref _contaminatedVomitDiseases, "contaminatedVomitDiseases", LookMode.Def);
        Scribe_Collections.Look(ref _contaminatedVomitTicks, "contaminatedVomitTicks", LookMode.Value);
        Scribe_Collections.Look(ref _seederCooldownDiseases, "seederCooldownDiseases", LookMode.Def);
        Scribe_Collections.Look(ref _seederCooldownKeys, "seederCooldownKeys", LookMode.Value);
        Scribe_Collections.Look(ref _seederCooldownTicks, "seederCooldownTicks", LookMode.Value);
        Scribe_Collections.Look(ref _pendingEvents, "pendingEvents", LookMode.Deep);
        Scribe_Deep.Look(ref _diseaseDirector, "diseaseDirector");

        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            _contaminatedVomitFilth ??= new List<Filth>();
            _contaminatedVomitDiseases ??= new List<HediffDef>();
            _contaminatedVomitTicks ??= new List<int>();
            _seederCooldownDiseases ??= new List<HediffDef>();
            _seederCooldownKeys ??= new List<string>();
            _seederCooldownTicks ??= new List<int>();
            _pendingEvents ??= new List<PendingDiseaseEvent>();
            _diseaseDirector ??= new ContagionDiseaseDirector();
            _vomitFomiteTracker.Rebind(_contaminatedVomitFilth, _contaminatedVomitDiseases, _contaminatedVomitTicks);
            _vomitFomiteTracker.Cleanup();
            _seedingState.Rebind(_seederCooldownDiseases, _seederCooldownKeys, _seederCooldownTicks, _pendingEvents, _diseaseDirector);
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

    public void DeveloperArmForcedArrival(HediffDef diseaseDef)
    {
        _developerDiagnosticsController.ArmForcedArrival(diseaseDef);
    }

    public void DeveloperClearForcedArrival()
    {
        _developerDiagnosticsController.ClearForcedArrival();
    }

    public void ArmForceRot(int pawnId)
    {
        _slaughterOverrideTracker.ArmForceRot(pawnId, Find.TickManager.TicksGame);
    }

    public bool ConsumeForceRot(int pawnId) => _slaughterOverrideTracker.ConsumeForceRot(pawnId);

    public void ArmButcherBypass(int pawnId)
    {
        _slaughterOverrideTracker.ArmButcherBypass(pawnId, Find.TickManager.TicksGame);
    }

    public bool ConsumeButcherBypass(int pawnId) => _slaughterOverrideTracker.ConsumeButcherBypass(pawnId);

    public void DeveloperRecordTransmissionTrace(
        Pawn sourcePawn,
        Pawn targetPawn,
        HediffDef diseaseDef,
        ContagionDebugVectorKind vectorKind)
    {
        _developerDiagnosticsController.RecordTransmissionTrace(sourcePawn, targetPawn, diseaseDef, vectorKind);
    }

    public void DeveloperClearAllTraces()
    {
        _developerDiagnosticsController.ClearAllTraces();
    }

    public void DeveloperToggleTraceCapture()
    {
        _developerDiagnosticsController.ToggleTraceCapture();
    }

    public void DeveloperClearTracesForPawn(Pawn pawn)
    {
        _developerDiagnosticsController.ClearTracesForPawn(pawn);
    }

    public bool DeveloperHasTracesForPawn(Pawn pawn)
    {
        return _developerDiagnosticsController.HasTracesForPawn(pawn);
    }

    public void DeveloperSetHoverPair(Pawn sourcePawn, Pawn targetPawn)
    {
        _developerDiagnosticsController.SetHoverPair(sourcePawn, targetPawn);
    }

    public bool DeveloperTryGetHoverPair(out Pawn sourcePawn, out Pawn targetPawn)
    {
        return _developerDiagnosticsController.TryGetHoverPair(out sourcePawn, out targetPawn);
    }

    public void DeveloperClearHoverPair()
    {
        _developerDiagnosticsController.ClearHoverPair();
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

        _vomitFomiteTracker.Cleanup();
        _slaughterOverrideTracker.CleanupStaleEntries(ticksGame);

        if (runEnvironmental)
        {
            long environmentalTiming = ContagionDiagnostics.BeginTiming();
            RunGeneralSeederPass(spawnedPawns);
            _environmentalExposureProcessor.RunEnvironmentalExposurePass(spawnedPawns);
            ContagionDiagnostics.EndTiming(ContagionPerformanceMetric.EnvironmentalPass, environmentalTiming);
        }

        if (!runTransmission)
        {
            return;
        }

        long transmissionTiming = ContagionDiagnostics.BeginTiming();
        _vomitFomiteTracker.RunFomiteExposurePass(spawnedPawns);

        if (spawnedPawns.Count >= 2)
        {
            _pawnTransmissionProcessor.RunPawnTransmissionPass(spawnedPawns);
        }

        ContagionDiagnostics.EndTiming(ContagionPerformanceMetric.TransmissionPass, transmissionTiming);
    }

    public void NotifyVomitFilthCreated(Filth filth, Pawn sourcePawn)
    {
        _vomitFomiteTracker.NotifyVomitFilthCreated(filth, sourcePawn);
    }

    private void RunGeneralSeederPass(IReadOnlyList<Pawn> spawnedPawns)
    {
        ContagionSeedingCoordinator.RunGeneralSeeding(this, spawnedPawns);
    }
}
