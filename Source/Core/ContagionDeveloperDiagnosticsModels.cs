using UnityEngine;
using Verse;

namespace Contagion;

public enum ContagionDebugVectorKind
{
    Airborne,
    Proximity,
    Social
}

public sealed class ContagionSpreadBreakdown
{
    public HediffDef DiseaseDef { get; set; }

    public ResolvedTransmissionProfile ResolvedProfile { get; set; }

    public ContagionDebugVectorKind VectorKind { get; set; }

    public float BaseChance { get; set; }

    public float Infectivity { get; set; }

    public float SeasonalMultiplier { get; set; } = 1f;

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

    public float FinalChance => Mathf.Max(0f, BaseChance)
        * Mathf.Max(0f, Infectivity)
        * Mathf.Max(0f, SeasonalMultiplier)
        * Mathf.Max(0f, TargetEligibilityFactor)
        * Mathf.Max(0f, VectorContextFactor)
        * Mathf.Max(0f, SettingsMultiplier);
}

public sealed class ContagionTransmissionTrace
{
    public ContagionTransmissionTrace(
        Pawn sourcePawn,
        Pawn targetPawn,
        HediffDef diseaseDef,
        ContagionDebugVectorKind vectorKind,
        int tick)
    {
        SourcePawn = sourcePawn;
        TargetPawn = targetPawn;
        DiseaseDef = diseaseDef;
        VectorKind = vectorKind;
        Tick = tick;
    }

    public Pawn SourcePawn { get; }

    public Pawn TargetPawn { get; }

    public HediffDef DiseaseDef { get; }

    public ContagionDebugVectorKind VectorKind { get; }

    public int Tick { get; }

    public bool Contains(Pawn pawn)
    {
        return pawn != null && (SourcePawn == pawn || TargetPawn == pawn);
    }

    public bool IsValidFor(Map map, int currentTick, int maxAgeTicks)
    {
        if (SourcePawn == null || TargetPawn == null || map == null)
        {
            return false;
        }

        if (currentTick - Tick > maxAgeTicks)
        {
            return false;
        }

        return SourcePawn.Spawned
            && TargetPawn.Spawned
            && SourcePawn.Map == map
            && TargetPawn.Map == map
            && !SourcePawn.Destroyed
            && !TargetPawn.Destroyed;
    }
}