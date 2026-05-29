using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion.Patches;

[HarmonyPatch(typeof(MouseoverReadout), nameof(MouseoverReadout.MouseoverReadoutOnGUI))]
internal static class Patch_MouseoverReadout_MouseoverReadoutOnGUI
{
    private const float ReadoutWidth = 520f;

    private const float CursorOffsetX = 22f;

    private const float CursorOffsetY = 22f;

    private const float ScreenMargin = 18f;

    public static void Postfix()
    {
        if (!TryGetHoverContext(out Pawn sourcePawn, out Pawn targetPawn, out Contagion_MapTransmissionComponent component))
        {
            component?.DeveloperDiagnostics.ClearHoverPair();
            return;
        }

        List<ContagionSpreadBreakdown> breakdowns = BuildBreakdowns(sourcePawn, targetPawn);
        if (breakdowns.Count == 0)
        {
            component.DeveloperDiagnostics.ClearHoverPair();
            return;
        }

        component.DeveloperDiagnostics.SetHoverPair(sourcePawn, targetPawn);
        DrawBreakdownReadout(sourcePawn, targetPawn, breakdowns);
    }

    private static bool TryGetHoverContext(
        out Pawn sourcePawn,
        out Pawn targetPawn,
        out Contagion_MapTransmissionComponent component)
    {
        sourcePawn = Find.Selector.SingleSelectedThing as Pawn;
        targetPawn = null;
        component = null;

        if (Contagion_Mod.Settings?.DeveloperDiagnosticsEnabled != true
            || sourcePawn == null
            || !sourcePawn.Spawned
            || sourcePawn.Map == null
            || sourcePawn.Dead
            || sourcePawn.Map != Find.CurrentMap)
        {
            return false;
        }

        component = sourcePawn.Map.GetComponent<Contagion_MapTransmissionComponent>();
        if (component == null)
        {
            return false;
        }

        bool hasContagiousProfiles = false;
        foreach (ResolvedTransmissionProfile resolvedProfile in ContagionDiseaseUtility.GetContagiousProfiles(sourcePawn))
        {
            hasContagiousProfiles = true;
            break;
        }

        if (!hasContagiousProfiles)
        {
            return false;
        }

        targetPawn = GetHoveredPawn(sourcePawn.Map, sourcePawn);
        return targetPawn != null;
    }

    private static Pawn GetHoveredPawn(Map map, Pawn sourcePawn)
    {
        IntVec3 mouseCell = UI.MouseCell();
        if (map == null || !mouseCell.InBounds(map) || mouseCell.Fogged(map))
        {
            return null;
        }

        List<Thing> things = mouseCell.GetThingList(map);
        for (int i = 0; i < things.Count; i++)
        {
            Pawn pawn = things[i] as Pawn;
            if (pawn != null && pawn != sourcePawn)
            {
                return pawn;
            }
        }

        return null;
    }

