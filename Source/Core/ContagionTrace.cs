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

    public static void Edge(Map map, int fromId, int toId, ContagionDebugVectorKind vector)
    {
        if (fromId < 0 || toId < 0)
        {
            return;
        }

        Controller(map)?.RecordEdge(fromId, toId, vector);
    }
}
