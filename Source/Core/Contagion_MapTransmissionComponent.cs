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

    private const int TicksPerHour = 2500;

    private const float MinCleanlinessFactor = 0.1f;

    private const float MaxCleanlinessFactor = 3f;

    private const float MaxEnvironmentalWaterFactor = 3f;

    private const float MinFomitePotency = 0.05f;

    private List<Filth> _contaminatedVomitFilth = new List<Filth>();

    private List<HediffDef> _contaminatedVomitDiseases = new List<HediffDef>();

    private List<int> _contaminatedVomitTicks = new List<int>();

    private List<HediffDef> _seederCooldownDiseases = new List<HediffDef>();

    private List<string> _seederCooldownKeys = new List<string>();

    private List<int> _seederCooldownTicks = new List<int>();

    private List<PendingDiseaseEvent> _pendingEvents = new List<PendingDiseaseEvent>();

    private ContagionDiseaseDirector _diseaseDirector = new ContagionDiseaseDirector();

    private sealed class TransmissionSource
    {
        public TransmissionSource(Pawn pawn, ResolvedTransmissionProfile resolvedProfile)
        {
            Pawn = pawn;
            ResolvedProfile = resolvedProfile;
        }

        public Pawn Pawn { get; }

        public ResolvedTransmissionProfile ResolvedProfile { get; }

        public float SuppressionFactor { get; set; } = 1f;
    }

    private sealed class EnvironmentalProfile
    {
        public EnvironmentalProfile(
            ResolvedTransmissionProfile resolvedProfile,
            Vector_Environmental vector,
            Seeder_Environmental seeder,
            float biomeCommonality)
        {
            ResolvedProfile = resolvedProfile;
            Vector = vector;
            Seeder = seeder;
            BiomeCommonality = biomeCommonality;
        }

        public ResolvedTransmissionProfile ResolvedProfile { get; }

        public Vector_Environmental Vector { get; }

        public Seeder_Environmental Seeder { get; }

        public float BiomeCommonality { get; }
    }

    public Contagion_MapTransmissionComponent(Map map)
        : base(map)
    {
    }

    public Map Map => map;

    public IReadOnlyList<PendingDiseaseEvent> PendingEvents => _pendingEvents;

    public ContagionDiseaseDirector DiseaseDirector => _diseaseDirector;

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
            CleanupContaminatedVomit();
        }
    }

    public bool IsAtActiveCaseLimit(ResolvedTransmissionProfile resolvedProfile, TransmissionSeeder seeder)
    {
        if (resolvedProfile?.Profile == null)
        {
            return false;
        }

        int activeCaseLimit = seeder?.maxActiveCases > 0 ? seeder.maxActiveCases : resolvedProfile.Profile.maxActiveCases;
        return ContagionTransmissionUtility.IsProfileActiveOnMap(map, resolvedProfile, activeCaseLimit);
    }

    public bool CanRunSeeder(ResolvedTransmissionProfile resolvedProfile, TransmissionSeeder seeder)
    {
        if (resolvedProfile?.Profile == null || seeder == null)
        {
            return false;
        }

        if (IsAtActiveCaseLimit(resolvedProfile, seeder))
        {
            return false;
        }

        if (seeder.cooldownDays <= 0f)
        {
            return true;
        }

        string key = GetSeederCooldownKey(seeder);
        int cooldownTicks = Mathf.RoundToInt(seeder.cooldownDays * 60000f);
        int currentTick = Find.TickManager.TicksGame;
        for (int i = 0; i < _seederCooldownDiseases.Count; i++)
        {
            if (_seederCooldownDiseases[i] == resolvedProfile.DiseaseDef && _seederCooldownKeys[i] == key)
            {
                return currentTick - _seederCooldownTicks[i] >= cooldownTicks;
            }
        }

        return true;
    }

    public PendingDiseaseEvent GetPendingEvent(HediffDef diseaseDef)
    {
        if (diseaseDef == null)
        {
            return null;
        }

        for (int i = 0; i < _pendingEvents.Count; i++)
        {
            if (_pendingEvents[i]?.diseaseDef == diseaseDef)
            {
                return _pendingEvents[i];
            }
        }

        return null;
    }

    public void AddPendingEvent(PendingDiseaseEvent pendingEvent)
    {
        if (pendingEvent == null)
        {
            return;
        }

        _pendingEvents.Add(pendingEvent);
    }

    public void RemovePendingEvent(PendingDiseaseEvent pendingEvent)
    {
        if (pendingEvent == null)
        {
            return;
        }

        _pendingEvents.Remove(pendingEvent);
    }

    public void NotifySeederFired(ResolvedTransmissionProfile resolvedProfile, TransmissionSeeder seeder)
    {
        if (resolvedProfile?.DiseaseDef == null || seeder == null || seeder.cooldownDays <= 0f)
        {
            return;
        }

        string key = GetSeederCooldownKey(seeder);
        int currentTick = Find.TickManager.TicksGame;
        for (int i = 0; i < _seederCooldownDiseases.Count; i++)
        {
            if (_seederCooldownDiseases[i] == resolvedProfile.DiseaseDef && _seederCooldownKeys[i] == key)
            {
                _seederCooldownTicks[i] = currentTick;
                return;
            }
        }

        _seederCooldownDiseases.Add(resolvedProfile.DiseaseDef);
        _seederCooldownKeys.Add(key);
        _seederCooldownTicks.Add(currentTick);
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
            _diseaseDirector.DailyTick(map);
        }

        if (!runTransmission && !runEnvironmental)
        {
            return;
        }

        CleanupContaminatedVomit();

        if (runEnvironmental)
        {
            long environmentalTiming = ContagionDiagnostics.BeginTiming();
            RunGeneralSeederPass(spawnedPawns);
            RunEnvironmentalExposurePass(spawnedPawns);
            ContagionDiagnostics.EndTiming(ContagionPerformanceMetric.EnvironmentalPass, environmentalTiming);
        }

        if (!runTransmission)
        {
            return;
        }

        long transmissionTiming = ContagionDiagnostics.BeginTiming();
        RunFomiteExposurePass(spawnedPawns);

        if (spawnedPawns.Count >= 2)
        {
            RunPawnTransmissionPass(spawnedPawns);
        }

        ContagionDiagnostics.EndTiming(ContagionPerformanceMetric.TransmissionPass, transmissionTiming);
    }

    private void RunPawnTransmissionPass(IReadOnlyList<Pawn> spawnedPawns)
    {
        List<TransmissionSource> sources = GatherTransmissionSources(spawnedPawns);
        if (sources.Count == 0)
        {
            return;
        }

        // Suppression depends only on (map, disease), so compute it once per disease for the pass.
        Dictionary<HediffDef, float> suppressionByDisease = new Dictionary<HediffDef, float>();
        for (int i = 0; i < sources.Count; i++)
        {
            TransmissionSource source = sources[i];
            HediffDef diseaseDef = source.ResolvedProfile.DiseaseDef;
            if (!suppressionByDisease.TryGetValue(diseaseDef, out float suppression))
            {
                suppression = ContagionTransmissionUtility.GetSpreadSuppressionFactor(map, source.ResolvedProfile);
                suppressionByDisease[diseaseDef] = suppression;
            }

            source.SuppressionFactor = suppression;
        }

        for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            TransmissionSource source = sources[sourceIndex];
            for (int targetIndex = 0; targetIndex < spawnedPawns.Count; targetIndex++)
            {
                Pawn targetPawn = spawnedPawns[targetIndex];
                if (targetPawn == source.Pawn)
                {
                    continue;
                }

                TryTransmit(source, targetPawn);
            }
        }
    }

    public void NotifyVomitFilthCreated(Filth filth, Pawn sourcePawn)
    {
        if (filth == null || sourcePawn == null || sourcePawn.Map != map)
        {
            return;
        }

        foreach (ResolvedTransmissionProfile resolvedProfile in ContagionDiseaseUtility.GetContagiousProfiles(sourcePawn))
        {
            if (!ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Fomite fomiteVector) || !fomiteVector.contaminatesVomit)
            {
                continue;
            }

            int index = _contaminatedVomitFilth.IndexOf(filth);
            if (index >= 0)
            {
                _contaminatedVomitDiseases[index] = resolvedProfile.DiseaseDef;
                _contaminatedVomitTicks[index] = Find.TickManager.TicksGame;
            }
            else
            {
                _contaminatedVomitFilth.Add(filth);
                _contaminatedVomitDiseases.Add(resolvedProfile.DiseaseDef);
                _contaminatedVomitTicks.Add(Find.TickManager.TicksGame);
            }

            ContagionDiagnostics.Record(ContagionDiagnosticCounter.VomitFilthContaminated);
            ContagionDiagnostics.Trace($"Vomit contaminated: {resolvedProfile.DiseaseDef.defName} from {sourcePawn.LabelShortCap}.");
            return;
        }
    }

    private void RunEnvironmentalExposurePass(IReadOnlyList<Pawn> spawnedPawns)
    {
        List<EnvironmentalProfile> environmentalProfiles = GatherEnvironmentalProfiles();
        if (environmentalProfiles.Count == 0)
        {
            return;
        }

        float transmissionMultiplier = Contagion_Mod.Settings?.EffectiveTransmissionMultiplier ?? 1f;

        for (int pawnIndex = 0; pawnIndex < spawnedPawns.Count; pawnIndex++)
        {
            Pawn pawn = spawnedPawns[pawnIndex];
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map != map)
            {
                continue;
            }

            for (int profileIndex = 0; profileIndex < environmentalProfiles.Count; profileIndex++)
            {
                if (TryApplyEnvironmentalExposure(pawn, environmentalProfiles[profileIndex], transmissionMultiplier))
                {
                    break;
                }
            }
        }
    }

    private void RunFomiteExposurePass(IReadOnlyList<Pawn> spawnedPawns)
    {
        if (_contaminatedVomitFilth.Count == 0)
        {
            return;
        }

        float transmissionMultiplier = Contagion_Mod.Settings?.EffectiveTransmissionMultiplier ?? 1f;
        Dictionary<HediffDef, float> suppressionByDisease = new Dictionary<HediffDef, float>();
        for (int pawnIndex = 0; pawnIndex < spawnedPawns.Count; pawnIndex++)
        {
            Pawn pawn = spawnedPawns[pawnIndex];
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map != map)
            {
                continue;
            }

            for (int contaminationIndex = 0; contaminationIndex < _contaminatedVomitFilth.Count; contaminationIndex++)
            {
                Filth filth = _contaminatedVomitFilth[contaminationIndex];
                if (filth == null || filth.Position != pawn.Position)
                {
                    continue;
                }

                if (!DiseaseProfileCache.TryGetResolvedProfile(_contaminatedVomitDiseases[contaminationIndex], out ResolvedTransmissionProfile resolvedProfile)
                    || !ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Fomite fomiteVector))
                {
                    continue;
                }

                float potencyFactor = GetFomitePotencyFactor(_contaminatedVomitTicks[contaminationIndex], fomiteVector.potencyDecayPerHour);
                if (potencyFactor <= MinFomitePotency)
                {
                    continue;
                }

                float suppression = 1f;
                if (ContagionTransmissionUtility.IsSuppressionTarget(pawn))
                {
                    if (!suppressionByDisease.TryGetValue(resolvedProfile.DiseaseDef, out suppression))
                    {
                        suppression = ContagionTransmissionUtility.GetSpreadSuppressionFactor(map, resolvedProfile);
                        suppressionByDisease[resolvedProfile.DiseaseDef] = suppression;
                    }
                }

                ContagionDiagnostics.Record(ContagionDiagnosticCounter.FomiteAttempted);
                float chance = ContagionTransmissionUtility.BuildSeederChance(
                    fomiteVector.baseChancePerContact * potencyFactor * suppression,
                    pawn,
                    resolvedProfile,
                    map,
                    transmissionMultiplier,
                    out HediffDef _);
                if (!Rand.Chance(Mathf.Clamp01(chance)))
                {
                    continue;
                }

                if (ContagionDiseaseUtility.TrySeedIncubation(
                    pawn,
                    resolvedProfile.DiseaseDef,
                    resolvedProfile.PartsToAffect,
                    ContagionDiagnosticOrigin.Spread,
                    out HediffDef _))
                {
                    ContagionDiagnostics.Record(ContagionDiagnosticCounter.FomiteSeeded);
                    ContagionDiagnostics.Trace($"Fomite transmission: {resolvedProfile.DiseaseDef.defName} on {pawn.LabelShortCap} from vomit filth.");
                    break;
                }
            }
        }
    }

    private static List<TransmissionSource> GatherTransmissionSources(IReadOnlyList<Pawn> spawnedPawns)
    {
        List<TransmissionSource> sources = new List<TransmissionSource>();

        for (int pawnIndex = 0; pawnIndex < spawnedPawns.Count; pawnIndex++)
        {
            Pawn pawn = spawnedPawns[pawnIndex];
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.health?.hediffSet == null)
            {
                continue;
            }

            foreach (ResolvedTransmissionProfile resolvedProfile in ContagionDiseaseUtility.GetContagiousProfiles(pawn))
            {
                sources.Add(new TransmissionSource(pawn, resolvedProfile));
            }
        }

        return sources;
    }

    private bool TryTransmit(TransmissionSource source, Pawn targetPawn)
    {
        if (targetPawn == null || targetPawn.Dead || !targetPawn.Spawned || targetPawn.Map != map)
        {
            return false;
        }

        float transmissionMultiplier = Contagion_Mod.Settings?.EffectiveTransmissionMultiplier ?? 1f;

        for (int vectorIndex = 0; vectorIndex < source.ResolvedProfile.Profile.vectors.Count; vectorIndex++)
        {
            TransmissionVector vector = source.ResolvedProfile.Profile.vectors[vectorIndex];
            if (vector is Vector_Airborne airborne && TryTransmitAirborne(source, targetPawn, airborne, transmissionMultiplier))
            {
                return true;
            }

            if (vector is Vector_Proximity proximity && TryTransmitProximity(source, targetPawn, proximity, transmissionMultiplier))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryApplyEnvironmentalExposure(
        Pawn pawn,
        EnvironmentalProfile environmentalProfile,
        float transmissionMultiplier)
    {
        if (!ContagionSeedingCoordinator.TryGetEnvironmentalSeedingContext(
            this,
            environmentalProfile.ResolvedProfile,
            environmentalProfile.Seeder,
            out PendingDiseaseEvent windowEvent,
            out float seedingMultiplier))
        {
            return false;
        }

        ContagionDiagnostics.Record(ContagionDiagnosticCounter.EnvironmentalAttempted);
        Room room = pawn.Position.GetRoom(map);
        float ambientTemperature = GetAmbientTemperature(room);
        float chance = environmentalProfile.Vector.baseChancePerCheck
            * environmentalProfile.Seeder.baseChanceMultiplier
            * environmentalProfile.BiomeCommonality
            * seedingMultiplier;
        chance *= GetEnvironmentalTemperatureFactor(ambientTemperature, environmentalProfile.Vector);
        if (chance <= 0f)
        {
            return false;
        }

        chance *= GetEnvironmentalShelterFactor(pawn.Position, room, ambientTemperature, environmentalProfile.Vector);
        chance *= GetWaterProximityFactor(pawn.Position, environmentalProfile.Vector);
        chance = ContagionTransmissionUtility.BuildSeederChance(
            chance,
            pawn,
            environmentalProfile.ResolvedProfile,
            map,
            transmissionMultiplier,
            out HediffDef _);
        if (!Rand.Chance(Mathf.Clamp01(chance)))
        {
            return false;
        }

        bool seeded = ContagionDiseaseUtility.TrySeedIncubation(
            pawn,
            environmentalProfile.ResolvedProfile.DiseaseDef,
            environmentalProfile.ResolvedProfile.PartsToAffect,
            ContagionDiagnosticOrigin.Incidence,
            out HediffDef _);
        if (seeded)
        {
            ContagionSeedingCoordinator.NotifyEnvironmentalSeeded(this, environmentalProfile.ResolvedProfile, environmentalProfile.Seeder, windowEvent);
            ContagionDiagnostics.Record(ContagionDiagnosticCounter.EnvironmentalSeeded);
            ContagionDiagnostics.Trace($"Environmental transmission: {environmentalProfile.ResolvedProfile.DiseaseDef.defName} on {pawn.LabelShortCap}.");
        }

        return seeded;
    }

    private bool TryTransmitAirborne(
        TransmissionSource source,
        Pawn targetPawn,
        Vector_Airborne vector,
        float transmissionMultiplier)
    {
        if (!source.Pawn.Position.InHorDistOf(targetPawn.Position, vector.maxRange))
        {
            return false;
        }

        ContagionDiagnostics.Record(ContagionDiagnosticCounter.AirborneAttempted);
        float distance = GetHorizontalDistance(source.Pawn.Position, targetPawn.Position);
        bool sourceRoofed = map.roofGrid.Roofed(source.Pawn.Position);
        bool targetRoofed = map.roofGrid.Roofed(targetPawn.Position);
        bool hasLineOfSight = GenSight.LineOfSight(source.Pawn.Position, targetPawn.Position, map);
        float enclosureFactor = sourceRoofed && targetRoofed ? 1f : vector.outdoorFactor;
        float obstructionFactor = hasLineOfSight ? 1f : vector.obstructedFactor;
        float maskFactor = ContagionMaskUtility.GetRespiratoryMaskFactor(source.Pawn, targetPawn, vector);
        float suppressionFactor = ContagionTransmissionUtility.IsSuppressionTarget(targetPawn) ? source.SuppressionFactor : 1f;
        float chance = ContagionTransmissionUtility.BuildSourceTargetChance(
            vector.baseChancePerCheck,
            source.Pawn,
            targetPawn,
            source.ResolvedProfile,
            vector,
            map,
            GetDistanceFactor(distance, vector.distanceFalloffRate) * enclosureFactor * obstructionFactor * maskFactor * suppressionFactor,
            transmissionMultiplier,
            out HediffDef _);

        if (!Rand.Chance(Mathf.Clamp01(chance)))
        {
            return false;
        }

        bool seeded = ContagionDiseaseUtility.TrySeedIncubation(
            targetPawn,
            source.ResolvedProfile.DiseaseDef,
            source.ResolvedProfile.PartsToAffect,
            source.Pawn,
            ContagionDiagnosticOrigin.Spread,
            out HediffDef _);
        if (seeded)
        {
            ContagionDiagnostics.Record(ContagionDiagnosticCounter.AirborneSeeded);
        }

        return seeded;
    }

    private bool TryTransmitProximity(
        TransmissionSource source,
        Pawn targetPawn,
        Vector_Proximity vector,
        float transmissionMultiplier)
    {
        if (!source.Pawn.Position.InHorDistOf(targetPawn.Position, vector.maxRange))
        {
            return false;
        }

        ContagionDiagnostics.Record(ContagionDiagnosticCounter.ProximityAttempted);
        float distance = GetHorizontalDistance(source.Pawn.Position, targetPawn.Position);
        Room sourceRoom = source.Pawn.Position.GetRoom(map);
        Room targetRoom = targetPawn.Position.GetRoom(map);
        float outdoorFactor = IsOutdoors(sourceRoom) || IsOutdoors(targetRoom) ? vector.outdoorFactor : 1f;
        float cleanlinessFactor = GetLocalCleanlinessFactor(targetPawn.Position, targetRoom, vector.cleanlinessImpact, vector.outdoorFilthRadius);
        float maskFactor = ContagionMaskUtility.GetRespiratoryMaskFactor(source.Pawn, targetPawn, vector);
        float suppressionFactor = ContagionTransmissionUtility.IsSuppressionTarget(targetPawn) ? source.SuppressionFactor : 1f;
        float chance = ContagionTransmissionUtility.BuildSourceTargetChance(
            vector.baseChancePerCheck,
            source.Pawn,
            targetPawn,
            source.ResolvedProfile,
            vector,
            map,
            GetDistanceFactor(distance, vector.distanceFalloffRate) * outdoorFactor * cleanlinessFactor * maskFactor * suppressionFactor,
            transmissionMultiplier,
            out HediffDef _);

        if (!Rand.Chance(Mathf.Clamp01(chance)))
        {
            return false;
        }

        bool seeded = ContagionDiseaseUtility.TrySeedIncubation(
            targetPawn,
            source.ResolvedProfile.DiseaseDef,
            source.ResolvedProfile.PartsToAffect,
            source.Pawn,
            ContagionDiagnosticOrigin.Spread,
            out HediffDef _);
        if (seeded)
        {
            ContagionDiagnostics.Record(ContagionDiagnosticCounter.ProximitySeeded);
        }

        return seeded;
    }

    private List<EnvironmentalProfile> GatherEnvironmentalProfiles()
    {
        List<EnvironmentalProfile> environmentalProfiles = new List<EnvironmentalProfile>();

        foreach (ResolvedTransmissionProfile resolvedProfile in DiseaseProfileCache.AllProfiles)
        {
            if (!TryGetEnvironmentalSettings(resolvedProfile.Profile, out Vector_Environmental vector, out Seeder_Environmental seeder))
            {
                continue;
            }

            float biomeCommonality = GetBiomeDiseaseCommonality(resolvedProfile);
            if (biomeCommonality <= 0f)
            {
                continue;
            }

            environmentalProfiles.Add(new EnvironmentalProfile(resolvedProfile, vector, seeder, biomeCommonality));
        }

        return environmentalProfiles;
    }

    private float GetBiomeDiseaseCommonality(ResolvedTransmissionProfile resolvedProfile)
    {
        if (resolvedProfile?.LinkedIncidentDef == null || map?.Biome == null)
        {
            return 0f;
        }

        return Mathf.Max(0f, map.Biome.CommonalityOfDisease(resolvedProfile.LinkedIncidentDef));
    }

    private static bool TryGetEnvironmentalSettings(
        TransmissionProfile profile,
        out Vector_Environmental vector,
        out Seeder_Environmental seeder)
    {
        vector = null;
        seeder = null;

        if (profile?.vectors == null || profile.seeders == null)
        {
            return false;
        }

        for (int i = 0; i < profile.vectors.Count; i++)
        {
            if (profile.vectors[i] is Vector_Environmental environmentalVector)
            {
                vector = environmentalVector;
                break;
            }
        }

        if (vector == null)
        {
            return false;
        }

        for (int i = 0; i < profile.seeders.Count; i++)
        {
            if (profile.seeders[i] is Seeder_Environmental environmentalSeeder)
            {
                seeder = environmentalSeeder;
                break;
            }
        }

        return seeder != null;
    }

    private static float GetHorizontalDistance(IntVec3 first, IntVec3 second)
    {
        float deltaX = first.x - second.x;
        float deltaZ = first.z - second.z;
        return Mathf.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
    }

    private static float GetDistanceFactor(float distance, float distanceFalloffRate)
    {
        return Mathf.Exp(-Mathf.Max(0.01f, distanceFalloffRate) * distance);
    }

    private float GetFomitePotencyFactor(int contaminationTick, float potencyDecayPerHour)
    {
        float elapsedHours = Mathf.Max(0f, (Find.TickManager.TicksGame - contaminationTick) / (float)TicksPerHour);
        return Mathf.Exp(-Mathf.Max(0f, potencyDecayPerHour) * elapsedHours);
    }

    private void CleanupContaminatedVomit()
    {
        for (int i = _contaminatedVomitFilth.Count - 1; i >= 0; i--)
        {
            Filth filth = _contaminatedVomitFilth[i];
            HediffDef diseaseDef = i < _contaminatedVomitDiseases.Count ? _contaminatedVomitDiseases[i] : null;
            int contaminationTick = i < _contaminatedVomitTicks.Count ? _contaminatedVomitTicks[i] : 0;
            bool remove = filth == null || filth.Destroyed || !filth.Spawned || filth.Map != map || diseaseDef == null;

            if (!remove
                && (!DiseaseProfileCache.TryGetResolvedProfile(diseaseDef, out ResolvedTransmissionProfile resolvedProfile)
                    || !ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Fomite fomiteVector)
                    || GetFomitePotencyFactor(contaminationTick, fomiteVector.potencyDecayPerHour) <= MinFomitePotency))
            {
                remove = true;
            }

            if (!remove)
            {
                continue;
            }

            _contaminatedVomitFilth.RemoveAt(i);

            if (i < _contaminatedVomitDiseases.Count)
            {
                _contaminatedVomitDiseases.RemoveAt(i);
            }

            if (i < _contaminatedVomitTicks.Count)
            {
                _contaminatedVomitTicks.RemoveAt(i);
            }
        }
    }

    private float GetAmbientTemperature(Room room)
    {
        if (room == null || room.UsesOutdoorTemperature)
        {
            return map.mapTemperature.OutdoorTemp;
        }

        return room.Temperature;
    }

    private static float GetEnvironmentalTemperatureFactor(float ambientTemperature, Vector_Environmental vector)
    {
        if (ambientTemperature <= vector.minTemperature)
        {
            return 0f;
        }

        if (ambientTemperature >= vector.peakTemperature)
        {
            return 1f;
        }

        return Mathf.InverseLerp(vector.minTemperature, vector.peakTemperature, ambientTemperature);
    }

    private float GetEnvironmentalShelterFactor(IntVec3 position, Room room, float ambientTemperature, Vector_Environmental vector)
    {
        if (room == null || room.UsesOutdoorTemperature || room.PsychologicallyOutdoors)
        {
            return 1f;
        }

        int cellsFromUnroofed = GetCellsFromUnroofed(position, 30);
        float shelterFactor = Mathf.Clamp01(1f - vector.indoorReductionPerCellFromEdge * Mathf.Max(1, cellsFromUnroofed));
        if (ambientTemperature < vector.coolRoomThreshold)
        {
            shelterFactor *= Mathf.InverseLerp(vector.minTemperature, vector.coolRoomThreshold, ambientTemperature);
        }

        return shelterFactor;
    }

    private void RunGeneralSeederPass(IReadOnlyList<Pawn> spawnedPawns)
    {
        ContagionSeedingCoordinator.RunGeneralSeeding(this, spawnedPawns);
    }

    private int GetCellsFromUnroofed(IntVec3 center, int maxRadius)
    {
        if (!center.InBounds(map) || !map.roofGrid.Roofed(center))
        {
            return 0;
        }

        for (int radius = 1; radius <= maxRadius; radius++)
        {
            for (int x = center.x - radius; x <= center.x + radius; x++)
            {
                for (int z = center.z - radius; z <= center.z + radius; z++)
                {
                    if (x != center.x - radius && x != center.x + radius && z != center.z - radius && z != center.z + radius)
                    {
                        continue;
                    }

                    IntVec3 candidate = new IntVec3(x, 0, z);
                    if (candidate.InBounds(map) && !map.roofGrid.Roofed(candidate))
                    {
                        return radius;
                    }
                }
            }
        }

        return maxRadius;
    }

    private float GetWaterProximityFactor(IntVec3 center, Vector_Environmental vector)
    {
        if (vector.waterProximityRadius <= 0 || vector.waterProximityWeight <= 0f)
        {
            return 1f;
        }

        int nearbyWaterCells = 0;
        int radius = vector.waterProximityRadius;
        for (int x = center.x - radius; x <= center.x + radius; x++)
        {
            for (int z = center.z - radius; z <= center.z + radius; z++)
            {
                IntVec3 candidate = new IntVec3(x, 0, z);
                if (!candidate.InBounds(map) || !center.InHorDistOf(candidate, radius))
                {
                    continue;
                }

                TerrainDef terrain = map.terrainGrid.TerrainAt(candidate);
                if (terrain != null && terrain.IsWater)
                {
                    nearbyWaterCells++;
                }
            }
        }

        return Mathf.Clamp(1f + nearbyWaterCells * vector.waterProximityWeight, 1f, MaxEnvironmentalWaterFactor);
    }

    private static bool IsOutdoors(Room room)
    {
        return room == null || room.PsychologicallyOutdoors;
    }

    private float GetLocalCleanlinessFactor(IntVec3 position, Room room, float cleanlinessImpact, int outdoorFilthRadius)
    {
        if (cleanlinessImpact <= 0f)
        {
            return 1f;
        }

        if (room == null || room.PsychologicallyOutdoors)
        {
            return GetOutdoorFilthCleanlinessFactor(position, cleanlinessImpact, outdoorFilthRadius);
        }

        float cleanliness = room.GetStat(RoomStatDefOf.Cleanliness);
        return Mathf.Clamp(1f - cleanliness * cleanlinessImpact, MinCleanlinessFactor, MaxCleanlinessFactor);
    }

    private float GetOutdoorFilthCleanlinessFactor(IntVec3 center, float cleanlinessImpact, int outdoorFilthRadius)
    {
        if (outdoorFilthRadius <= 0)
        {
            return 1f;
        }

        int filthCount = 0;
        for (int x = center.x - outdoorFilthRadius; x <= center.x + outdoorFilthRadius; x++)
        {
            for (int z = center.z - outdoorFilthRadius; z <= center.z + outdoorFilthRadius; z++)
            {
                IntVec3 candidate = new IntVec3(x, 0, z);
                if (!candidate.InBounds(map) || !center.InHorDistOf(candidate, outdoorFilthRadius))
                {
                    continue;
                }

                List<Thing> things = candidate.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is Filth)
                    {
                        filthCount++;
                    }
                }
            }
        }

        float area = Mathf.Max(1f, (2 * outdoorFilthRadius + 1) * (2 * outdoorFilthRadius + 1));
        float filthDensity = filthCount / area;
        return Mathf.Clamp(1f + filthDensity * cleanlinessImpact, MinCleanlinessFactor, MaxCleanlinessFactor);
    }

    private static string GetSeederCooldownKey(TransmissionSeeder seeder)
    {
        return seeder.GetType().FullName;
    }
}
