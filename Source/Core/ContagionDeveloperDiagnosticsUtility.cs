using UnityEngine;
using Verse;

namespace Contagion;

public static class ContagionDeveloperDiagnosticsUtility
{
    public static bool TryBuildAirborneBreakdown(
        Pawn sourcePawn,
        Pawn targetPawn,
        ResolvedTransmissionProfile resolvedProfile,
        Vector_Airborne vector,
        Map map,
        float settingsMultiplier,
        float distance,
        float distanceFactor,
        float enclosureFactor,
        float obstructionFactor,
        float maskFactor,
        float suppressionFactor,
        out ContagionSpreadBreakdown breakdown)
    {
        bool canCompute = TryBuildBreakdown(
            vector?.baseChancePerCheck ?? 0f,
            sourcePawn,
            targetPawn,
            resolvedProfile,
            vector,
            map,
            settingsMultiplier,
            distanceFactor * enclosureFactor * obstructionFactor * maskFactor * suppressionFactor,
            ContagionDebugVectorKind.Airborne,
            out breakdown);

        if (breakdown != null)
        {
            breakdown.Distance = distance;
            breakdown.DistanceFactor = distanceFactor;
            breakdown.EnclosureFactor = enclosureFactor;
            breakdown.ObstructionFactor = obstructionFactor;
            breakdown.MaskFactor = maskFactor;
            breakdown.SuppressionFactor = suppressionFactor;
        }

        return canCompute;
    }

    public static bool TryBuildAirborneRoomBreakdown(
        Pawn sourcePawn,
        Pawn targetPawn,
        ResolvedTransmissionProfile resolvedProfile,
        Vector_Airborne vector,
        Map map,
        float settingsMultiplier,
        float effectiveRoomDistance,
        float roomAirFactor,
        float maskFactor,
        float suppressionFactor,
        out ContagionSpreadBreakdown breakdown)
    {
        bool canCompute = TryBuildBreakdown(
            (vector?.baseChancePerCheck ?? 0f) * Mathf.Max(0f, vector?.roomAirBaseChanceFactor ?? 0f),
            sourcePawn,
            targetPawn,
            resolvedProfile,
            vector,
            map,
            settingsMultiplier,
            roomAirFactor * maskFactor * suppressionFactor,
            ContagionDebugVectorKind.AirborneRoom,
            out breakdown);

        if (breakdown != null)
        {
            breakdown.Distance = effectiveRoomDistance;
            breakdown.DistanceFactor = roomAirFactor;
            breakdown.EnclosureFactor = 1f;
            breakdown.ObstructionFactor = 1f;
            breakdown.MaskFactor = maskFactor;
            breakdown.SuppressionFactor = suppressionFactor;
        }

        return canCompute;
    }

    public static bool TryBuildProximityBreakdown(
        Pawn sourcePawn,
        Pawn targetPawn,
        ResolvedTransmissionProfile resolvedProfile,
        Vector_Proximity vector,
        Map map,
        float settingsMultiplier,
        float distance,
        float distanceFactor,
        float outdoorFactor,
        float cleanlinessFactor,
        float maskFactor,
        float suppressionFactor,
        out ContagionSpreadBreakdown breakdown)
    {
        bool canCompute = TryBuildBreakdown(
            vector?.baseChancePerCheck ?? 0f,
            sourcePawn,
            targetPawn,
            resolvedProfile,
            vector,
            map,
            settingsMultiplier,
            distanceFactor * outdoorFactor * cleanlinessFactor * maskFactor * suppressionFactor,
            ContagionDebugVectorKind.Proximity,
            out breakdown);

        if (breakdown != null)
        {
            breakdown.Distance = distance;
            breakdown.DistanceFactor = distanceFactor;
            breakdown.OutdoorFactor = outdoorFactor;
            breakdown.CleanlinessFactor = cleanlinessFactor;
            breakdown.MaskFactor = maskFactor;
            breakdown.SuppressionFactor = suppressionFactor;
        }

        return canCompute;
    }

    public static bool TryBuildSocialBreakdown(
        Pawn sourcePawn,
        Pawn targetPawn,
        ResolvedTransmissionProfile resolvedProfile,
        Vector_Social vector,
        Map map,
        float settingsMultiplier,
        float enclosureFactor,
        float obstructionFactor,
        float maskFactor,
        float suppressionFactor,
        out ContagionSpreadBreakdown breakdown)
    {
        bool canCompute = TryBuildBreakdown(
            vector?.baseChancePerInteraction ?? 0f,
            sourcePawn,
            targetPawn,
            resolvedProfile,
            vector,
            map,
            settingsMultiplier,
            enclosureFactor * obstructionFactor * maskFactor * suppressionFactor,
            ContagionDebugVectorKind.Social,
            out breakdown);

        if (breakdown != null)
        {
            breakdown.EnclosureFactor = enclosureFactor;
            breakdown.ObstructionFactor = obstructionFactor;
            breakdown.MaskFactor = maskFactor;
            breakdown.SuppressionFactor = suppressionFactor;
        }

        return canCompute;
    }

