using UnityEngine;
using Verse;

namespace Contagion;

// Saved nowhere — the trace graph is session-only. Ordering is cosmetic; appended for tidiness.
public enum ContagionDebugVectorKind
{
    Airborne,
    AirborneRoom,
    Proximity,
    Social,
    Foodborne,
    CorpseFlea,
    CorpseFluid,
    Cooking,
    Fomite,
    FecalOralEating,
    FecalOralLiving,
    Environmental,
    Developer,
    OffMap
}

// Glyph selector for rendering a trace node. Derived from the node's current anchor.
public enum ContagionTraceNodeKind
{
    Pawn,
    Corpse,
    Bench,
    Item,
    Filth,
    Cell
}

public sealed class ContagionSpreadBreakdown
{
    public HediffDef DiseaseDef { get; set; }

    public ResolvedTransmissionProfile ResolvedProfile { get; set; }

    public ContagionDebugVectorKind VectorKind { get; set; }

    public float BaseChance { get; set; }

    public float Infectivity { get; set; }

    public float TargetEligibilityFactor { get; set; }

    public float SettingsMultiplier { get; set; } = 1f;

    public float VectorContextFactor { get; set; } = 1f;

    public float Distance { get; set; }

    public float DistanceFactor { get; set; } = 1f;

    public float EnclosureFactor { get; set; } = 1f;

    public float ObstructionFactor { get; set; } = 1f;

    public float OutdoorFactor { get; set; } = 1f;

    public float CleanlinessFactor { get; set; } = 1f;

    public float MaskFactor { get; set; } = 1f;

    public float SuppressionFactor { get; set; } = 1f;

    public HediffDef ImmunityCause { get; set; }

    public string TargetEligibilityBlockReason { get; set; }

    public float FinalChance => Mathf.Max(0f, BaseChance)
        * Mathf.Max(0f, Infectivity)
        * Mathf.Max(0f, TargetEligibilityFactor)
        * Mathf.Max(0f, VectorContextFactor)
        * Mathf.Max(0f, SettingsMultiplier);
}

// A node in the session-only infection trace graph. Anchors to a live Thing (pawn / corpse /
// item stack / bench) while it exists, then falls back to LastCell as a "ghost" once the Thing
// is destroyed or unspawned. Plain runtime object — never serialized.
public sealed class ContagionTraceNode
{
    public ContagionTraceNode(int id, Thing anchor, IntVec3 lastCell, HediffDef disease, int createdTick)
    {
        Id = id;
        Anchor = anchor;
        LastCell = lastCell;
        Disease = disease;
        CreatedTick = createdTick;
    }

    public int Id { get; }

    public Thing Anchor { get; set; }

    public IntVec3 LastCell { get; set; }

    public HediffDef Disease { get; }

    public int CreatedTick { get; }

    public bool Orphaned => Anchor == null || Anchor.Destroyed || !Anchor.SpawnedOrAnyParentSpawned;

    public ContagionTraceNodeKind Kind
    {
        get
        {
            switch (Anchor)
            {
                case null:
                    return ContagionTraceNodeKind.Cell;
                case Corpse:
                    return ContagionTraceNodeKind.Corpse;
                case Pawn:
                    return ContagionTraceNodeKind.Pawn;
                case Building:
                    return ContagionTraceNodeKind.Bench;
                case RimWorld.Filth:
                    return ContagionTraceNodeKind.Filth;
                default:
                    return ContagionTraceNodeKind.Item;
            }
        }
    }

    // Best draw position: the live anchor (or its holder) centre, else the last-known cell.
    public Vector3 DrawPosition
    {
        get
        {
            if (Anchor != null && Anchor.SpawnedOrAnyParentSpawned)
            {
                Thing spawned = Anchor.SpawnedParentOrMe;
                if (spawned != null && spawned.Spawned)
                {
                    return spawned.DrawPos;
                }
            }

            return LastCell.ToVector3Shifted();
        }
    }
}

// A directed link in the trace graph (source → target) tagged with the vector that carried it.
public sealed class ContagionTraceEdge
{
    public ContagionTraceEdge(int fromId, int toId, ContagionDebugVectorKind vector, int tick)
    {
        FromId = fromId;
        ToId = toId;
        Vector = vector;
        Tick = tick;
    }

    public int FromId { get; }

    public int ToId { get; }

    public ContagionDebugVectorKind Vector { get; }

    public int Tick { get; }
}
