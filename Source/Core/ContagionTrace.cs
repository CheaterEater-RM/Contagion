using Verse;

namespace Contagion;

// Thin façade over the per-map developer trace controller so transmission code anywhere can
// record graph nodes/edges without plumbing the map component through every call. All calls are
// no-ops unless developer mode + trace capture are on (the controller enforces that).
internal static class ContagionTrace
{
    private static ContagionMapDeveloperDiagnosticsController Controller(Map map)
    {
        return map?.GetComponent<Contagion_MapTransmissionComponent>()?.DeveloperDiagnostics;
    }

    // Records source→target as a graph edge (creating/reusing each node). Resolves the controller
    // from the target's map. A null source becomes a cell-anchored origin at the target.
    public static void Transmission(Thing source, Thing target, HediffDef disease, ContagionDebugVectorKind vector)
    {
        if (target == null)
        {
            return;
        }

        Controller(target.MapHeld)?.RecordTransmission(source, target, disease, vector);
    }

    public static void SourceAtCell(IntVec3 sourceCell, Pawn target, HediffDef disease, ContagionDebugVectorKind vector)
    {
        if (target == null)
        {
            return;
        }

        Controller(target.MapHeld)?.RecordSourceTrace(sourceCell, target, disease, vector);
    }

    public static void SourceAtNearestMapEdge(Pawn target, HediffDef disease, ContagionDebugVectorKind vector)
    {
        if (target?.MapHeld == null)
        {
            return;
        }

        SourceAtCell(GetNearestMapEdgeCell(target.MapHeld, target.PositionHeld), target, disease, vector);
    }

    // Ensures a node for a thing (e.g. a corpse, bench, or food stack) and returns its id, -1 if
    // tracing is disabled or the thing isn't on a map.
    public static int EnsureNode(Thing anchor, HediffDef disease)
    {
        if (anchor == null)
        {
            return -1;
        }

        return Controller(anchor.MapHeld)?.EnsureNode(anchor, disease) ?? -1;
    }

    // Re-anchors any trace nodes for a freshly-dead pawn onto its corpse, so the infection chain
    // survives the pawn→corpse transition instead of being pruned. No-op if tracing is off.
    public static int ReanchorPawnToCorpse(Pawn pawn, Corpse corpse)
    {
        if (pawn == null || corpse == null)
        {
            return 0;
        }

        return Controller(corpse.MapHeld)?.ReanchorPawnToCorpse(pawn, corpse) ?? 0;
    }

    public static void Edge(Map map, int fromId, int toId, ContagionDebugVectorKind vector)
    {
        if (fromId < 0 || toId < 0)
        {
            return;
        }

        Controller(map)?.RecordEdge(fromId, toId, vector);
    }

    public static void FoodborneIngestion(Thing food, Pawn ingester, HediffDef disease, int fallbackSourceNodeId)
    {
        if (ingester == null)
        {
            return;
        }

        Controller(ingester.MapHeld)?.RecordFoodborneIngestionTrace(food, ingester, disease, fallbackSourceNodeId);
    }

    // Routes a cooked product's trace through the bench the worker is using (the stove/table in
    // job TargetA), so the chain reads source → bench → meal rather than source → meal. Ensures a
    // node for the bench, links the upstream source (contaminated ingredient or contagious cook)
    // into it, and returns the bench node id to stamp on the product. Falls back to the upstream
    // id unchanged when there's no usable bench (e.g. a nutrient-paste dispenser) or tracing is off.
    public static int RouteThroughBench(Pawn worker, HediffDef disease, int upstreamNodeId, ContagionDebugVectorKind upstreamVector)
    {
        if (worker?.CurJob?.GetTarget(Verse.AI.TargetIndex.A).Thing is not Building bench)
        {
            return upstreamNodeId;
        }

        int benchNodeId = EnsureNode(bench, disease);
        if (benchNodeId < 0)
        {
            return upstreamNodeId;
        }

        if (upstreamNodeId >= 0 && upstreamNodeId != benchNodeId)
        {
            Edge(bench.MapHeld, upstreamNodeId, benchNodeId, upstreamVector);
        }

        return benchNodeId;
    }

    // Finds the node that contaminated this anchor (the edge leading into its node), or -1.
    public static int GetUpstreamNode(Thing anchor, HediffDef disease)
    {
        if (anchor == null || disease == null)
        {
            return -1;
        }

        return Controller(anchor.MapHeld)?.GetUpstreamNodeId(anchor, disease) ?? -1;
    }

    public static int GetContaminationSourceNode(Map map, Thing anchor, HediffDef disease, int fallbackSourceNodeId)
    {
        if (map == null || disease == null)
        {
            return fallbackSourceNodeId;
        }

        return Controller(map)?.GetContaminationSourceNodeId(anchor, disease, fallbackSourceNodeId)
            ?? fallbackSourceNodeId;
    }

    private static IntVec3 GetNearestMapEdgeCell(Map map, IntVec3 position)
    {
        if (map == null || !position.IsValid)
        {
            return position;
        }

        int maxX = map.Size.x - 1;
        int maxZ = map.Size.z - 1;
        int x = UnityEngine.Mathf.Clamp(position.x, 0, maxX);
        int z = UnityEngine.Mathf.Clamp(position.z, 0, maxZ);
        int west = x;
        int east = maxX - x;
        int south = z;
        int north = maxZ - z;
        int nearest = UnityEngine.Mathf.Min(UnityEngine.Mathf.Min(west, east), UnityEngine.Mathf.Min(south, north));

        if (nearest == west)
        {
            x = 0;
        }
        else if (nearest == east)
        {
            x = maxX;
        }
        else if (nearest == south)
        {
            z = 0;
        }
        else
        {
            z = maxZ;
        }

        return new IntVec3(x, position.y, z);
    }
}
