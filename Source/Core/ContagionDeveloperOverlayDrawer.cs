using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

public static class ContagionDeveloperOverlayDrawer
{
    private const int MaxInstancedBatchSize = 1023;

    private const int ValueBandCount = 16;

    private const float MinVisibleNominalChance = 0.00005f;

    private const float MinCleanlinessFactor = 0.1f;

    private const float MaxCleanlinessFactor = 3f;

    private static readonly Color HoverLineColor = new Color(0.15f, 0.92f, 1f, 0.82f);

    private static readonly Color TraceAirborneColor = new Color(0.18f, 0.82f, 1f, 0.72f);

    private static readonly Color TraceProximityColor = new Color(1f, 0.74f, 0.16f, 0.72f);

    private static readonly Color TraceSocialColor = new Color(0.86f, 0.38f, 1f, 0.72f);

    private static readonly Color[] ValueBands = BuildValueBands();

    private static readonly Dictionary<int, Material> MaterialCache = new Dictionary<int, Material>();

    private static readonly Matrix4x4[] MatrixBatchBuffer = new Matrix4x4[MaxInstancedBatchSize];

    private static readonly List<Matrix4x4> CellMatrices = new List<Matrix4x4>(MaxInstancedBatchSize);

    private static readonly Dictionary<int, List<IntVec3>> FillOverlayBuckets = new Dictionary<int, List<IntVec3>>();

    private static readonly Dictionary<int, Color> FillOverlayBucketColors = new Dictionary<int, Color>();

    public static void DrawHoverLine(Pawn sourcePawn, Pawn targetPawn)
    {
        if (sourcePawn == null || targetPawn == null)
        {
            return;
        }

        Material material = GetMaterial(HoverLineColor);
        Vector3 origin = GetPawnLinePosition(sourcePawn);
        Vector3 end = GetPawnLinePosition(targetPawn);
        GenDraw.DrawLineBetween(origin, end, material, 0.055f);
        DrawMarker(end, HoverLineColor, 0.22f, 0.02f);
    }

    public static void DrawTransmissionTraces(Map map, IReadOnlyList<ContagionTransmissionTrace> traces)
    {
        if (map == null || traces == null || traces.Count == 0)
        {
            return;
        }

        for (int i = 0; i < traces.Count; i++)
        {
            ContagionTransmissionTrace trace = traces[i];
            if (trace?.SourcePawn == null || trace.TargetPawn == null)
            {
                continue;
            }

            Color color = GetTraceColor(trace.VectorKind);
            Material material = GetMaterial(color);
            Vector3 origin = GetPawnLinePosition(trace.SourcePawn);
            Vector3 end = GetPawnLinePosition(trace.TargetPawn);
            GenDraw.DrawLineBetween(origin, end, material, 0.045f);
            DrawDirectionMarker(origin, end, color);
        }
    }

