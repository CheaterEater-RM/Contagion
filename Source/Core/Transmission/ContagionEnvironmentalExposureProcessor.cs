using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

internal sealed class ContagionEnvironmentalExposureProcessor
{
    private const float MaxEnvironmentalWaterFactor = 3f;

    private readonly Contagion_MapTransmissionComponent _owner;

    private readonly Map _map;

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

    public ContagionEnvironmentalExposureProcessor(Contagion_MapTransmissionComponent owner)
    {
        _owner = owner;
        _map = owner?.Map;
    }

    public void RunEnvironmentalExposurePass(IReadOnlyList<Pawn> spawnedPawns)
    {
        List<EnvironmentalProfile> environmentalProfiles = GatherEnvironmentalProfiles();
        if (environmentalProfiles.Count == 0)
        {
            return;
        }

        float transmissionMultiplier = Contagion_Mod.Settings?.EffectiveTransmissionMultiplier ?? 1f;
        Dictionary<IntVec3, int> roofEdgeCache = new Dictionary<IntVec3, int>();
        Dictionary<Vector_Environmental, Dictionary<IntVec3, float>> waterProximityCache =
            new Dictionary<Vector_Environmental, Dictionary<IntVec3, float>>();

        for (int pawnIndex = 0; pawnIndex < spawnedPawns.Count; pawnIndex++)
        {
            Pawn pawn = spawnedPawns[pawnIndex];
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map != _map)
            {
                continue;
            }

            for (int profileIndex = 0; profileIndex < environmentalProfiles.Count; profileIndex++)
            {
                if (TryApplyEnvironmentalExposure(pawn, environmentalProfiles[profileIndex], transmissionMultiplier, roofEdgeCache, waterProximityCache))
                {
                    break;
                }
            }
        }
    }

    private bool TryApplyEnvironmentalExposure(
        Pawn pawn,
        EnvironmentalProfile environmentalProfile,
        float transmissionMultiplier,
        Dictionary<IntVec3, int> roofEdgeCache,
        Dictionary<Vector_Environmental, Dictionary<IntVec3, float>> waterProximityCache)
    {
        if (!ContagionSeedingCoordinator.TryGetEnvironmentalSeedingContext(
            _owner,
            environmentalProfile.ResolvedProfile,
            environmentalProfile.Seeder,
            out PendingDiseaseEvent windowEvent,
            out float seedingMultiplier))
        {
            return false;
        }

        ContagionDiagnostics.Record(ContagionDiagnosticCounter.EnvironmentalAttempted);
        Room room = pawn.Position.GetRoom(_map);
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

        chance *= GetEnvironmentalShelterFactor(pawn.Position, room, ambientTemperature, environmentalProfile.Vector, roofEdgeCache);
        chance *= GetWaterProximityFactor(pawn.Position, environmentalProfile.Vector, waterProximityCache);
        chance *= GetEnvironmentalPawnFactor(pawn, environmentalProfile.Vector);
        chance = ContagionTransmissionUtility.BuildSeederChance(
            chance,
            pawn,
            environmentalProfile.ResolvedProfile,
            _map,
            transmissionMultiplier,
            out HediffDef _);
        if (ContagionSeedingCoordinator.CurrentMode == ContagionSeedingMode.Contagion)
        {
            chance = _owner.DiseaseDirector.ClampChance(chance, enforceMinimum: false);
        }

        if (!Rand.Chance(Mathf.Clamp01(chance)))
        {
            return false;
        }

        bool seeded = ContagionDiseaseUtility.TrySeedIncubation(
            pawn,
            environmentalProfile.ResolvedProfile.DiseaseDef,
            environmentalProfile.ResolvedProfile.PartsToAffect,
            ContagionDiagnosticOrigin.Incidence,
            ContagionSeedSource.Environmental,
            out HediffDef _);
        if (seeded)
        {
            ContagionSeedingCoordinator.NotifyEnvironmentalSeeded(_owner, environmentalProfile.ResolvedProfile, environmentalProfile.Seeder, windowEvent);
            ContagionDiagnostics.Record(ContagionDiagnosticCounter.EnvironmentalSeeded);
            ContagionDiagnostics.Trace($"Environmental transmission: {environmentalProfile.ResolvedProfile.DiseaseDef.defName} on {pawn.LabelShortCap}.");
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
        if (resolvedProfile?.LinkedIncidentDef == null || _map?.Biome == null)
        {
            return 0f;
        }

        return Mathf.Max(0f, _map.Biome.CommonalityOfDisease(resolvedProfile.LinkedIncidentDef));
    }

    private static float GetEnvironmentalPawnFactor(Pawn pawn, Vector_Environmental vector)
    {
        if (pawn?.RaceProps?.Humanlike == true)
        {
            return Mathf.Max(0f, vector.humanExposureFactor);
        }

        return 1f;
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

    private float GetAmbientTemperature(Room room)
    {
        if (room == null || room.UsesOutdoorTemperature)
        {
            return _map.mapTemperature.OutdoorTemp;
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

    private float GetEnvironmentalShelterFactor(
        IntVec3 position,
        Room room,
        float ambientTemperature,
        Vector_Environmental vector,
        Dictionary<IntVec3, int> roofEdgeCache)
    {
        if (room == null || room.UsesOutdoorTemperature || room.PsychologicallyOutdoors)
        {
            return 1f;
        }

        if (!roofEdgeCache.TryGetValue(position, out int cellsFromUnroofed))
        {
            cellsFromUnroofed = GetCellsFromUnroofed(position, 30);
            roofEdgeCache[position] = cellsFromUnroofed;
        }

        float shelterFactor = Mathf.Clamp01(1f - vector.indoorReductionPerCellFromEdge * Mathf.Max(1, cellsFromUnroofed));
        if (ambientTemperature < vector.coolRoomThreshold)
        {
            shelterFactor *= Mathf.InverseLerp(vector.minTemperature, vector.coolRoomThreshold, ambientTemperature);
        }

        return shelterFactor;
    }

    private int GetCellsFromUnroofed(IntVec3 center, int maxRadius)
    {
        if (!center.InBounds(_map) || !_map.roofGrid.Roofed(center))
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
                    if (candidate.InBounds(_map) && !_map.roofGrid.Roofed(candidate))
                    {
                        return radius;
                    }
                }
            }
        }

        return maxRadius;
    }

    private float GetWaterProximityFactor(
        IntVec3 center,
        Vector_Environmental vector,
        Dictionary<Vector_Environmental, Dictionary<IntVec3, float>> cache)
    {
        if (vector.waterProximityRadius <= 0 || vector.waterProximityWeight <= 0f)
        {
            return 1f;
        }

        if (!cache.TryGetValue(vector, out Dictionary<IntVec3, float> vectorCache))
        {
            vectorCache = new Dictionary<IntVec3, float>();
            cache[vector] = vectorCache;
        }

        if (vectorCache.TryGetValue(center, out float cached))
        {
            return cached;
        }

        int nearbyWaterCells = 0;
        int radius = vector.waterProximityRadius;
        for (int x = center.x - radius; x <= center.x + radius; x++)
        {
            for (int z = center.z - radius; z <= center.z + radius; z++)
            {
                IntVec3 candidate = new IntVec3(x, 0, z);
                if (!candidate.InBounds(_map) || !center.InHorDistOf(candidate, radius))
                {
                    continue;
                }

                TerrainDef terrain = _map.terrainGrid.BaseTerrainAt(candidate);
                if (terrain != null && terrain.IsWater)
                {
                    nearbyWaterCells++;
                }
            }
        }

        float result = Mathf.Clamp(1f + nearbyWaterCells * vector.waterProximityWeight, 1f, MaxEnvironmentalWaterFactor);
        vectorCache[center] = result;
        return result;
    }
}