    private static List<ContagionSpreadBreakdown> BuildBreakdowns(Pawn sourcePawn, Pawn targetPawn)
    {
        List<ContagionSpreadBreakdown> breakdowns = new List<ContagionSpreadBreakdown>();
        Map map = sourcePawn.Map;
        float settingsMultiplier = Contagion_Mod.Settings?.EffectiveTransmissionMultiplier ?? 1f;

        foreach (ResolvedTransmissionProfile resolvedProfile in ContagionDiseaseUtility.GetContagiousProfiles(sourcePawn))
        {
            if (ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Airborne airborne)
                && sourcePawn.Position.InHorDistOf(targetPawn.Position, airborne.maxRange))
            {
                float distance = GetHorizontalDistance(sourcePawn.Position, targetPawn.Position);
                bool sourceRoofed = map.roofGrid.Roofed(sourcePawn.Position);
                bool targetRoofed = map.roofGrid.Roofed(targetPawn.Position);
                float enclosureFactor = sourceRoofed && targetRoofed ? 1f : airborne.outdoorFactor;
                float obstructionFactor = GenSight.LineOfSight(sourcePawn.Position, targetPawn.Position, map) ? 1f : airborne.obstructedFactor;
                float maskFactor = ContagionMaskUtility.GetRespiratoryMaskFactor(sourcePawn, targetPawn, airborne);
                float suppressionFactor = ContagionTransmissionUtility.IsSuppressionTarget(targetPawn)
                    ? ContagionTransmissionUtility.GetSpreadSuppressionFactor(map, resolvedProfile)
                    : 1f;
                ContagionDeveloperDiagnosticsUtility.TryBuildAirborneBreakdown(
                    sourcePawn,
                    targetPawn,
                    resolvedProfile,
                    airborne,
                    map,
                    settingsMultiplier,
                    distance,
                    GetDistanceFactor(distance, airborne.distanceFalloffRate),
                    enclosureFactor,
                    obstructionFactor,
                    maskFactor,
                    suppressionFactor,
                    out ContagionSpreadBreakdown breakdown);
                if (breakdown != null)
                {
                    breakdowns.Add(breakdown);
                }
            }

            if (ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Proximity proximity)
                && sourcePawn.Position.InHorDistOf(targetPawn.Position, proximity.maxRange))
            {
                float distance = GetHorizontalDistance(sourcePawn.Position, targetPawn.Position);
                Room sourceRoom = sourcePawn.Position.GetRoom(map);
                Room targetRoom = targetPawn.Position.GetRoom(map);
                float outdoorFactor = IsOutdoors(sourceRoom) || IsOutdoors(targetRoom) ? proximity.outdoorFactor : 1f;
                float cleanlinessFactor = GetLocalCleanlinessFactor(targetPawn, targetRoom, proximity.cleanlinessImpact, proximity.outdoorFilthRadius);
                float maskFactor = ContagionMaskUtility.GetRespiratoryMaskFactor(sourcePawn, targetPawn, proximity);
                float suppressionFactor = ContagionTransmissionUtility.IsSuppressionTarget(targetPawn)
                    ? ContagionTransmissionUtility.GetSpreadSuppressionFactor(map, resolvedProfile)
                    : 1f;
                ContagionDeveloperDiagnosticsUtility.TryBuildProximityBreakdown(
                    sourcePawn,
                    targetPawn,
                    resolvedProfile,
                    proximity,
                    map,
                    settingsMultiplier,
                    distance,
                    GetDistanceFactor(distance, proximity.distanceFalloffRate),
                    outdoorFactor,
                    cleanlinessFactor,
                    maskFactor,
                    suppressionFactor,
                    out ContagionSpreadBreakdown breakdown);
                if (breakdown != null)
                {
                    breakdowns.Add(breakdown);
                }
            }

