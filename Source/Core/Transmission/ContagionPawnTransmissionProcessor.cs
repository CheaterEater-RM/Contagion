using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

internal sealed class ContagionPawnTransmissionProcessor
{
    private readonly Map _map;

    private readonly ContagionMapDeveloperDiagnosticsController _developerDiagnosticsController;

    private readonly SpatialPawnIndex _spatialIndex = new SpatialPawnIndex();

    private readonly List<Pawn> _rangeQueryBuffer = new List<Pawn>();

    private readonly HashSet<Pawn> _candidatePawnSet = new HashSet<Pawn>();

    private readonly Dictionary<Room, List<Pawn>> _pawnsByRoom = new Dictionary<Room, List<Pawn>>();

    private readonly Dictionary<int, float> _pathDistanceByCell = new Dictionary<int, float>();

    private readonly Queue<IntVec3> _pathOpenCells = new Queue<IntVec3>();

    private sealed class TransmissionSource
    {
        public TransmissionSource(Pawn pawn, ResolvedTransmissionProfile resolvedProfile)
        {
            Pawn = pawn;
            ResolvedProfile = resolvedProfile;
        }

        public Pawn Pawn { get; }

        public ResolvedTransmissionProfile ResolvedProfile { get; }

    }

    public ContagionPawnTransmissionProcessor(Map map, ContagionMapDeveloperDiagnosticsController developerDiagnosticsController)
    {
        _map = map;
        _developerDiagnosticsController = developerDiagnosticsController;
    }

