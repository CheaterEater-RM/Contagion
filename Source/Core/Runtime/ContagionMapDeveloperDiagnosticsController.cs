using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Contagion;

internal sealed class ContagionMapDeveloperDiagnosticsController
{
    // Bounds on the session-only graph so a long game can't grow it without limit.
    private const int MaxNodeCount = 256;

    private const int MaxEdgeCount = 512;

    private readonly Map _map;

    private HediffDef _forcedArrivalDisease;

    private readonly List<ContagionTraceNode> _nodes = new List<ContagionTraceNode>();

    private readonly List<ContagionTraceEdge> _edges = new List<ContagionTraceEdge>();

    private int _nextNodeId;

    private bool _traceCaptureEnabled = true;

    private Pawn _hoverSourcePawn;

    private Pawn _hoverTargetPawn;

    private int _hoverFrame = -1;

    public ContagionMapDeveloperDiagnosticsController(Map map)
    {
        _map = map;
    }

    public HediffDef ForcedArrivalDisease => _forcedArrivalDisease;

    public IReadOnlyList<ContagionTraceNode> Nodes => _nodes;

    public IReadOnlyList<ContagionTraceEdge> Edges => _edges;

    public int TraceElementCount => _edges.Count;

    public bool TraceCaptureEnabled => _traceCaptureEnabled;

    public void ArmForcedArrival(HediffDef diseaseDef)
    {
        if (diseaseDef == null)
        {
            return;
        }

        _forcedArrivalDisease = diseaseDef;
    }

    public void ClearForcedArrival()
    {
        _forcedArrivalDisease = null;
    }

    // ── Trace graph API ──────────────────────────────────────────────────────────────────────

    // Convenience wrapper for the simple pawn→pawn transmission sites.
    public void RecordTransmissionTrace(
        Pawn sourcePawn,
        Pawn targetPawn,
        HediffDef diseaseDef,
        ContagionDebugVectorKind vectorKind)
    {
        RecordTransmission(sourcePawn, targetPawn, diseaseDef, vectorKind);
    }

    // Records source→target as a graph edge, creating/reusing a node for each thing. Either
    // endpoint may be null (an off-map / environmental source); a null source resolves to a cell
    // node at the target's position so the chain still has an origin to draw from.
    public int RecordTransmission(
        Thing source,
        Thing target,
        HediffDef diseaseDef,
        ContagionDebugVectorKind vectorKind)
    {
        if (!CaptureActive() || target == null || diseaseDef == null || !ThingOnMap(target))
        {
            return -1;
        }

        int targetId = EnsureNode(target, diseaseDef);
        if (targetId < 0)
        {
            return -1;
        }

        int sourceId = source != null && ThingOnMap(source)
            ? EnsureNode(source, diseaseDef)
            : EnsureCellNode(target.PositionHeld, diseaseDef);
        RecordEdge(sourceId, targetId, vectorKind);
        return targetId;
    }

    // Finds the live node for this anchor+disease or creates one. Returns the node id, -1 if disabled.
    public int EnsureNode(Thing anchor, HediffDef diseaseDef)
    {
        if (!CaptureActive() || anchor == null || diseaseDef == null || !ThingOnMap(anchor))
        {
            return -1;
        }

        for (int i = 0; i < _nodes.Count; i++)
        {
            ContagionTraceNode node = _nodes[i];
            if (node.Anchor == anchor && node.Disease == diseaseDef)
            {
                node.LastCell = anchor.PositionHeld;
                return node.Id;
            }
        }

        return AddNode(anchor, anchor.PositionHeld, diseaseDef);
    }

    // Creates/reuses a cell-anchored "ghost" node (sourceless environmental/seeding origins).
    public int EnsureCellNode(IntVec3 cell, HediffDef diseaseDef)
    {
        if (!CaptureActive() || diseaseDef == null || !cell.IsValid)
        {
            return -1;
        }

        for (int i = 0; i < _nodes.Count; i++)
        {
            ContagionTraceNode node = _nodes[i];
            if (node.Anchor == null && node.Disease == diseaseDef && node.LastCell == cell)
            {
                return node.Id;
            }
        }

        return AddNode(null, cell, diseaseDef);
    }

    public void RecordEdge(int fromId, int toId, ContagionDebugVectorKind vectorKind)
    {
        if (!CaptureActive() || fromId < 0 || toId < 0 || fromId == toId)
        {
            return;
        }

        for (int i = 0; i < _edges.Count; i++)
        {
            ContagionTraceEdge edge = _edges[i];
            if (edge.FromId == fromId && edge.ToId == toId && edge.Vector == vectorKind)
            {
                return;
            }
        }

        if (_edges.Count >= MaxEdgeCount)
        {
            _edges.RemoveAt(0);
        }

        _edges.Add(new ContagionTraceEdge(fromId, toId, vectorKind, Find.TickManager.TicksGame));
    }

