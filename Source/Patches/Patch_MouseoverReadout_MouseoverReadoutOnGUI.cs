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

    private static readonly Dictionary<int, float> PathDistanceByCell = new Dictionary<int, float>();

    private static readonly Queue<IntVec3> PathOpenCells = new Queue<IntVec3>();

    public static void Postfix()
    {
        if (!TryGetHoverContext(out Pawn sourcePawn, out Pawn targetPawn, out Contagion_MapTransmissionComponent component))
        {
            component?.DeveloperDiagnostics.ClearHoverPair();
            // No pawn-source context — fall back to a corpse infectivity readout if hovering one.
            TryDrawCorpseReadout();
            return;
        }

        List<ContagionSpreadBreakdown> breakdowns = BuildBreakdowns(sourcePawn, targetPawn);
        if (breakdowns.Count == 0)
        {
            component.DeveloperDiagnostics.SetHoverPair(sourcePawn, targetPawn);
            DrawNoDirectVectorReadout(sourcePawn, targetPawn);
            return;
        }

        component.DeveloperDiagnostics.SetHoverPair(sourcePawn, targetPawn);
        DrawBreakdownReadout(sourcePawn, targetPawn, breakdowns);
    }

    // When the cursor is over an infectious corpse, show its infectivity: disease, flea potency
    // and peak radial chance, and fluid (contact) potency. Mirrors the pawn hover breakdown.
    private static void TryDrawCorpseReadout()
    {
        if (Contagion_Mod.Settings?.DeveloperDiagnosticsEnabled != true)
        {
            return;
        }

        Map map = Find.CurrentMap;
        IntVec3 mouseCell = UI.MouseCell();
        if (map == null || !mouseCell.InBounds(map) || mouseCell.Fogged(map))
        {
            return;
        }

        Corpse corpse = null;
        List<Thing> things = mouseCell.GetThingList(map);
        for (int i = 0; i < things.Count; i++)
        {
            if (things[i] is Corpse candidate
                && candidate.TryGetComp<Comp_InfectedCorpse>() is Comp_InfectedCorpse comp
                && (comp.IsInfected || comp.IsSuspectedInfected))
            {
                corpse = candidate;
                break;
            }
        }

        if (corpse == null)
        {
            return;
        }

        Comp_InfectedCorpse infectedComp = corpse.TryGetComp<Comp_InfectedCorpse>();
        string diseaseLabel = infectedComp.InfectedDiseaseDef != null && infectedComp.DiseaseIdentified
            ? infectedComp.InfectedDiseaseDef.LabelCap.Resolve()
            : "Contagion_CorpseInfectivityUnknownDisease".Translate().Resolve();

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("Contagion_CorpseInfectivityHeader".Translate(diseaseLabel).Resolve());

        bool anyVector = false;
        if (ContagionCorpseExposureUtility.TryGetCorpseFleaVector(corpse, out _, out Vector_CorpseFlea fleaVector)
            && fleaVector.maxRange > 0f)
        {
            anyVector = true;
            float ageDays = Mathf.Max(0f, corpse.Age / 60000f);
            float potency = Mathf.Max(
                ContagionCorpseExposureUtility.FindCorpseFleas(corpse)?.Severity ?? 0f,
                ContagionCorpseExposureUtility.EvaluateCorpseFleaAgePotency(fleaVector, ageDays));
            float peakChance = Mathf.Clamp01(fleaVector.baseChancePerCheck * potency);
            builder.AppendLine("Contagion_CorpseInfectivityFlea".Translate(
                potency.ToString("0.00"),
                FormatChance(peakChance),
                fleaVector.maxRange.ToString("0.#")).Resolve());
        }

        if (ContagionCorpseExposureUtility.TryGetCorpseFluidVector(corpse, out _, out Vector_CorpseFluid fluidVector))
        {
            anyVector = true;
            float fluidPotency = ContagionCorpseExposureUtility.GetCorpseFluidPotency(corpse, fluidVector);
            builder.AppendLine("Contagion_CorpseInfectivityFluid".Translate(fluidPotency.ToString("0.00")).Resolve());
        }

        if (!anyVector)
        {
            builder.AppendLine("Contagion_CorpseInfectivityNone".Translate().Resolve());
        }

        DrawReadoutText(builder.ToString().TrimEnd());
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
        foreach (ResolvedTransmissionProfile resolvedProfile in GetDiagnosticProfiles(sourcePawn))
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

        foreach (ResolvedTransmissionProfile resolvedProfile in GetDiagnosticProfiles(sourcePawn))
        {
            if (ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Airborne airborne)
                && sourcePawn.Position.InHorDistOf(targetPawn.Position, airborne.maxRange))
            {
                float distance = ContagionTransmissionUtility.GetHorizontalDistance(sourcePawn.Position, targetPawn.Position);
                bool sourceRoofed = map.roofGrid.Roofed(sourcePawn.Position);
                bool targetRoofed = map.roofGrid.Roofed(targetPawn.Position);
                float enclosureFactor = sourceRoofed && targetRoofed ? 1f : airborne.outdoorFactor;
                float obstructionFactor = GenSight.LineOfSight(sourcePawn.Position, targetPawn.Position, map) ? 1f : airborne.obstructedFactor;
                float maskFactor = ContagionMaskUtility.GetRespiratoryMaskFactor(sourcePawn, targetPawn, airborne);
                float suppressionFactor = ContagionTransmissionUtility.GetSpreadSuppressionFactor(map, resolvedProfile, targetPawn);
                ContagionDeveloperDiagnosticsUtility.TryBuildAirborneBreakdown(
                    sourcePawn,
                    targetPawn,
                    resolvedProfile,
                    airborne,
                    map,
                    settingsMultiplier,
                    distance,
                    ContagionTransmissionUtility.GetDistanceFactor(distance, airborne.distanceFalloffRate),
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

            if (ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Airborne roomAirborne)
                && ContagionTransmissionUtility.TryGetRoomAirFactor(
                    map,
                    sourcePawn.Position,
                    targetPawn.Position,
                    roomAirborne,
                    out float effectiveRoomDistance,
                    out float roomAirFactor))
            {
                float maskFactor = ContagionMaskUtility.GetRespiratoryMaskFactor(sourcePawn, targetPawn, roomAirborne);
                float suppressionFactor = ContagionTransmissionUtility.GetSpreadSuppressionFactor(map, resolvedProfile, targetPawn);
                ContagionDeveloperDiagnosticsUtility.TryBuildAirborneRoomBreakdown(
                    sourcePawn,
                    targetPawn,
                    resolvedProfile,
                    roomAirborne,
                    map,
                    settingsMultiplier,
                    effectiveRoomDistance,
                    roomAirFactor,
                    maskFactor,
                    suppressionFactor,
                    out ContagionSpreadBreakdown breakdown);
                if (breakdown != null)
                {
                    breakdowns.Add(breakdown);
                }
            }

            if (ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Proximity proximity)
                && TryGetPathDistance(map, sourcePawn.Position, targetPawn.Position, proximity.maxRange, out float pathDistance))
            {
                Room sourceRoom = sourcePawn.Position.GetRoom(map);
                Room targetRoom = targetPawn.Position.GetRoom(map);
                float outdoorFactor = ContagionTransmissionUtility.IsOutdoors(sourceRoom) || ContagionTransmissionUtility.IsOutdoors(targetRoom)
                    ? proximity.outdoorFactor
                    : 1f;
                float cleanlinessFactor = ContagionTransmissionUtility.GetLocalCleanlinessFactor(
                    targetPawn.Position, targetRoom, map, proximity.cleanlinessImpact, proximity.outdoorFilthRadius);
                float maskFactor = ContagionMaskUtility.GetRespiratoryMaskFactor(sourcePawn, targetPawn, proximity);
                float suppressionFactor = ContagionTransmissionUtility.GetSpreadSuppressionFactor(map, resolvedProfile, targetPawn);
                ContagionDeveloperDiagnosticsUtility.TryBuildProximityBreakdown(
                    sourcePawn,
                    targetPawn,
                    resolvedProfile,
                    proximity,
                    map,
                    settingsMultiplier,
                    pathDistance,
                    ContagionTransmissionUtility.GetDistanceFactor(pathDistance, proximity.distanceFalloffRate),
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
                float suppressionFactor = ContagionTransmissionUtility.GetSpreadSuppressionFactor(map, resolvedProfile, targetPawn);
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

    private static IEnumerable<ResolvedTransmissionProfile> GetDiagnosticProfiles(Pawn pawn)
    {
        if (pawn?.health?.hediffSet == null)
        {
            yield break;
        }

        HashSet<HediffDef> seenDiseases = new HashSet<HediffDef>();
        List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
        for (int i = 0; i < hediffs.Count; i++)
        {
            Hediff hediff = hediffs[i];
            HediffDef diseaseDef = hediff is Hediff_ContagionIncubation incubation
                ? incubation.TargetDiseaseDef
                : hediff?.def;
            if (diseaseDef == null
                || !DiseaseProfileCache.TryGetResolvedProfile(diseaseDef, out ResolvedTransmissionProfile resolvedProfile)
                || resolvedProfile?.Profile?.HasVectors != true
                || !seenDiseases.Add(resolvedProfile.DiseaseDef))
            {
                continue;
            }

            yield return resolvedProfile;
        }
    }

    private static bool TryGetPathDistance(Map map, IntVec3 source, IntVec3 target, float maxRange, out float pathDistance)
    {
        pathDistance = 0f;
        if (map == null || !source.InBounds(map) || !target.InBounds(map) || maxRange < 0f)
        {
            return false;
        }

        ContagionTransmissionUtility.CollectReachablePathDistances(
            map,
            source,
            maxRange,
            PathDistanceByCell,
            PathOpenCells);

        return PathDistanceByCell.TryGetValue(map.cellIndices.CellToIndex(target), out pathDistance)
            && pathDistance <= maxRange;
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
                GetVectorLabel(breakdown),
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
                case ContagionDebugVectorKind.AirborneRoom:
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
            else if (!breakdown.TargetEligibilityBlockReason.NullOrEmpty() && breakdown.FinalChance <= 0f)
            {
                builder.AppendLine("Contagion_DeveloperHoverBlockedReason".Translate(breakdown.TargetEligibilityBlockReason).Resolve());
            }

            if (i + 1 < breakdowns.Count)
            {
                builder.AppendLine();
            }
        }

        DrawReadoutText(builder.ToString().TrimEnd());
    }

    private static void DrawNoDirectVectorReadout(Pawn sourcePawn, Pawn targetPawn)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("Contagion_DeveloperHoverHeader".Translate(sourcePawn.LabelShortCap, targetPawn.LabelShortCap).Resolve());
        builder.AppendLine("Contagion_DeveloperHoverNoDirectVector".Translate().Resolve());
        DrawReadoutText(builder.ToString().TrimEnd());
    }

    private static void DrawReadoutText(string text)
    {
        if (text.NullOrEmpty())
        {
            return;
        }

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

    private static TaggedString GetVectorLabel(ContagionSpreadBreakdown breakdown)
    {
        if (breakdown?.VectorKind == ContagionDebugVectorKind.Proximity
            && IsPlagueFleaTransfer(breakdown.DiseaseDef))
        {
            return "live flea transfer";
        }

        return breakdown?.VectorKind switch
        {
            ContagionDebugVectorKind.Airborne => "Contagion_DeveloperHoverVectorAirborne".Translate(),
            ContagionDebugVectorKind.AirborneRoom => "room air",
            ContagionDebugVectorKind.Proximity => "Contagion_DeveloperHoverVectorProximity".Translate(),
            _ => "Contagion_DeveloperHoverVectorSocial".Translate()
        };
    }

    private static bool IsPlagueFleaTransfer(HediffDef diseaseDef)
    {
        string defName = diseaseDef?.defName;
        return defName == "Plague" || defName == "Animal_Plague";
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