    public void RunPawnTransmissionPass(IReadOnlyList<Pawn> spawnedPawns, IReadOnlyList<Pawn> sourcePawns)
    {
        List<TransmissionSource> sources = GatherTransmissionSources(sourcePawns);
        if (sources.Count == 0)
        {
            return;
        }

        _spatialIndex.Build(spawnedPawns);
        BuildRoomPawnIndex(spawnedPawns);

        for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            TransmissionSource source = sources[sourceIndex];
            float maxRange = GetMaxVectorRange(source.ResolvedProfile.Profile);
            float maxRoomAirRange = GetMaxRoomAirRange(source.ResolvedProfile.Profile);
            float maxPathRange = GetMaxProximityVectorRange(source.ResolvedProfile.Profile);
            Dictionary<int, float> pathDistanceByCell = null;
            if (maxPathRange > 0f)
            {
                ContagionTransmissionUtility.CollectReachablePathDistances(
                    _map,
                    source.Pawn.Position,
                    maxPathRange,
                    _pathDistanceByCell,
                    _pathOpenCells);
                pathDistanceByCell = _pathDistanceByCell;
            }

            _rangeQueryBuffer.Clear();
            CollectTransmissionCandidates(source.Pawn, source.ResolvedProfile.Profile, maxRange, maxRoomAirRange, _rangeQueryBuffer);

            for (int targetIndex = 0; targetIndex < _rangeQueryBuffer.Count; targetIndex++)
            {
                Pawn targetPawn = _rangeQueryBuffer[targetIndex];
                if (targetPawn == source.Pawn)
                {
                    continue;
                }

                TryTransmit(source, targetPawn, pathDistanceByCell);
            }
        }
    }

    private static List<TransmissionSource> GatherTransmissionSources(IReadOnlyList<Pawn> spawnedPawns)
    {
        List<TransmissionSource> sources = new List<TransmissionSource>();

        for (int pawnIndex = 0; pawnIndex < spawnedPawns.Count; pawnIndex++)
        {
            Pawn pawn = spawnedPawns[pawnIndex];
            if (pawn == null
                || pawn.Dead
                || !pawn.Spawned
                || pawn.health?.hediffSet == null)
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

    private void BuildRoomPawnIndex(IReadOnlyList<Pawn> spawnedPawns)
    {
        foreach (List<Pawn> pawns in _pawnsByRoom.Values)
        {
            pawns.Clear();
        }

        _pawnsByRoom.Clear();
        for (int i = 0; i < spawnedPawns.Count; i++)
        {
            Pawn pawn = spawnedPawns[i];
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map != _map)
            {
                continue;
            }

            Room room = pawn.Position.GetRoom(_map);
            if (room == null || ContagionTransmissionUtility.IsOutdoors(room))
            {
                continue;
            }

            if (!_pawnsByRoom.TryGetValue(room, out List<Pawn> pawns))
            {
                pawns = new List<Pawn>();
                _pawnsByRoom[room] = pawns;
            }

            pawns.Add(pawn);
        }
    }

    private void CollectTransmissionCandidates(Pawn sourcePawn, TransmissionProfile profile, float maxRange, float maxRoomAirRange, List<Pawn> result)
    {
        _candidatePawnSet.Clear();
        if (maxRange > 0f)
        {
            _spatialIndex.CollectPawnsInRange(sourcePawn.Position, maxRange, result);
            for (int i = 0; i < result.Count; i++)
            {
                _candidatePawnSet.Add(result[i]);
            }
        }

        if (maxRoomAirRange <= 0f || !HasRoomAirborne(profile))
        {
            return;
        }

        Room sourceRoom = sourcePawn.Position.GetRoom(_map);
        if (sourceRoom == null
            || ContagionTransmissionUtility.IsOutdoors(sourceRoom)
            || !_pawnsByRoom.TryGetValue(sourceRoom, out List<Pawn> roomPawns))
        {
            return;
        }

        for (int i = 0; i < roomPawns.Count; i++)
        {
            Pawn roomPawn = roomPawns[i];
            if (sourcePawn.Position.InHorDistOf(roomPawn.Position, maxRoomAirRange)
                && _candidatePawnSet.Add(roomPawn))
            {
                result.Add(roomPawn);
            }
        }
    }

    private static float GetMaxVectorRange(TransmissionProfile profile)
    {
        float maxRange = 0f;
        if (profile.vectors == null)
        {
            return maxRange;
        }

        for (int i = 0; i < profile.vectors.Count; i++)
        {
            if (profile.vectors[i] is Vector_Airborne airborne)
            {
                maxRange = Mathf.Max(maxRange, airborne.maxRange);
            }
            else if (profile.vectors[i] is Vector_Proximity proximity)
            {
                maxRange = Mathf.Max(maxRange, proximity.maxRange);
            }
        }

        return maxRange;
    }

    private static bool HasRoomAirborne(TransmissionProfile profile)
    {
        if (profile?.vectors == null)
        {
            return false;
        }

        for (int i = 0; i < profile.vectors.Count; i++)
        {
            if (profile.vectors[i] is Vector_Airborne airborne
                && airborne.roomAirBaseChanceFactor > 0f
                && airborne.roomAirMaxRange > 0
                && airborne.roomAirMaxCells > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static float GetMaxRoomAirRange(TransmissionProfile profile)
    {
        float maxRange = 0f;
        if (profile?.vectors == null)
        {
            return maxRange;
        }

        for (int i = 0; i < profile.vectors.Count; i++)
        {
            if (profile.vectors[i] is Vector_Airborne airborne
                && airborne.roomAirBaseChanceFactor > 0f
                && airborne.roomAirMaxRange > 0
                && airborne.roomAirMaxCells > 0)
            {
                maxRange = Mathf.Max(maxRange, airborne.roomAirMaxRange);
            }
        }

        return maxRange;
    }

    private static float GetMaxProximityVectorRange(TransmissionProfile profile)
    {
        float maxRange = 0f;
        if (profile.vectors == null)
        {
            return maxRange;
        }

        for (int i = 0; i < profile.vectors.Count; i++)
        {
            if (profile.vectors[i] is Vector_Proximity proximity)
            {
                maxRange = Mathf.Max(maxRange, proximity.maxRange);
            }
        }

        return maxRange;
    }

    private bool TryTransmit(TransmissionSource source, Pawn targetPawn, Dictionary<int, float> pathDistanceByCell)
    {
        if (targetPawn == null || targetPawn.Dead || !targetPawn.Spawned || targetPawn.Map != _map)
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

            if (vector is Vector_Proximity proximity && TryTransmitProximity(source, targetPawn, proximity, transmissionMultiplier, pathDistanceByCell))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryTransmitAirborne(
        TransmissionSource source,
        Pawn targetPawn,
        Vector_Airborne vector,
        float transmissionMultiplier)
    {
        if (TryTransmitAirborneDirect(source, targetPawn, vector, transmissionMultiplier))
        {
            return true;
        }

        return TryTransmitAirborneRoom(source, targetPawn, vector, transmissionMultiplier);
    }

    private bool TryTransmitAirborneDirect(
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
        float distance = ContagionTransmissionUtility.GetHorizontalDistance(source.Pawn.Position, targetPawn.Position);
        bool sourceRoofed = _map.roofGrid.Roofed(source.Pawn.Position);
        bool targetRoofed = _map.roofGrid.Roofed(targetPawn.Position);
        bool hasLineOfSight = GenSight.LineOfSight(source.Pawn.Position, targetPawn.Position, _map);
        float enclosureFactor = sourceRoofed && targetRoofed ? 1f : vector.outdoorFactor;
        float obstructionFactor = hasLineOfSight ? 1f : vector.obstructedFactor;
        float maskFactor = ContagionApparelProtectionUtility.GetRespiratoryMaskFactor(source.Pawn, targetPawn, vector);
        float suppressionFactor = ContagionTransmissionUtility.GetSpreadSuppressionFactor(_map, source.ResolvedProfile, targetPawn);
        if (!ContagionDeveloperDiagnosticsUtility.TryBuildAirborneBreakdown(
            source.Pawn,
            targetPawn,
            source.ResolvedProfile,
            vector,
            _map,
            transmissionMultiplier,
            distance,
            ContagionTransmissionUtility.GetDistanceFactor(distance, vector.distanceFalloffRate),
            enclosureFactor,
            obstructionFactor,
            maskFactor,
            suppressionFactor,
            out ContagionSpreadBreakdown breakdown))
        {
            return false;
        }

        return RollSeedAndLog(source, targetPawn, breakdown, ContagionDiagnosticCounter.AirborneSeeded, ContagionDebugVectorKind.Airborne);
    }

    private bool TryTransmitAirborneRoom(
        TransmissionSource source,
        Pawn targetPawn,
        Vector_Airborne vector,
        float transmissionMultiplier)
    {
        if (!ContagionTransmissionUtility.TryGetRoomAirFactor(
            _map,
            source.Pawn.Position,
            targetPawn.Position,
            vector,
            out float effectiveRoomDistance,
            out float roomAirFactor))
        {
            return false;
        }

        ContagionDiagnostics.Record(ContagionDiagnosticCounter.AirborneAttempted);
        float maskFactor = ContagionApparelProtectionUtility.GetRespiratoryMaskFactor(source.Pawn, targetPawn, vector);
        float suppressionFactor = ContagionTransmissionUtility.GetSpreadSuppressionFactor(_map, source.ResolvedProfile, targetPawn);
        if (!ContagionDeveloperDiagnosticsUtility.TryBuildAirborneRoomBreakdown(
            source.Pawn,
            targetPawn,
            source.ResolvedProfile,
            vector,
            _map,
            transmissionMultiplier,
            effectiveRoomDistance,
            roomAirFactor,
            maskFactor,
            suppressionFactor,
            out ContagionSpreadBreakdown breakdown))
        {
            return false;
        }

        return RollSeedAndLog(source, targetPawn, breakdown, ContagionDiagnosticCounter.AirborneSeeded, ContagionDebugVectorKind.AirborneRoom);
    }

    private bool TryTransmitProximity(
        TransmissionSource source,
        Pawn targetPawn,
        Vector_Proximity vector,
        float transmissionMultiplier,
        Dictionary<int, float> pathDistanceByCell)
    {
        if (pathDistanceByCell == null
            || !pathDistanceByCell.TryGetValue(_map.cellIndices.CellToIndex(targetPawn.Position), out float pathDistance)
            || pathDistance > vector.maxRange)
        {
            return false;
        }

        ContagionDiagnostics.Record(ContagionDiagnosticCounter.ProximityAttempted);
        Room sourceRoom = source.Pawn.Position.GetRoom(_map);
        Room targetRoom = targetPawn.Position.GetRoom(_map);
        float outdoorFactor = ContagionTransmissionUtility.IsOutdoors(sourceRoom) || ContagionTransmissionUtility.IsOutdoors(targetRoom)
            ? vector.outdoorFactor
            : 1f;
        float cleanlinessFactor = ContagionTransmissionUtility.GetLocalCleanlinessFactor(
            targetPawn.Position, targetRoom, _map, vector.cleanlinessImpact, vector.outdoorFilthRadius);
        float maskFactor = ContagionApparelProtectionUtility.GetRespiratoryMaskFactor(source.Pawn, targetPawn, vector);
        float suppressionFactor = ContagionTransmissionUtility.GetSpreadSuppressionFactor(_map, source.ResolvedProfile, targetPawn);
        if (!ContagionDeveloperDiagnosticsUtility.TryBuildProximityBreakdown(
            source.Pawn,
            targetPawn,
            source.ResolvedProfile,
            vector,
            _map,
            transmissionMultiplier,
            pathDistance,
            ContagionTransmissionUtility.GetDistanceFactor(pathDistance, vector.distanceFalloffRate),
            outdoorFactor,
            cleanlinessFactor,
            maskFactor,
            suppressionFactor,
            out ContagionSpreadBreakdown breakdown))
        {
            return false;
        }

        return RollSeedAndLog(source, targetPawn, breakdown, ContagionDiagnosticCounter.ProximitySeeded, ContagionDebugVectorKind.Proximity);
    }

    // Shared tail for the three pawn-to-pawn vectors (airborne direct, airborne room, proximity):
    // roll the breakdown's final chance, seed incubation on success, record the per-vector counter
    // and developer trace, and always log the roll. Keeps the seed protocol in one place.
    private bool RollSeedAndLog(
        TransmissionSource source,
        Pawn targetPawn,
        ContagionSpreadBreakdown breakdown,
        ContagionDiagnosticCounter seededCounter,
        ContagionDebugVectorKind traceKind)
    {
        bool seeded = false;
        if (Rand.Chance(Mathf.Clamp01(breakdown.FinalChance)))
        {
            seeded = ContagionDiseaseUtility.TrySeedIncubation(
                targetPawn,
                source.ResolvedProfile.ResolveHediffForPawn(targetPawn),
                source.ResolvedProfile.PartsToAffect,
                source.Pawn,
                ContagionDiagnosticOrigin.Spread,
                ContagionSeedSource.Contact,
                out HediffDef _);
            if (seeded)
            {
                ContagionDiagnostics.Record(seededCounter);
                _developerDiagnosticsController.RecordTransmissionTrace(source.Pawn, targetPawn, source.ResolvedProfile.DiseaseDef, traceKind);
            }
        }

        ContagionDiagnostics.LogRoll(source.Pawn, targetPawn, breakdown, seeded);
        return seeded;
    }

    private sealed class SpatialPawnIndex
    {
        private const int BucketSize = 16;

        private readonly Dictionary<(int, int), List<Pawn>> _buckets = new Dictionary<(int, int), List<Pawn>>();

        public void Build(IReadOnlyList<Pawn> pawns)
        {
            _buckets.Clear();
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || !pawn.Spawned)
                {
                    continue;
                }

                (int, int) bucket = (pawn.Position.x / BucketSize, pawn.Position.z / BucketSize);
                if (!_buckets.TryGetValue(bucket, out List<Pawn> list))
                {
                    _buckets[bucket] = list = new List<Pawn>(4);
                }

                list.Add(pawn);
            }
        }

        public void CollectPawnsInRange(IntVec3 center, float range, List<Pawn> result)
        {
            int searchRadius = (int)(range / BucketSize) + 1;
            int cx = center.x / BucketSize;
            int cz = center.z / BucketSize;

            for (int bx = cx - searchRadius; bx <= cx + searchRadius; bx++)
            {
                for (int bz = cz - searchRadius; bz <= cz + searchRadius; bz++)
                {
                    if (_buckets.TryGetValue((bx, bz), out List<Pawn> bucket))
                    {
                        for (int i = 0; i < bucket.Count; i++)
                        {
                            result.Add(bucket[i]);
                        }
                    }
                }
            }
        }
    }
}
