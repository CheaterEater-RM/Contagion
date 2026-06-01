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

        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            _vomitFomiteTracker ??= new ContagionVomitFomiteTracker();
            _seedingState ??= new ContagionMapSeedingState();
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
            long environmentalTiming = ContagionDiagnostics.BeginTiming();
            RunGeneralSeederPass(spawnedPawns);
            _environmentalExposureProcessor.RunEnvironmentalExposurePass(spawnedPawns);
            ContagionSeedingCoordinator.RunSpontaneousFalsePositives(spawnedPawns, EnvironmentalCheckInterval);
            ContagionDiagnostics.EndTiming(ContagionPerformanceMetric.EnvironmentalPass, environmentalTiming);
        }

        if (!runTransmission)
        {
            return;
        }

        long transmissionTiming = ContagionDiagnostics.BeginTiming();
        _vomitFomiteTracker.RunFomiteExposurePass(spawnedPawns, map);
        _corpseExposureProcessor.RunCorpseExposurePass(spawnedPawns, TransmissionCheckInterval);

        if (spawnedPawns.Count >= 2)
        {
            _pawnTransmissionProcessor.RunPawnTransmissionPass(spawnedPawns);
        }

        ContagionDiagnostics.EndTiming(ContagionPerformanceMetric.TransmissionPass, transmissionTiming);
    }

    public void NotifyVomitFilthCreated(Filth filth, Pawn sourcePawn)
    {
        _vomitFomiteTracker.NotifyVomitFilthCreated(filth, sourcePawn, map);
    }

    private void RunGeneralSeederPass(IReadOnlyList<Pawn> spawnedPawns)
    {
        ContagionSeedingCoordinator.RunGeneralSeeding(this, spawnedPawns);
    }
}