    private int AddNode(Thing anchor, IntVec3 cell, HediffDef diseaseDef)
    {
        if (_nodes.Count >= MaxNodeCount)
        {
            RemoveOldestRemovableNode();
        }

        int id = _nextNodeId++;
        _nodes.Add(new ContagionTraceNode(id, anchor, cell, diseaseDef, Find.TickManager.TicksGame));
        return id;
    }

    private void RemoveOldestRemovableNode()
    {
        if (_nodes.Count == 0)
        {
            return;
        }

        int removeId = _nodes[0].Id;
        _nodes.RemoveAt(0);
        _edges.RemoveAll(edge => edge.FromId == removeId || edge.ToId == removeId);
    }

    public void ClearAllTraces()
    {
        _nodes.Clear();
        _edges.Clear();
    }

    public void ToggleTraceCapture()
    {
        _traceCaptureEnabled = !_traceCaptureEnabled;
    }

    public void ClearTracesForPawn(Pawn pawn)
    {
        if (pawn == null || _nodes.Count == 0)
        {
            return;
        }

        List<int> removedIds = new List<int>();
        _nodes.RemoveAll(node =>
        {
            if (node.Anchor == pawn)
            {
                removedIds.Add(node.Id);
                return true;
            }

            return false;
        });

        if (removedIds.Count > 0)
        {
            _edges.RemoveAll(edge => removedIds.Contains(edge.FromId) || removedIds.Contains(edge.ToId));
        }
    }

    public bool HasTracesForPawn(Pawn pawn)
    {
        if (pawn == null)
        {
            return false;
        }

        for (int i = 0; i < _nodes.Count; i++)
        {
            if (_nodes[i].Anchor == pawn)
            {
                return true;
            }
        }

        return false;
    }

    // ── Hover line (pawn ↔ pawn) ─────────────────────────────────────────────────────────────

    public void SetHoverPair(Pawn sourcePawn, Pawn targetPawn)
    {
        if (Contagion_Mod.Settings?.DeveloperDiagnosticsEnabled != true
            || sourcePawn == null
            || targetPawn == null
            || sourcePawn == targetPawn
            || sourcePawn.Map != _map
            || targetPawn.Map != _map)
        {
            ClearHoverPair();
            return;
        }

        _hoverSourcePawn = sourcePawn;
        _hoverTargetPawn = targetPawn;
        _hoverFrame = Time.frameCount;
    }

    public bool TryGetHoverPair(out Pawn sourcePawn, out Pawn targetPawn)
    {
        sourcePawn = null;
        targetPawn = null;

        if (Contagion_Mod.Settings?.DeveloperDiagnosticsEnabled != true)
        {
            ClearHoverPair();
            return false;
        }

        if (_hoverSourcePawn == null
            || _hoverTargetPawn == null
            || Time.frameCount - _hoverFrame > 1
            || _hoverSourcePawn.Map != _map
            || _hoverTargetPawn.Map != _map
            || !_hoverSourcePawn.Spawned
            || !_hoverTargetPawn.Spawned)
        {
            ClearHoverPair();
            return false;
        }

        sourcePawn = _hoverSourcePawn;
        targetPawn = _hoverTargetPawn;
        return true;
    }

    public void ClearHoverPair()
    {
        _hoverSourcePawn = null;
        _hoverTargetPawn = null;
        _hoverFrame = -1;
    }

    // ── Per-frame update ─────────────────────────────────────────────────────────────────────

    public void Update()
    {
        PruneGraph();

        if (Contagion_Mod.Settings?.DeveloperDiagnosticsEnabled != true || Find.CurrentMap != _map)
        {
            ClearHoverPair();
            return;
        }

        if (TryGetHoverPair(out Pawn hoverSourcePawn, out Pawn hoverTargetPawn))
        {
            ContagionDeveloperOverlayDrawer.DrawHoverLine(hoverSourcePawn, hoverTargetPawn);
        }

        if (_traceCaptureEnabled)
        {
            ContagionDeveloperOverlayDrawer.DrawTraceGraph(_map, _nodes, _edges);
        }

        ContagionDeveloperOverlayDrawer.DrawInfectedIndicators(_map);

        if (Find.Selector.SingleSelectedThing is Corpse corpse
            && corpse.Map == _map
            && corpse.TryGetComp<Comp_InfectedCorpse>() is Comp_InfectedCorpse infectedComp
            && infectedComp.IsInfected)
        {
            ContagionDeveloperOverlayDrawer.DrawCorpseInfectivityOverlay(corpse);
        }
    }