    public static void DrawNominalSpreadOverlay(Pawn sourcePawn)
    {
        if (sourcePawn?.Map == null || !sourcePawn.Spawned || sourcePawn.Dead)
        {
            return;
        }

        Map map = sourcePawn.Map;
        List<ResolvedTransmissionProfile> contagiousProfiles = new List<ResolvedTransmissionProfile>();
        float maxRange = 0f;
        foreach (ResolvedTransmissionProfile resolvedProfile in ContagionDiseaseUtility.GetContagiousProfiles(sourcePawn))
        {
            contagiousProfiles.Add(resolvedProfile);

            if (ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Airborne airborne))
            {
                maxRange = Mathf.Max(maxRange, airborne.maxRange);
            }

            if (ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Proximity proximity))
            {
                maxRange = Mathf.Max(maxRange, proximity.maxRange);
            }
        }

        if (contagiousProfiles.Count == 0 || maxRange <= 0f)
        {
            return;
        }

        foreach (List<IntVec3> bucket in FillOverlayBuckets.Values)
        {
            bucket.Clear();
        }

        FillOverlayBucketColors.Clear();
        Dictionary<int, float> chanceByCell = new Dictionary<int, float>();
        float strongestChance = 0f;
        bool sourceRoofed = map.roofGrid.Roofed(sourcePawn.Position);
        Room sourceRoom = sourcePawn.Position.GetRoom(map);
        float settingsMultiplier = Contagion_Mod.Settings?.EffectiveTransmissionMultiplier ?? 1f;
        foreach (IntVec3 cell in GenRadial.RadialCellsAround(sourcePawn.Position, maxRange, useCenter: true))
        {
            if (!ShouldDrawCell(map, cell) || cell == sourcePawn.Position)
            {
                continue;
            }

            float aggregateChance = 0f;
            for (int profileIndex = 0; profileIndex < contagiousProfiles.Count; profileIndex++)
            {
                ResolvedTransmissionProfile resolvedProfile = contagiousProfiles[profileIndex];
                if (ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Airborne airborne)
                    && sourcePawn.Position.InHorDistOf(cell, airborne.maxRange))
                {
                    float distance = GetHorizontalDistance(sourcePawn.Position, cell);
                    bool targetRoofed = map.roofGrid.Roofed(cell);
                    float distanceFactor = GetDistanceFactor(distance, airborne.distanceFalloffRate);
                    float enclosureFactor = sourceRoofed && targetRoofed ? 1f : airborne.outdoorFactor;
                    float obstructionFactor = GenSight.LineOfSight(sourcePawn.Position, cell, map) ? 1f : airborne.obstructedFactor;
                    ContagionDeveloperDiagnosticsUtility.TryBuildNominalAirborneBreakdown(
                        sourcePawn,
                        resolvedProfile,
                        airborne,
                        map,
                        settingsMultiplier,
                        distance,
                        distanceFactor,
                        enclosureFactor,
                        obstructionFactor,
                        out ContagionSpreadBreakdown airborneBreakdown);
                    if (airborneBreakdown != null)
                    {
                        aggregateChance = CombineChance(aggregateChance, airborneBreakdown.FinalChance);
                    }
                }

                if (ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Proximity proximity)
                    && sourcePawn.Position.InHorDistOf(cell, proximity.maxRange))
                {
                    float distance = GetHorizontalDistance(sourcePawn.Position, cell);
                    Room targetRoom = cell.GetRoom(map);
                    float distanceFactor = GetDistanceFactor(distance, proximity.distanceFalloffRate);
                    float outdoorFactor = IsOutdoors(sourceRoom) || IsOutdoors(targetRoom) ? proximity.outdoorFactor : 1f;
                    float cleanlinessFactor = GetLocalCleanlinessFactor(map, cell, targetRoom, proximity.cleanlinessImpact, proximity.outdoorFilthRadius);
                    ContagionDeveloperDiagnosticsUtility.TryBuildNominalProximityBreakdown(
                        sourcePawn,
                        resolvedProfile,
                        proximity,
                        map,
                        settingsMultiplier,
                        distance,
                        distanceFactor,
                        outdoorFactor,
                        cleanlinessFactor,
                        out ContagionSpreadBreakdown proximityBreakdown);
                    if (proximityBreakdown != null)
                    {
                        aggregateChance = CombineChance(aggregateChance, proximityBreakdown.FinalChance);
                    }
                }
            }

            if (aggregateChance <= 0f)
            {
                continue;
            }

            if (aggregateChance < MinVisibleNominalChance)
            {
                continue;
            }

            int cellIndex = map.cellIndices.CellToIndex(cell);
            chanceByCell[cellIndex] = aggregateChance;
            strongestChance = Mathf.Max(strongestChance, aggregateChance);
        }

        if (strongestChance <= 0f || chanceByCell.Count == 0)
        {
            return;
        }

        foreach (KeyValuePair<int, float> chanceEntry in chanceByCell)
        {
            float normalizedChance = GetDisplayStrength(chanceEntry.Value, strongestChance);
            if (normalizedChance <= 0f)
            {
                continue;
            }

            IntVec3 cell = CellIndicesUtility.IndexToCell(chanceEntry.Key, map.Size.x);
            Color color = ValueBands[GetValueBand(normalizedChance)];
            int colorKey = PackColor(color);
            if (!FillOverlayBuckets.TryGetValue(colorKey, out List<IntVec3> bucket))
            {
                bucket = new List<IntVec3>();
                FillOverlayBuckets[colorKey] = bucket;
            }

            FillOverlayBucketColors[colorKey] = color;

            bucket.Add(cell);
        }

        foreach (KeyValuePair<int, List<IntVec3>> bucket in FillOverlayBuckets)
        {
            if (bucket.Value.Count > 0)
            {
                DrawFilledCells(bucket.Value, FillOverlayBucketColors[bucket.Key]);
            }
        }
    }

    private static float CombineChance(float existingChance, float additionalChance)
    {
        float existing = Mathf.Clamp01(existingChance);
        float additional = Mathf.Clamp01(additionalChance);
        return 1f - (1f - existing) * (1f - additional);
    }

    private static Vector3 GetPawnLinePosition(Pawn pawn)
    {
        Vector3 position = pawn.DrawPos;
        position.y = AltitudeLayer.MetaOverlays.AltitudeFor() + 0.1f;
        return position;
    }

    private static void DrawDirectionMarker(Vector3 origin, Vector3 end, Color color)
    {
        Vector3 markerPosition = Vector3.Lerp(origin, end, 0.82f);
        DrawMarker(markerPosition, color, 0.18f, 0.03f);
    }

    private static void DrawMarker(Vector3 position, Color color, float scale, float heightOffset)
    {
        position.y += heightOffset;
        Graphics.DrawMesh(
            MeshPool.plane10,
            Matrix4x4.TRS(position, Quaternion.identity, new Vector3(scale, 1f, scale)),
            GetMaterial(color),
            0);
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

    private static float GetDisplayStrength(float chance, float strongestChance)
    {
        if (chance <= 0f || strongestChance <= 0f)
        {
            return 0f;
        }

        float normalizedChance = Mathf.Clamp01(chance / strongestChance);
        return Mathf.Pow(normalizedChance, 0.65f);
    }

    private static bool IsOutdoors(Room room)
    {
        return room == null || room.PsychologicallyOutdoors;
    }

    private static float GetLocalCleanlinessFactor(Map map, IntVec3 position, Room room, float cleanlinessImpact, int outdoorFilthRadius)
    {
        if (cleanlinessImpact <= 0f)
        {
            return 1f;
        }

        if (room == null || room.PsychologicallyOutdoors)
        {
            return GetOutdoorFilthCleanlinessFactor(map, position, cleanlinessImpact, outdoorFilthRadius);
        }

        float cleanliness = room.GetStat(RoomStatDefOf.Cleanliness);
        return Mathf.Clamp(1f - cleanliness * cleanlinessImpact, MinCleanlinessFactor, MaxCleanlinessFactor);
    }

    private static float GetOutdoorFilthCleanlinessFactor(Map map, IntVec3 center, float cleanlinessImpact, int outdoorFilthRadius)
    {
        if (map == null || outdoorFilthRadius <= 0)
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

    private static Color GetTraceColor(ContagionDebugVectorKind vectorKind)
    {
        return vectorKind switch
        {
            ContagionDebugVectorKind.Airborne => TraceAirborneColor,
            ContagionDebugVectorKind.Proximity => TraceProximityColor,
            _ => TraceSocialColor
        };
    }

    private static Color[] BuildValueBands()
    {
        Color low = new Color(0.94f, 0.46f, 0.16f, 0.10f);
        Color lowerMid = new Color(1.00f, 0.70f, 0.12f, 0.22f);
        Color upperMid = new Color(0.98f, 0.92f, 0.18f, 0.42f);
        Color high = new Color(0.20f, 0.96f, 0.24f, 0.60f);
        Color[] bands = new Color[ValueBandCount];

        for (int i = 0; i < bands.Length; i++)
        {
            float t = bands.Length <= 1 ? 1f : i / (float)(bands.Length - 1);
            if (t < 0.33f)
            {
                bands[i] = Color.Lerp(low, lowerMid, t / 0.33f);
            }
            else if (t < 0.66f)
            {
                bands[i] = Color.Lerp(lowerMid, upperMid, (t - 0.33f) / 0.33f);
            }
            else
            {
                bands[i] = Color.Lerp(upperMid, high, (t - 0.66f) / 0.34f);
            }
        }

        return bands;
    }

    private static int GetValueBand(float value)
    {
        return Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(value) * ValueBandCount), 0, ValueBandCount - 1);
    }

    private static int PackColor(Color color)
    {
        int r = Mathf.RoundToInt(Mathf.Clamp01(color.r) * 255f);
        int g = Mathf.RoundToInt(Mathf.Clamp01(color.g) * 255f);
        int b = Mathf.RoundToInt(Mathf.Clamp01(color.b) * 255f);
        int a = Mathf.RoundToInt(Mathf.Clamp01(color.a) * 255f);
        return (r << 24) ^ (g << 16) ^ (b << 8) ^ a;
    }

    private static void DrawFilledCells(IEnumerable<IntVec3> cells, Color color)
    {
        Material material = GetMaterial(color);
        CellMatrices.Clear();
        Map map = Find.CurrentMap;

        foreach (IntVec3 cell in cells)
        {
            if (map != null && !ShouldDrawCell(map, cell))
            {
                continue;
            }

            Vector3 position = cell.ToVector3ShiftedWithAltitude(AltitudeLayer.MapDataOverlay);
            CellMatrices.Add(Matrix4x4.TRS(position, Quaternion.identity, Vector3.one));

            if (CellMatrices.Count == MaxInstancedBatchSize)
            {
                FlushCellBatch(material);
            }
        }

        FlushCellBatch(material);
    }

    private static void FlushCellBatch(Material material)
    {
        if (CellMatrices.Count == 0)
        {
            return;
        }

        int count = CellMatrices.Count;
        for (int i = 0; i < count; i++)
        {
            MatrixBatchBuffer[i] = CellMatrices[i];
        }

        Graphics.DrawMeshInstanced(MeshPool.plane10, 0, material, MatrixBatchBuffer, count);
        CellMatrices.Clear();
    }

    private static bool ShouldDrawCell(Map map, IntVec3 cell)
    {
        if (map == null || !cell.InBounds(map) || cell.Fogged(map))
        {
            return false;
        }

        CellRect viewRect = CellRect.ViewRect(map);
        if (!viewRect.IsEmpty)
        {
            const int ViewMargin = 2;
            if (cell.x < viewRect.minX - ViewMargin
                || cell.x > viewRect.maxX + ViewMargin
                || cell.z < viewRect.minZ - ViewMargin
                || cell.z > viewRect.maxZ + ViewMargin)
            {
                return false;
            }
        }

        return true;
    }

    private static Material GetMaterial(Color color)
    {
        int key = PackColor(color);
        if (!MaterialCache.TryGetValue(key, out Material material))
        {
            material = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.MetaOverlay, color);
            material.enableInstancing = true;
            MaterialCache[key] = material;
        }

        return material;
    }
}