            if (ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Social social))
            {
                bool sourceRoofed = map.roofGrid.Roofed(sourcePawn.Position);
                bool targetRoofed = map.roofGrid.Roofed(targetPawn.Position);
                float enclosureFactor = sourceRoofed && targetRoofed ? 1f : social.outdoorFactor;
                float obstructionFactor = GenSight.LineOfSight(sourcePawn.Position, targetPawn.Position, map) ? 1f : 0f;
                float maskFactor = ContagionMaskUtility.GetRespiratoryMaskFactor(sourcePawn, targetPawn, social);
                float suppressionFactor = ContagionTransmissionUtility.IsSuppressionTarget(targetPawn)
                    ? ContagionTransmissionUtility.GetSpreadSuppressionFactor(map, resolvedProfile)
                    : 1f;
                ContagionDeveloperDiagnosticsUtility.TryBuildSocialBreakdown(
                    sourcePawn,
                    targetPawn,
                    resolvedProfile,
                    social,
                    map,
                    settingsMultiplier,
                    enclosureFactor,
                    obstructionFactor,
                    maskFactor,
                    suppressionFactor,
                    out ContagionSpreadBreakdown breakdown);
                if (breakdown != null)
                {
                    breakdowns.Add(breakdown);
                }
            }
        }

        return breakdowns;
    }

    private static void DrawBreakdownReadout(Pawn sourcePawn, Pawn targetPawn, List<ContagionSpreadBreakdown> breakdowns)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("Contagion_DeveloperHoverHeader".Translate(sourcePawn.LabelShortCap, targetPawn.LabelShortCap).Resolve());
        for (int i = 0; i < breakdowns.Count; i++)
        {
            ContagionSpreadBreakdown breakdown = breakdowns[i];
            builder.AppendLine("Contagion_DeveloperHoverBreakdownLine".Translate(
                breakdown.DiseaseDef?.LabelCap ?? "?",
                GetVectorLabel(breakdown.VectorKind),
                FormatChance(breakdown.FinalChance)).Resolve());
            builder.AppendLine("Contagion_DeveloperHoverCommonFactors".Translate(
                FormatChance(breakdown.BaseChance),
                FormatMultiplier(breakdown.Infectivity),
                FormatMultiplier(breakdown.SeasonalMultiplier),
                FormatMultiplier(breakdown.TargetEligibilityFactor),
                FormatMultiplier(breakdown.SettingsMultiplier)).Resolve());

            switch (breakdown.VectorKind)
            {
                case ContagionDebugVectorKind.Airborne:
                    builder.AppendLine("Contagion_DeveloperHoverAirborneFactors".Translate(
                        FormatMultiplier(breakdown.DistanceFactor),
                        FormatMultiplier(breakdown.EnclosureFactor),
                        FormatMultiplier(breakdown.ObstructionFactor),
                        FormatMultiplier(breakdown.MaskFactor),
                        FormatMultiplier(breakdown.SuppressionFactor)).Resolve());
                    break;
                case ContagionDebugVectorKind.Proximity:
                    builder.AppendLine("Contagion_DeveloperHoverProximityFactors".Translate(
                        FormatMultiplier(breakdown.DistanceFactor),
                        FormatMultiplier(breakdown.OutdoorFactor),
                        FormatMultiplier(breakdown.CleanlinessFactor),
                        FormatMultiplier(breakdown.MaskFactor),
                        FormatMultiplier(breakdown.SuppressionFactor)).Resolve());
                    break;
                case ContagionDebugVectorKind.Social:
                    builder.AppendLine("Contagion_DeveloperHoverSocialFactors".Translate(
                        FormatMultiplier(breakdown.EnclosureFactor),
                        FormatMultiplier(breakdown.ObstructionFactor),
                        FormatMultiplier(breakdown.MaskFactor),
                        FormatMultiplier(breakdown.SuppressionFactor)).Resolve());
                    break;
            }

            if (breakdown.ImmunityCause != null && breakdown.FinalChance <= 0f)
            {
                builder.AppendLine("Contagion_DeveloperHoverBlockedByImmunity".Translate(breakdown.ImmunityCause.LabelCap).Resolve());
            }

            if (i + 1 < breakdowns.Count)
            {
                builder.AppendLine();
            }
        }

        string text = builder.ToString().TrimEnd();
        float height = Text.CalcHeight(text, ReadoutWidth);
        Vector2 mousePosition = Event.current.mousePosition;
        float x = Mathf.Clamp(mousePosition.x + CursorOffsetX, ScreenMargin, UI.screenWidth - ReadoutWidth - ScreenMargin);
        float y = mousePosition.y + CursorOffsetY;
        if (y + height + ScreenMargin > UI.screenHeight)
        {
            y = Mathf.Max(ScreenMargin, mousePosition.y - height - CursorOffsetY);
        }

        Rect rect = new Rect(x, y, ReadoutWidth, height + 4f);
        GUI.color = new Color(1f, 1f, 1f, 0.84f);
        Widgets.Label(rect, text);
        GUI.color = Color.white;
    }

    private static TaggedString GetVectorLabel(ContagionDebugVectorKind vectorKind)
    {
        return vectorKind switch
        {
            ContagionDebugVectorKind.Airborne => "Contagion_DeveloperHoverVectorAirborne".Translate(),
            ContagionDebugVectorKind.Proximity => "Contagion_DeveloperHoverVectorProximity".Translate(),
            _ => "Contagion_DeveloperHoverVectorSocial".Translate()
        };
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

    private static float GetLocalCleanlinessFactor(Pawn targetPawn, Room room, float cleanlinessImpact, int outdoorFilthRadius)
    {
        if (cleanlinessImpact <= 0f)
        {
            return 1f;
        }

        if (room == null || room.PsychologicallyOutdoors)
        {
            return GetOutdoorFilthCleanlinessFactor(targetPawn.Position, targetPawn.Map, cleanlinessImpact, outdoorFilthRadius);
        }

        float cleanliness = room.GetStat(RoomStatDefOf.Cleanliness);
        return Mathf.Clamp(1f - cleanliness * cleanlinessImpact, 0.1f, 3f);
    }

    private static float GetOutdoorFilthCleanlinessFactor(IntVec3 center, Map map, float cleanlinessImpact, int outdoorFilthRadius)
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
        return Mathf.Clamp(1f + filthDensity * cleanlinessImpact, 0.1f, 3f);
    }

    private static string FormatChance(float chance)
    {
        float clampedChance = Mathf.Clamp01(chance);
        if (clampedChance >= 0.01f)
        {
            return clampedChance.ToStringPercent("0.00");
        }

        if (clampedChance >= 0.001f)
        {
            return clampedChance.ToStringPercent("0.000");
        }

        if (clampedChance <= 0f)
        {
            return "0.000%";
        }

        return "<0.001%";
    }

    private static string FormatMultiplier(float value)
    {
        return value.ToString("0.00") + "x";
    }
}