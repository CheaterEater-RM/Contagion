using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

internal sealed class ContagionPawnTransmissionProcessor
{
    private const float MinCleanlinessFactor = 0.1f;

    private const float MaxCleanlinessFactor = 3f;

    private readonly Map _map;

    private readonly ContagionMapDeveloperDiagnosticsController _developerDiagnosticsController;

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

    public ContagionPawnTransmissionProcessor(Map map, ContagionMapDeveloperDiagnosticsController developerDiagnosticsController)
    {
        _map = map;
        _developerDiagnosticsController = developerDiagnosticsController;
    }

    public void RunPawnTransmissionPass(IReadOnlyList<Pawn> spawnedPawns)
    {
        List<TransmissionSource> sources = GatherTransmissionSources(spawnedPawns);
        if (sources.Count == 0)
        {
            return;
        }

        Dictionary<HediffDef, float> suppressionByDisease = new Dictionary<HediffDef, float>();
        for (int i = 0; i < sources.Count; i++)
        {
            TransmissionSource source = sources[i];
            HediffDef diseaseDef = source.ResolvedProfile.DiseaseDef;
            if (!suppressionByDisease.TryGetValue(diseaseDef, out float suppression))
            {
                suppression = ContagionTransmissionUtility.GetSpreadSuppressionFactor(_map, source.ResolvedProfile);
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

            if (vector is Vector_Proximity proximity && TryTransmitProximity(source, targetPawn, proximity, transmissionMultiplier))
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
        if (!source.Pawn.Position.InHorDistOf(targetPawn.Position, vector.maxRange))
        {
            return false;
        }

        ContagionDiagnostics.Record(ContagionDiagnosticCounter.AirborneAttempted);
        float distance = GetHorizontalDistance(source.Pawn.Position, targetPawn.Position);
        bool sourceRoofed = _map.roofGrid.Roofed(source.Pawn.Position);
        bool targetRoofed = _map.roofGrid.Roofed(targetPawn.Position);
        bool hasLineOfSight = GenSight.LineOfSight(source.Pawn.Position, targetPawn.Position, _map);
        float enclosureFactor = sourceRoofed && targetRoofed ? 1f : vector.outdoorFactor;
        float obstructionFactor = hasLineOfSight ? 1f : vector.obstructedFactor;
        float maskFactor = ContagionMaskUtility.GetRespiratoryMaskFactor(source.Pawn, targetPawn, vector);
        float suppressionFactor = ContagionTransmissionUtility.IsSuppressionTarget(targetPawn) ? source.SuppressionFactor : 1f;
        if (!ContagionDeveloperDiagnosticsUtility.TryBuildAirborneBreakdown(
            source.Pawn,
            targetPawn,
            source.ResolvedProfile,
            vector,
            _map,
            transmissionMultiplier,
            distance,
            GetDistanceFactor(distance, vector.distanceFalloffRate),
            enclosureFactor,
            obstructionFactor,
            maskFactor,
            suppressionFactor,
            out ContagionSpreadBreakdown breakdown))
        {
            return false;
        }

        if (!Rand.Chance(Mathf.Clamp01(breakdown.FinalChance)))
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
            _developerDiagnosticsController.RecordTransmissionTrace(source.Pawn, targetPawn, source.ResolvedProfile.DiseaseDef, ContagionDebugVectorKind.Airborne);
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
        Room sourceRoom = source.Pawn.Position.GetRoom(_map);
        Room targetRoom = targetPawn.Position.GetRoom(_map);
        float outdoorFactor = IsOutdoors(sourceRoom) || IsOutdoors(targetRoom) ? vector.outdoorFactor : 1f;
        float cleanlinessFactor = GetLocalCleanlinessFactor(targetPawn.Position, targetRoom, vector.cleanlinessImpact, vector.outdoorFilthRadius);
        float maskFactor = ContagionMaskUtility.GetRespiratoryMaskFactor(source.Pawn, targetPawn, vector);
        float suppressionFactor = ContagionTransmissionUtility.IsSuppressionTarget(targetPawn) ? source.SuppressionFactor : 1f;
        if (!ContagionDeveloperDiagnosticsUtility.TryBuildProximityBreakdown(
            source.Pawn,
            targetPawn,
            source.ResolvedProfile,
            vector,
            _map,
            transmissionMultiplier,
            distance,
            GetDistanceFactor(distance, vector.distanceFalloffRate),
            outdoorFactor,
            cleanlinessFactor,
            maskFactor,
            suppressionFactor,
            out ContagionSpreadBreakdown breakdown))
        {
            return false;
        }

        if (!Rand.Chance(Mathf.Clamp01(breakdown.FinalChance)))
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
            _developerDiagnosticsController.RecordTransmissionTrace(source.Pawn, targetPawn, source.ResolvedProfile.DiseaseDef, ContagionDebugVectorKind.Proximity);
        }

        return seeded;
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
                if (!candidate.InBounds(_map) || !center.InHorDistOf(candidate, outdoorFilthRadius))
                {
                    continue;
                }

                List<Thing> things = candidate.GetThingList(_map);
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
}