    // ── Pruning ──────────────────────────────────────────────────────────────────────────────

    // Each tick: refresh ghost positions, then drop any connected component that no longer
    // contains an active carrier (live infected pawn, infectious corpse, or contaminated food).
    private void PruneGraph()
    {
        if (_nodes.Count == 0)
        {
            _edges.Clear();
            return;
        }

        // Refresh last-known cell for still-spawned anchors so ghosts freeze at the right spot.
        for (int i = 0; i < _nodes.Count; i++)
        {
            ContagionTraceNode node = _nodes[i];
            if (node.Anchor != null && node.Anchor.SpawnedOrAnyParentSpawned)
            {
                node.LastCell = node.Anchor.PositionHeld;
            }
        }

        Dictionary<int, ContagionTraceNode> byId = new Dictionary<int, ContagionTraceNode>(_nodes.Count);
        for (int i = 0; i < _nodes.Count; i++)
        {
            byId[_nodes[i].Id] = _nodes[i];
        }

        // Undirected adjacency.
        Dictionary<int, List<int>> adjacency = new Dictionary<int, List<int>>();
        for (int i = 0; i < _edges.Count; i++)
        {
            ContagionTraceEdge edge = _edges[i];
            if (!byId.ContainsKey(edge.FromId) || !byId.ContainsKey(edge.ToId))
            {
                continue;
            }

            AddAdjacency(adjacency, edge.FromId, edge.ToId);
            AddAdjacency(adjacency, edge.ToId, edge.FromId);
        }

        // Seed the keep-set with every active node, then flood across edges.
        HashSet<int> keep = new HashSet<int>();
        Queue<int> frontier = new Queue<int>();
        for (int i = 0; i < _nodes.Count; i++)
        {
            ContagionTraceNode node = _nodes[i];
            if (IsNodeActive(node) && keep.Add(node.Id))
            {
                frontier.Enqueue(node.Id);
            }
        }

        while (frontier.Count > 0)
        {
            int current = frontier.Dequeue();
            if (!adjacency.TryGetValue(current, out List<int> neighbours))
            {
                continue;
            }

            for (int i = 0; i < neighbours.Count; i++)
            {
                if (keep.Add(neighbours[i]))
                {
                    frontier.Enqueue(neighbours[i]);
                }
            }
        }

        _nodes.RemoveAll(node => !keep.Contains(node.Id));
        _edges.RemoveAll(edge => !keep.Contains(edge.FromId) || !keep.Contains(edge.ToId));
    }

    private static void AddAdjacency(Dictionary<int, List<int>> adjacency, int from, int to)
    {
        if (!adjacency.TryGetValue(from, out List<int> list))
        {
            list = new List<int>();
            adjacency[from] = list;
        }

        list.Add(to);
    }

    // A node still carries the disease if its anchor is a live infected pawn, an infectious
    // corpse, or a still-contaminated food stack. Benches, ghosts, and cleared anchors are inert
    // and survive only by being reachable from an active node.
    private bool IsNodeActive(ContagionTraceNode node)
    {
        Thing anchor = node.Anchor;
        if (anchor == null || anchor.Destroyed || !anchor.SpawnedOrAnyParentSpawned)
        {
            return false;
        }

        switch (anchor)
        {
            case Corpse corpse:
            {
                Comp_InfectedCorpse comp = corpse.TryGetComp<Comp_InfectedCorpse>();
                return comp != null && (comp.IsInfected || comp.IsSuspectedInfected);
            }

            case Pawn pawn:
            {
                HediffDef effectiveDef = node.Disease;
                if (DiseaseProfileCache.TryGetResolvedProfile(node.Disease, out ResolvedTransmissionProfile profile))
                {
                    effectiveDef = profile.ResolveHediffForPawn(pawn);
                }

                return ContagionDiseaseUtility.HasDiseaseOrIncubation(pawn, effectiveDef);
            }

            default:
            {
                Comp_ContaminatedFood food = anchor.TryGetComp<Comp_ContaminatedFood>();
                return food != null && food.IsContaminated;
            }
        }
    }

    private bool CaptureActive()
    {
        return Contagion_Mod.Settings?.DeveloperDiagnosticsEnabled == true && _traceCaptureEnabled;
    }

    private bool ThingOnMap(Thing thing)
    {
        return thing != null && thing.MapHeld == _map;
    }
}