    public static bool TryBuildNominalAirborneBreakdown(
        Pawn sourcePawn,
        ResolvedTransmissionProfile resolvedProfile,
        Vector_Airborne vector,
        Map map,
        float settingsMultiplier,
        float distance,
        float distanceFactor,
        float enclosureFactor,
        float obstructionFactor,
        out ContagionSpreadBreakdown breakdown)
    {
        bool canCompute = TryBuildNominalBreakdown(
            vector?.baseChancePerCheck ?? 0f,
            sourcePawn,
            resolvedProfile,
            vector,
            map,
            settingsMultiplier,
            distanceFactor * enclosureFactor * obstructionFactor,
            ContagionDebugVectorKind.Airborne,
            out breakdown);

        if (breakdown != null)
        {
            breakdown.Distance = distance;
            breakdown.DistanceFactor = distanceFactor;
            breakdown.EnclosureFactor = enclosureFactor;
            breakdown.ObstructionFactor = obstructionFactor;
        }

        return canCompute;
    }

    public static bool TryBuildNominalAirborneRoomBreakdown(
        Pawn sourcePawn,
        ResolvedTransmissionProfile resolvedProfile,
        Vector_Airborne vector,
        Map map,
        float settingsMultiplier,
        float effectiveRoomDistance,
        float roomAirFactor,
        out ContagionSpreadBreakdown breakdown)
    {
        bool canCompute = TryBuildNominalBreakdown(
            (vector?.baseChancePerCheck ?? 0f) * Mathf.Max(0f, vector?.roomAirBaseChanceFactor ?? 0f),
            sourcePawn,
            resolvedProfile,
            vector,
            map,
            settingsMultiplier,
            roomAirFactor,
            ContagionDebugVectorKind.AirborneRoom,
            out breakdown);

        if (breakdown != null)
        {
            breakdown.Distance = effectiveRoomDistance;
            breakdown.DistanceFactor = roomAirFactor;
            breakdown.EnclosureFactor = 1f;
            breakdown.ObstructionFactor = 1f;
        }

        return canCompute;
    }

    public static bool TryBuildNominalProximityBreakdown(
        Pawn sourcePawn,
        ResolvedTransmissionProfile resolvedProfile,
        Vector_Proximity vector,
        Map map,
        float settingsMultiplier,
        float distance,
        float distanceFactor,
        float outdoorFactor,
        float cleanlinessFactor,
        out ContagionSpreadBreakdown breakdown)
    {
        bool canCompute = TryBuildNominalBreakdown(
            vector?.baseChancePerCheck ?? 0f,
            sourcePawn,
            resolvedProfile,
            vector,
            map,
            settingsMultiplier,
            distanceFactor * outdoorFactor * cleanlinessFactor,
            ContagionDebugVectorKind.Proximity,
            out breakdown);

        if (breakdown != null)
        {
            breakdown.Distance = distance;
            breakdown.DistanceFactor = distanceFactor;
            breakdown.OutdoorFactor = outdoorFactor;
            breakdown.CleanlinessFactor = cleanlinessFactor;
        }

        return canCompute;
    }

    private static bool TryBuildBreakdown(
        float baseChance,
        Pawn sourcePawn,
        Pawn targetPawn,
        ResolvedTransmissionProfile resolvedProfile,
        TransmissionVector vector,
        Map map,
        float settingsMultiplier,
        float vectorContextFactor,
        ContagionDebugVectorKind vectorKind,
        out ContagionSpreadBreakdown breakdown)
    {
        breakdown = null;
        if (sourcePawn == null || targetPawn == null || resolvedProfile?.Profile == null || vector == null || map == null)
        {
            return false;
        }

        float infectivity = ContagionTransmissionUtility.GetSourceInfectivity(sourcePawn, resolvedProfile, vector);
        float targetFactor = ContagionTransmissionUtility.GetTargetEligibilityFactor(targetPawn, resolvedProfile, sourcePawn, out HediffDef immunityCause);
        string blockReason = targetFactor <= 0f
            ? ContagionTransmissionUtility.GetTargetEligibilityBlockReason(targetPawn, resolvedProfile, sourcePawn)
            : null;
        breakdown = new ContagionSpreadBreakdown
        {
            DiseaseDef = resolvedProfile.DiseaseDef,
            ResolvedProfile = resolvedProfile,
            VectorKind = vectorKind,
            BaseChance = baseChance,
            Infectivity = infectivity,
            TargetEligibilityFactor = targetFactor,
            SettingsMultiplier = settingsMultiplier,
            VectorContextFactor = vectorContextFactor,
            ImmunityCause = immunityCause,
            TargetEligibilityBlockReason = blockReason,
            SeederBonusApplied = targetFactor > 0f
                && ContagionTransmissionUtility.ShouldApplySeederBonus(map, sourcePawn, targetPawn, resolvedProfile)
        };

        return breakdown.FinalChance > 0f;
    }

    private static bool TryBuildNominalBreakdown(
        float baseChance,
        Pawn sourcePawn,
        ResolvedTransmissionProfile resolvedProfile,
        TransmissionVector vector,
        Map map,
        float settingsMultiplier,
        float vectorContextFactor,
        ContagionDebugVectorKind vectorKind,
        out ContagionSpreadBreakdown breakdown)
    {
        breakdown = null;
        if (sourcePawn == null || resolvedProfile?.Profile == null || vector == null || map == null)
        {
            return false;
        }

        breakdown = new ContagionSpreadBreakdown
        {
            DiseaseDef = resolvedProfile.DiseaseDef,
            ResolvedProfile = resolvedProfile,
            VectorKind = vectorKind,
            BaseChance = baseChance,
            Infectivity = ContagionTransmissionUtility.GetSourceInfectivity(sourcePawn, resolvedProfile, vector),
            TargetEligibilityFactor = 1f,
            SettingsMultiplier = settingsMultiplier,
            VectorContextFactor = vectorContextFactor
        };

        return breakdown.FinalChance > 0f;
    }
}
