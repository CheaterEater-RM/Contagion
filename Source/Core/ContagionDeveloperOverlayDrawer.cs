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

    private static readonly Color HoverLineColor = new Color(0.15f, 0.92f, 1f, 0.82f);

    private static readonly Color TraceAirborneColor = new Color(0.18f, 0.82f, 1f, 0.72f);

    private static readonly Color TraceProximityColor = new Color(1f, 0.74f, 0.16f, 0.72f);

    private static readonly Color TraceSocialColor = new Color(0.86f, 0.38f, 1f, 0.72f);

    private static readonly Color TraceFoodborneColor = new Color(0.46f, 0.92f, 0.40f, 0.72f);

    private static readonly Color TraceCorpseFleaColor = new Color(0.74f, 0.55f, 0.26f, 0.72f);

    private static readonly Color TraceCorpseFluidColor = new Color(0.85f, 0.22f, 0.22f, 0.72f);

    private static readonly Color TraceCookingColor = new Color(0.98f, 0.86f, 0.30f, 0.72f);

    private static readonly Color TraceFomiteColor = new Color(0.95f, 0.40f, 0.74f, 0.72f);

    private static readonly Color TraceFecalOralEatingColor = new Color(0.62f, 0.86f, 0.30f, 0.72f);

    private static readonly Color TraceFecalOralLivingColor = new Color(0.58f, 0.42f, 0.22f, 0.72f);

    private static readonly Color TraceEnvironmentalColor = new Color(0.30f, 0.78f, 0.78f, 0.72f);

    private static readonly Color TraceDeveloperColor = new Color(1f, 0.45f, 0.18f, 0.72f);

    private static readonly Color TraceOffMapColor = new Color(0.92f, 0.92f, 0.98f, 0.72f);

    // Node glyph colors, keyed by anchor kind.
    private static readonly Color NodePawnColor = new Color(0.30f, 0.96f, 0.34f, 0.85f);

    private static readonly Color NodeCorpseColor = new Color(0.80f, 0.20f, 0.20f, 0.85f);

    private static readonly Color NodeBenchColor = new Color(1f, 0.62f, 0.16f, 0.85f);

    private static readonly Color NodeItemColor = new Color(0.98f, 0.90f, 0.30f, 0.85f);

    private static readonly Color NodeFilthColor = new Color(0.58f, 0.42f, 0.22f, 0.82f);

    private static readonly Color NodeGhostColor = new Color(0.70f, 0.70f, 0.74f, 0.65f);

    private static readonly Color NodeSourceColor = new Color(0.20f, 0.92f, 1f, 0.86f);

    // Always-on infected indicator colors (dev mode).
    private static readonly Color IndicatorPawnColor = new Color(0.95f, 0.30f, 0.30f, 0.80f);

    private static readonly Color IndicatorCorpseColor = new Color(0.62f, 0.16f, 0.62f, 0.80f);

    private static readonly Color IndicatorFoodColor = new Color(0.98f, 0.78f, 0.20f, 0.80f);

    private static readonly Color[] ValueBands = BuildValueBands();

    private static readonly Dictionary<int, Material> MaterialCache = new Dictionary<int, Material>();

    private static readonly Matrix4x4[] MatrixBatchBuffer = new Matrix4x4[MaxInstancedBatchSize];

    private static readonly List<Matrix4x4> CellMatrices = new List<Matrix4x4>(MaxInstancedBatchSize);

    private static readonly Dictionary<int, List<IntVec3>> FillOverlayBuckets = new Dictionary<int, List<IntVec3>>();

    private static readonly Dictionary<int, Color> FillOverlayBucketColors = new Dictionary<int, Color>();

    private static readonly Dictionary<int, float> PathDistanceByCell = new Dictionary<int, float>();

    private static readonly Queue<IntVec3> PathOpenCells = new Queue<IntVec3>();

    private static readonly Dictionary<int, float> EatingRiskByCell = new Dictionary<int, float>();

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

    // Renders the session-only infection trace graph: an edge line per transmission (colored by
    // vector) plus a glyph per node (live pawn / corpse / bench / item, or a hollow ghost circle
    // for a removed carrier so the chain doesn't visually vanish).
    public static void DrawTraceGraph(
        Map map,
        IReadOnlyList<ContagionTraceNode> nodes,
        IReadOnlyList<ContagionTraceEdge> edges)
    {
        if (map == null || nodes == null || nodes.Count == 0)
        {
            return;
        }

        Dictionary<int, ContagionTraceNode> byId = new Dictionary<int, ContagionTraceNode>(nodes.Count);
        for (int i = 0; i < nodes.Count; i++)
        {
            byId[nodes[i].Id] = nodes[i];
        }

        if (edges != null)
        {
            for (int i = 0; i < edges.Count; i++)
            {
                ContagionTraceEdge edge = edges[i];
                if (!byId.TryGetValue(edge.FromId, out ContagionTraceNode fromNode)
                    || !byId.TryGetValue(edge.ToId, out ContagionTraceNode toNode))
                {
                    continue;
                }

                Color color = GetTraceColor(edge.Vector);
                Material material = GetMaterial(color);
                Vector3 origin = LiftToOverlay(fromNode.DrawPosition);
                Vector3 end = LiftToOverlay(toNode.DrawPosition);
                GenDraw.DrawLineBetween(origin, end, material, 0.045f);
                DrawDirectionArrows(origin, end, color);
            }
        }

        for (int i = 0; i < nodes.Count; i++)
        {
            DrawTraceNode(nodes[i]);
        }
    }

    private static void DrawTraceNode(ContagionTraceNode node)
    {
        Vector3 position = LiftToOverlay(node.DrawPosition);

        if (node.Anchor == null)
        {
            DrawRing(position, 0.28f, NodeSourceColor, 0.055f);
            DrawMarker(position, NodeSourceColor, 0.11f, 0.02f);
            return;
        }

        if (node.Orphaned)
        {
            // Removed carrier — leave a thin hollow ring so the chain stays visible.
            DrawRing(position, 0.22f, NodeGhostColor);
            return;
        }

        switch (node.Kind)
        {
            case ContagionTraceNodeKind.Pawn:
                DrawMarker(position, NodePawnColor, 0.24f, 0.02f);
                break;
            case ContagionTraceNodeKind.Corpse:
                DrawMarker(position, NodeCorpseColor, 0.22f, 0.02f);
                DrawRing(position, 0.30f, NodeCorpseColor);
                break;
            case ContagionTraceNodeKind.Bench:
                DrawMarker(position, NodeBenchColor, 0.18f, 0.02f);
                break;
            case ContagionTraceNodeKind.Item:
                DrawMarker(position, NodeItemColor, 0.16f, 0.02f);
                break;
            case ContagionTraceNodeKind.Filth:
                DrawMarker(position, NodeFilthColor, 0.16f, 0.02f);
                DrawRing(position, 0.24f, NodeFilthColor);
                break;
            default:
                DrawRing(position, 0.22f, NodeGhostColor);
                break;
        }
    }

    private static Vector3 LiftToOverlay(Vector3 position)
    {
        position.y = AltitudeLayer.MetaOverlays.AltitudeFor() + 0.1f;
        return position;
    }

    // Thin, smooth ring built from many short line segments at a fixed narrow width — far cleaner
    // than GenDraw.DrawCircleOutline, which draws a chunky ~12-sided polygon with 0.2-wide lines.
    private static void DrawRing(Vector3 center, float radius, Color color, float lineWidth = 0.05f)
    {
        Material material = GetMaterial(color);
        const int segments = 32;
        Vector3 previous = center;
        previous.x += radius;
        for (int i = 1; i <= segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            Vector3 next = center;
            next.x += Mathf.Cos(angle) * radius;
            next.z += Mathf.Sin(angle) * radius;
            GenDraw.DrawLineBetween(previous, next, material, lineWidth);
            previous = next;
        }
    }

    // Always-on infected indicators: a small marker over every infected pawn (human/animal,
    // colony or not) and every infectious corpse on the map. Dev-mode only, view-rect culled.
    public static void DrawInfectedIndicators(Map map)
    {
        if (map == null)
        {
            return;
        }

        IReadOnlyList<Pawn> pawns = map.mapPawns?.AllPawnsSpawned;
        if (pawns != null)
        {
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || !pawn.Spawned || !ShouldDrawCell(map, pawn.Position))
                {
                    continue;
                }

                if (ContagionDiseaseUtility.IsInfectedOrIncubating(pawn))
                {
                    Vector3 position = LiftToOverlay(pawn.DrawPos);
                    DrawRing(position, 0.46f, IndicatorPawnColor);
                }
            }
        }

        List<Thing> corpses = map.listerThings?.ThingsInGroup(ThingRequestGroup.Corpse);
        if (corpses != null)
        {
            for (int i = 0; i < corpses.Count; i++)
            {
                if (corpses[i] is not Corpse corpse
                    || !corpse.Spawned
                    || !ShouldDrawCell(map, corpse.Position))
                {
                    continue;
                }

                Comp_InfectedCorpse comp = corpse.TryGetComp<Comp_InfectedCorpse>();
                if (comp != null && (comp.IsInfected || comp.IsSuspectedInfected))
                {
                    Vector3 position = LiftToOverlay(corpse.DrawPos);
                    DrawRing(position, 0.46f, IndicatorCorpseColor);
                }
            }
        }

        // Mark contaminated food stacks (meat, meals) so foodborne carriers are visible at a glance.
        List<Thing> foods = map.listerThings?.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree);
        if (foods != null)
        {
            for (int i = 0; i < foods.Count; i++)
            {
                Thing food = foods[i];
                if (food == null || !food.Spawned || !ShouldDrawCell(map, food.Position))
                {
                    continue;
                }

                Comp_ContaminatedFood comp = food.TryGetComp<Comp_ContaminatedFood>();
                if (comp != null && comp.IsContaminated)
                {
                    Vector3 position = LiftToOverlay(food.DrawPos);
                    DrawRing(position, 0.34f, IndicatorFoodColor);
                }
            }
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
        float maxRoomAirRange = 0f;
        float maxProximityRange = 0f;
        foreach (ResolvedTransmissionProfile resolvedProfile in ContagionDiseaseUtility.GetContagiousProfiles(sourcePawn))
        {
            contagiousProfiles.Add(resolvedProfile);

            if (ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Airborne airborne))
            {
                maxRange = Mathf.Max(maxRange, airborne.maxRange);
                if (airborne.roomAirBaseChanceFactor > 0f && airborne.roomAirMaxRange > 0 && airborne.roomAirMaxCells > 0)
                {
                    maxRoomAirRange = Mathf.Max(maxRoomAirRange, airborne.roomAirMaxRange);
                }
            }

            if (ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Proximity proximity))
            {
                maxRange = Mathf.Max(maxRange, proximity.maxRange);
                maxProximityRange = Mathf.Max(maxProximityRange, proximity.maxRange);
            }
        }

        maxRange = Mathf.Max(maxRange, maxRoomAirRange);
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
        if (maxProximityRange > 0f)
        {
            ContagionTransmissionUtility.CollectReachablePathDistances(
                map,
                sourcePawn.Position,
                maxProximityRange,
                PathDistanceByCell,
                PathOpenCells);
        }

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
                    float distance = ContagionTransmissionUtility.GetHorizontalDistance(sourcePawn.Position, cell);
                    bool targetRoofed = map.roofGrid.Roofed(cell);
                    float distanceFactor = ContagionTransmissionUtility.GetDistanceFactor(distance, airborne.distanceFalloffRate);
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

                if (ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Airborne roomAirborne)
                    && ContagionTransmissionUtility.TryGetRoomAirFactor(
                        map,
                        sourcePawn.Position,
                        cell,
                        roomAirborne,
                        out float effectiveRoomDistance,
                        out float roomAirFactor))
                {
                    ContagionDeveloperDiagnosticsUtility.TryBuildNominalAirborneRoomBreakdown(
                        sourcePawn,
                        resolvedProfile,
                        roomAirborne,
                        map,
                        settingsMultiplier,
                        effectiveRoomDistance,
                        roomAirFactor,
                        out ContagionSpreadBreakdown roomAirBreakdown);
                    if (roomAirBreakdown != null)
                    {
                        aggregateChance = CombineChance(aggregateChance, roomAirBreakdown.FinalChance);
                    }
                }

                if (ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Proximity proximity)
                    && PathDistanceByCell.TryGetValue(map.cellIndices.CellToIndex(cell), out float pathDistance)
                    && pathDistance <= proximity.maxRange)
                {
                    Room targetRoom = cell.GetRoom(map);
                    float distanceFactor = ContagionTransmissionUtility.GetDistanceFactor(pathDistance, proximity.distanceFalloffRate);
                    float outdoorFactor = ContagionTransmissionUtility.IsOutdoors(sourceRoom) || ContagionTransmissionUtility.IsOutdoors(targetRoom)
                        ? proximity.outdoorFactor
                        : 1f;
                    float cleanlinessFactor = ContagionTransmissionUtility.GetLocalCleanlinessFactor(
                        cell, targetRoom, map, proximity.cleanlinessImpact, proximity.outdoorFilthRadius);
                    ContagionDeveloperDiagnosticsUtility.TryBuildNominalProximityBreakdown(
                        sourcePawn,
                        resolvedProfile,
                        proximity,
                        map,
                        settingsMultiplier,
                        pathDistance,
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

    // Heatmap of a corpse's spatial infectivity, mirroring the live flea-exposure pass: per-cell
    // chance = baseChancePerCheck × flea potency × distance falloff over the flea vector range.
    // Resolves the disease via the include-hidden path so an undiagnosed-but-infectious corpse
    // still shows in dev mode, and falls back to the age-potency curve when the live flea hediff
    // hasn't accrued severity yet (a freshly-dead corpse). If there is genuinely no radial risk
    // yet, the corpse's outer range ring is still drawn so the area of effect is visible.
    public static void DrawCorpseInfectivityOverlay(Corpse corpse)
    {
        if (corpse?.Map == null || !corpse.Spawned)
        {
            return;
        }

        if (!ContagionCorpseUtility.TryGetCorpseInfectionForTransmission(corpse, out HediffDef diseaseDef)
            || !DiseaseProfileCache.TryGetResolvedProfile(diseaseDef, out ResolvedTransmissionProfile resolvedProfile)
            || !ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_CorpseFlea vector)
            || vector.maxRange <= 0f)
        {
            return;
        }

        Map map = corpse.Map;
        Hediff_ContagionCorpseFleas fleas = ContagionCorpseExposureUtility.FindCorpseFleas(corpse);
        float ageDays = Mathf.Max(0f, corpse.Age / 60000f);
        float potency = Mathf.Max(
            fleas?.Severity ?? 0f,
            ContagionCorpseExposureUtility.EvaluateCorpseFleaAgePotency(vector, ageDays));
        if (potency <= 0f)
        {
            // No radial risk yet — at least outline the exposure range so it's clear something is here.
            DrawRing(LiftToOverlay(corpse.DrawPos), vector.maxRange, NodeCorpseColor);
            return;
        }

        foreach (List<IntVec3> bucket in FillOverlayBuckets.Values)
        {
            bucket.Clear();
        }

        FillOverlayBucketColors.Clear();
        Dictionary<int, float> chanceByCell = new Dictionary<int, float>();
        float strongestChance = 0f;
        ContagionTransmissionUtility.CollectReachablePathDistances(
            map,
            corpse.Position,
            vector.maxRange,
            PathDistanceByCell,
            PathOpenCells);

        foreach (KeyValuePair<int, float> pathEntry in PathDistanceByCell)
        {
            IntVec3 cell = CellIndicesUtility.IndexToCell(pathEntry.Key, map.Size.x);
            if (!ShouldDrawCell(map, cell))
            {
                continue;
            }

            float distanceFactor = ContagionTransmissionUtility.GetDistanceFactor(pathEntry.Value, vector.distanceFalloffRate);
            float chance = Mathf.Clamp01(vector.baseChancePerCheck * potency * distanceFactor);
            if (chance < MinVisibleNominalChance)
            {
                continue;
            }

            chanceByCell[pathEntry.Key] = chance;
            strongestChance = Mathf.Max(strongestChance, chance);
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

    // Heatmap of where a selected animal would risk picking up a fecal-oral disease if it grazed,
    // reading the live eating nodes via the map's transmission component. Pure dev visualization; the
    // per-cell math mirrors the real ingestion roll (see ContagionFecalOralTracker.TryGetNodeGrazingChance).
    // Cells are shaded relative to the strongest risk cell; the mouseover readout gives absolute %.
    public static void DrawEatingRiskOverlay(Pawn ingester)
    {
        if (ingester?.Map == null
            || !ingester.Spawned
            || ingester.Dead
            || ingester.RaceProps?.Animal != true
            || Find.Selector.SingleSelectedThing != ingester)
        {
            return;
        }

        Map map = ingester.Map;
        Contagion_MapTransmissionComponent component = map.GetComponent<Contagion_MapTransmissionComponent>();
        if (component == null)
        {
            return;
        }

        component.BuildEatingRiskOverlay(ingester, EatingRiskByCell);
        if (EatingRiskByCell.Count == 0)
        {
            return;
        }

        foreach (List<IntVec3> bucket in FillOverlayBuckets.Values)
        {
            bucket.Clear();
        }

        FillOverlayBucketColors.Clear();
        float strongestChance = 0f;
        foreach (KeyValuePair<int, float> chanceEntry in EatingRiskByCell)
        {
            IntVec3 cell = CellIndicesUtility.IndexToCell(chanceEntry.Key, map.Size.x);
            strongestChance = ContagionRiskMath.VisibleChanceMaximum(
                strongestChance,
                chanceEntry.Value,
                ShouldDrawCell(map, cell),
                MinVisibleNominalChance);
        }

        if (strongestChance <= 0f)
        {
            return;
        }

        foreach (KeyValuePair<int, float> chanceEntry in EatingRiskByCell)
        {
            if (chanceEntry.Value < MinVisibleNominalChance)
            {
                continue;
            }

            float normalizedChance = GetDisplayStrength(chanceEntry.Value, strongestChance);
            if (normalizedChance <= 0f)
            {
                continue;
            }

            IntVec3 cell = CellIndicesUtility.IndexToCell(chanceEntry.Key, map.Size.x);
            if (!ShouldDrawCell(map, cell))
            {
                continue;
            }

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

    // Draws arrowheads along the edge pointing from source toward target. For a long line, one
    // arrow sits ~1 cell in from each end so direction reads at both source and destination; for a
    // short line, a single arrow near the middle. Arrows are small "V" chevrons.
    private static void DrawDirectionArrows(Vector3 origin, Vector3 end, Color color)
    {
        Vector3 direction = end - origin;
        direction.y = 0f;
        float length = direction.magnitude;
        if (length < 0.0001f)
        {
            return;
        }

        direction /= length;
        Material material = GetMaterial(color);

        const float EndOffset = 1f;
        if (length < 2.4f * EndOffset)
        {
            DrawArrowHead(Vector3.Lerp(origin, end, 0.5f), direction, material);
            return;
        }

        DrawArrowHead(origin + direction * EndOffset, direction, material);
        DrawArrowHead(end - direction * EndOffset, direction, material);
    }

    private static void DrawArrowHead(Vector3 tip, Vector3 direction, Material material)
    {
        const float ArmLength = 0.15f;
        const float ArmWidth = 0.11f;
        Vector3 perpendicular = new Vector3(-direction.z, 0f, direction.x);
        Vector3 back = tip - direction * ArmLength;
        Vector3 leftArm = back + perpendicular * ArmWidth;
        Vector3 rightArm = back - perpendicular * ArmWidth;
        GenDraw.DrawLineBetween(tip, leftArm, material, 0.04f);
        GenDraw.DrawLineBetween(tip, rightArm, material, 0.04f);
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

    private static float GetDisplayStrength(float chance, float strongestChance)
    {
        if (chance <= 0f || strongestChance <= 0f)
        {
            return 0f;
        }

        float normalizedChance = Mathf.Clamp01(chance / strongestChance);
        return Mathf.Pow(normalizedChance, 0.65f);
    }

    private static Color GetTraceColor(ContagionDebugVectorKind vectorKind)
    {
        return vectorKind switch
        {
            ContagionDebugVectorKind.Airborne => TraceAirborneColor,
            ContagionDebugVectorKind.AirborneRoom => TraceAirborneColor,
            ContagionDebugVectorKind.Proximity => TraceProximityColor,
            ContagionDebugVectorKind.Social => TraceSocialColor,
            ContagionDebugVectorKind.Foodborne => TraceFoodborneColor,
            ContagionDebugVectorKind.CorpseFlea => TraceCorpseFleaColor,
            ContagionDebugVectorKind.CorpseFluid => TraceCorpseFluidColor,
            ContagionDebugVectorKind.Cooking => TraceCookingColor,
            ContagionDebugVectorKind.Fomite => TraceFomiteColor,
            ContagionDebugVectorKind.FecalOralEating => TraceFecalOralEatingColor,
            ContagionDebugVectorKind.FecalOralLiving => TraceFecalOralLivingColor,
            ContagionDebugVectorKind.Environmental => TraceEnvironmentalColor,
            ContagionDebugVectorKind.Developer => TraceDeveloperColor,
            ContagionDebugVectorKind.OffMap => TraceOffMapColor,
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
