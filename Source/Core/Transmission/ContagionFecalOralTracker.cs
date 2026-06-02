using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

internal enum ContagionFecalOralEatingContext
{
    None,
    Grazing,
    RawOutdoorGroundFood,
    PreparedOutdoorGroundFood,
    StoredOrIndoorFood
}

internal readonly struct ContagionIngestionContext
{
    public ContagionIngestionContext(
        Map map,
        IntVec3 cell,
        ThingDef foodDef,
        bool isPlant,
        bool isStored,
        bool isIndoor,
        bool isOutdoor)
    {
        Map = map;
        Cell = cell;
        FoodDef = foodDef;
        IsPlant = isPlant;
        IsStored = isStored;
        IsIndoor = isIndoor;
        IsOutdoor = isOutdoor;
    }

    public Map Map { get; }

    public IntVec3 Cell { get; }

    public ThingDef FoodDef { get; }

    public bool IsPlant { get; }

    public bool IsStored { get; }

    public bool IsIndoor { get; }

    public bool IsOutdoor { get; }

    public bool IsValid => Map != null && Cell.IsValid && FoodDef != null;
}

internal sealed class ContagionFecalOralTracker : IExposable
{
    private const int TicksPerDay = 60000;

    private const float MinPotency = 0.03f;

    private const float MinCleanlinessFactor = 0.1f;

    private const float MaxCleanlinessFactor = 3f;

    private ContagionFilthStore _contaminatedFilth = new ContagionFilthStore();

    private ContagionHotspotStore _hotspots = new ContagionHotspotStore();

    public void ExposeData()
    {
        _contaminatedFilth ??= new ContagionFilthStore();
        _contaminatedFilth.ExposeData("fecalOralContaminated");
        _hotspots ??= new ContagionHotspotStore();
        _hotspots.ExposeData("fecalOralHotspot");
    }

    public void Cleanup(Map map)
    {
        CleanupFilth(map);
        CleanupHotspots(map);
    }

    public void RunFecalOralLivingExposurePass(IReadOnlyList<Pawn> spawnedPawns, Map map)
    {
        if (_contaminatedFilth.Count == 0 || spawnedPawns == null || map == null)
        {
            return;
        }

        float transmissionMultiplier = Contagion_Mod.Settings?.EffectiveTransmissionMultiplier ?? 1f;
        for (int pawnIndex = 0; pawnIndex < spawnedPawns.Count; pawnIndex++)
        {
            Pawn pawn = spawnedPawns[pawnIndex];
            if (!IsAnimalExposureTarget(pawn, map))
            {
                continue;
            }

            Room pawnRoom = pawn.Position.GetRoom(map);
            if (pawnRoom == null || ContagionTransmissionUtility.IsOutdoors(pawnRoom))
            {
                continue;
            }

            for (int filthIndex = 0; filthIndex < _contaminatedFilth.Count; filthIndex++)
            {
                ContagionFilthEntry entry = _contaminatedFilth.Get(filthIndex);
                Filth filth = entry.Filth;
                if (filth == null || filth.Destroyed || !filth.Spawned || filth.Map != map)
                {
                    continue;
                }

                if (filth.Position.GetRoom(map) != pawnRoom)
                {
                    continue;
                }

                if (!DiseaseProfileCache.TryGetResolvedProfile(entry.DiseaseDef, out ResolvedTransmissionProfile resolvedProfile)
                    || !ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_FecalOralLiving vector))
                {
                    continue;
                }

                float sourceSpeciesFactor = ContagionTransmissionUtility.GetSourceSpeciesFactor(entry.SourceDef, pawn, resolvedProfile, map);
                if (sourceSpeciesFactor <= 0f)
                {
                    continue;
                }

                float potency = GetFilthPotency(entry, vector);
                if (potency <= MinPotency)
                {
                    continue;
                }

                float thicknessFactor = 1f + Mathf.Max(0, filth.thickness - 1) * Mathf.Max(0f, vector.thicknessFactor);
                float cleanlinessFactor = GetRoomCleanlinessFactor(pawnRoom, vector.roomCleanlinessImpact);
                float baseChance = vector.baseChancePerCheck * potency * thicknessFactor * cleanlinessFactor * sourceSpeciesFactor;
                if (baseChance <= 0f)
                {
                    continue;
                }

                ContagionDiagnostics.Record(ContagionDiagnosticCounter.FecalOralLivingAttempted);
                float chance = ContagionTransmissionUtility.BuildSeederChance(
                    baseChance,
                    pawn,
                    resolvedProfile,
                    map,
                    transmissionMultiplier,
                    out HediffDef _);
                float finalChance = Mathf.Clamp01(chance);
                bool passed = Rand.Chance(finalChance);
                bool seeded = false;
                if (passed)
                {
                    seeded = ContagionDiseaseUtility.TrySeedIncubation(
                        pawn,
                        resolvedProfile.DiseaseDef,
                        resolvedProfile.PartsToAffect,
                        null,
                        ContagionDiagnosticOrigin.Spread,
                        ContagionSeedSource.Environmental,
                        out HediffDef _);
                    if (seeded)
                    {
                        ContagionDiagnostics.Record(ContagionDiagnosticCounter.FecalOralLivingSeeded);
                        ContagionDiagnostics.Trace($"Fecal-oral living transmission: {resolvedProfile.DiseaseDef.defName} on {pawn.LabelShortCap} from barn filth.");
                        ContagionTrace.Transmission(filth, pawn, resolvedProfile.DiseaseDef, ContagionDebugVectorKind.FecalOralLiving);
                    }
                }

                ContagionDiagnostics.LogRoll(ContagionDebugVectorKind.FecalOralLiving, filth, pawn, resolvedProfile.DiseaseDef, finalChance, seeded);
                if (seeded)
                {
                    break;
                }
            }
        }
    }

    public void RunFecalOralEatingSheddingPass(IReadOnlyList<Pawn> spawnedPawns, Map map)
    {
        if (spawnedPawns == null || map == null)
        {
            return;
        }

        float transmissionMultiplier = Contagion_Mod.Settings?.EffectiveTransmissionMultiplier ?? 1f;
        for (int pawnIndex = 0; pawnIndex < spawnedPawns.Count; pawnIndex++)
        {
            Pawn pawn = spawnedPawns[pawnIndex];
            if (pawn == null
                || pawn.Dead
                || !pawn.Spawned
                || pawn.Map != map
                || pawn.RaceProps?.Animal != true
                || !IsOutdoorCell(pawn.Position, map))
            {
                continue;
            }

            foreach (ResolvedTransmissionProfile resolvedProfile in ContagionDiseaseUtility.GetContagiousProfiles(pawn))
            {
                if (!ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_FecalOralEating vector))
                {
                    continue;
                }

                float sourceInfectivity = ContagionTransmissionUtility.GetSourceInfectivity(pawn, resolvedProfile, vector);
                if (sourceInfectivity <= 0f)
                {
                    continue;
                }

                float chance = vector.hotspotShedChancePerCheck
                    * sourceInfectivity
                    * ContagionTransmissionUtility.GetSeasonalMultiplier(map, resolvedProfile.Profile)
                    * transmissionMultiplier;
                if (!Rand.Chance(Mathf.Clamp01(chance)))
                {
                    continue;
                }

                _hotspots.AddOrRefresh(
                    pawn.Position,
                    resolvedProfile.DiseaseDef,
                    pawn.def,
                    sourceInfectivity,
                    vector.hotspotMergeRadius,
                    vector.maxHotspotsPerDisease);
                ContagionDiagnostics.Record(ContagionDiagnosticCounter.FecalOralEatingHotspotCreated);
                ContagionDiagnostics.Trace($"Fecal-oral eating hotspot: {resolvedProfile.DiseaseDef.defName} near {pawn.LabelShortCap}.");
                ContagionTrace.TransmissionToCell(pawn, pawn.PositionHeld, resolvedProfile.DiseaseDef, ContagionDebugVectorKind.FecalOralEating);
            }
        }
    }

    public void NotifyAnimalFilthCreated(Filth filth, Pawn sourcePawn, Map map)
    {
        if (filth == null
            || sourcePawn == null
            || map == null
            || filth.def != ThingDefOf.Filth_AnimalFilth
            || sourcePawn.RaceProps?.Animal != true
            || !IsBarnFilthCell(filth.Position, map))
        {
            return;
        }

        ResolvedTransmissionProfile bestProfile = null;
        Vector_FecalOralLiving bestVector = null;
        float bestPotency = 0f;
        foreach (ResolvedTransmissionProfile resolvedProfile in ContagionDiseaseUtility.GetContagiousProfiles(sourcePawn))
        {
            if (!ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_FecalOralLiving vector))
            {
                continue;
            }

            float potency = ContagionTransmissionUtility.GetSourceInfectivity(sourcePawn, resolvedProfile, vector);
            if (potency > bestPotency)
            {
                bestProfile = resolvedProfile;
                bestVector = vector;
                bestPotency = potency;
            }
        }

        if (bestProfile == null || bestVector == null || bestPotency <= 0f || !Rand.Chance(Mathf.Clamp01(bestVector.filthContaminationChance)))
        {
            return;
        }

        _contaminatedFilth.AddOrUpdate(filth, bestProfile.DiseaseDef, sourcePawn.def, bestPotency);
        _contaminatedFilth.EnforceDiseaseCap(bestProfile.DiseaseDef, bestVector.maxFilthPerDisease);

        ContagionDiagnostics.Record(ContagionDiagnosticCounter.FecalOralLivingFilthContaminated);
        ContagionDiagnostics.Trace($"Animal filth contaminated: {bestProfile.DiseaseDef.defName} from {sourcePawn.LabelShortCap}.");
        ContagionTrace.Transmission(sourcePawn, filth, bestProfile.DiseaseDef, ContagionDebugVectorKind.FecalOralLiving);
    }

    public void NotifyAnimalIngested(Pawn ingester, ContagionIngestionContext context)
    {
        if (!context.IsValid || !IsAnimalExposureTarget(ingester, context.Map) || _hotspots.Count == 0)
        {
            return;
        }

        float transmissionMultiplier = Contagion_Mod.Settings?.EffectiveTransmissionMultiplier ?? 1f;
        for (int hotspotIndex = 0; hotspotIndex < _hotspots.Count; hotspotIndex++)
        {
            ContagionHotspotEntry hotspot = _hotspots.Get(hotspotIndex);
            if (hotspot?.DiseaseDef == null
                || !DiseaseProfileCache.TryGetResolvedProfile(hotspot.DiseaseDef, out ResolvedTransmissionProfile resolvedProfile)
                || !ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_FecalOralEating vector))
            {
                continue;
            }

            ContagionFecalOralEatingContext eatingContext = ClassifyEatingContext(context, vector);
            float contextFactor = GetEatingContextFactor(eatingContext, vector);
            if (contextFactor <= 0f)
            {
                continue;
            }

            IntVec3 hotspotCell = hotspot.Cell;
            if (!context.Cell.InHorDistOf(hotspotCell, vector.hotspotRadius))
            {
                continue;
            }

            float sourceSpeciesFactor = ContagionTransmissionUtility.GetSourceSpeciesFactor(hotspot.SourceDef, ingester, resolvedProfile, context.Map);
            if (sourceSpeciesFactor <= 0f)
            {
                continue;
            }

            float potency = GetHotspotPotency(hotspot, vector, context.Map);
            if (potency <= MinPotency)
            {
                continue;
            }

            float distance = ContagionTransmissionUtility.GetHorizontalDistance(context.Cell, hotspotCell);
            float distanceFactor = ContagionTransmissionUtility.GetDistanceFactor(distance, vector.distanceFalloffRate);
            float baseChance = vector.baseChancePerIngestion * contextFactor * potency * distanceFactor * sourceSpeciesFactor;
            if (baseChance <= 0f)
            {
                continue;
            }

            ContagionDiagnostics.Record(ContagionDiagnosticCounter.FecalOralEatingAttempted);
            float chance = ContagionTransmissionUtility.BuildSeederChance(
                baseChance,
                ingester,
                resolvedProfile,
                context.Map,
                transmissionMultiplier,
                out HediffDef _);
            float finalChance = Mathf.Clamp01(chance);
            bool passed = Rand.Chance(finalChance);
            bool seeded = false;
            if (passed)
            {
                seeded = ContagionDiseaseUtility.TrySeedIncubation(
                    ingester,
                    resolvedProfile.DiseaseDef,
                    resolvedProfile.PartsToAffect,
                    null,
                    ContagionDiagnosticOrigin.Spread,
                    ContagionSeedSource.Environmental,
                    out HediffDef _);
                if (seeded)
                {
                    ContagionDiagnostics.Record(ContagionDiagnosticCounter.FecalOralEatingSeeded);
                    ContagionDiagnostics.Trace($"Fecal-oral eating transmission: {resolvedProfile.DiseaseDef.defName} on {ingester.LabelShortCap} ({eatingContext}).");
                    ContagionTrace.SourceAtCell(hotspotCell, ingester, resolvedProfile.DiseaseDef, ContagionDebugVectorKind.FecalOralEating);
                }
            }

            ContagionDiagnostics.LogRoll(ContagionDebugVectorKind.FecalOralEating, null, ingester, resolvedProfile.DiseaseDef, finalChance, seeded);
            if (seeded)
            {
                return;
            }
        }
    }

    private void CleanupFilth(Map map)
    {
        _contaminatedFilth.Cleanup(
            map,
            entry => !DiseaseProfileCache.TryGetResolvedProfile(entry.DiseaseDef, out ResolvedTransmissionProfile resolvedProfile)
                || !ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_FecalOralLiving vector)
                || GetFilthPotency(entry, vector) <= MinPotency);
    }

    private void CleanupHotspots(Map map)
    {
        _hotspots.Cleanup(
            map,
            hotspot => !DiseaseProfileCache.TryGetResolvedProfile(hotspot.DiseaseDef, out ResolvedTransmissionProfile resolvedProfile)
                || !ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_FecalOralEating vector)
                || IsHotspotExpired(hotspot.Tick, vector)
                || GetHotspotPotency(hotspot, vector, map) <= MinPotency);
    }

    private ContagionFecalOralEatingContext ClassifyEatingContext(ContagionIngestionContext context, Vector_FecalOralEating vector)
    {
        if (!context.IsOutdoor)
        {
            return ContagionFecalOralEatingContext.StoredOrIndoorFood;
        }

        if (context.IsPlant)
        {
            return ContagionFecalOralEatingContext.Grazing;
        }

        if (context.IsStored)
        {
            return ContagionFecalOralEatingContext.StoredOrIndoorFood;
        }

        if (IsPreparedAnimalFeed(context.FoodDef))
        {
            return ContagionFecalOralEatingContext.PreparedOutdoorGroundFood;
        }

        return ContagionFecalOralEatingContext.RawOutdoorGroundFood;
    }

    private static float GetEatingContextFactor(ContagionFecalOralEatingContext context, Vector_FecalOralEating vector)
    {
        return context switch
        {
            ContagionFecalOralEatingContext.Grazing => vector.grazingFactor,
            ContagionFecalOralEatingContext.RawOutdoorGroundFood => vector.rawOutdoorGroundFoodFactor,
            ContagionFecalOralEatingContext.PreparedOutdoorGroundFood => vector.preparedOutdoorGroundFoodFactor,
            ContagionFecalOralEatingContext.StoredOrIndoorFood => vector.storedOrIndoorFoodFactor,
            _ => 0f
        };
    }

    private static bool IsPreparedAnimalFeed(ThingDef foodDef)
    {
        if (foodDef == null)
        {
            return false;
        }

        FoodTypeFlags foodType = foodDef.ingestible?.foodType ?? FoodTypeFlags.None;
        return foodDef.IsProcessedFood
            || (foodType & FoodTypeFlags.Kibble) != 0
            || (foodType & FoodTypeFlags.Processed) != 0
            || foodDef.defName == "Hay";
    }

    private static bool IsAnimalExposureTarget(Pawn pawn, Map map)
    {
        return pawn != null
            && !pawn.Dead
            && pawn.Spawned
            && pawn.Map == map
            && pawn.RaceProps?.Animal == true;
    }

    private static bool IsBarnFilthCell(IntVec3 cell, Map map)
    {
        if (!cell.InBounds(map))
        {
            return false;
        }

        if (cell.Roofed(map))
        {
            return true;
        }

        Room room = cell.GetRoom(map);
        return room != null
            && !room.PsychologicallyOutdoors
            && !room.TouchesMapEdge
            && !room.UsesOutdoorTemperature;
    }

    private static bool IsOutdoorCell(IntVec3 cell, Map map)
    {
        if (!cell.InBounds(map) || cell.Roofed(map))
        {
            return false;
        }

        Room room = cell.GetRoom(map);
        return room == null || room.PsychologicallyOutdoors || room.UsesOutdoorTemperature;
    }

    private static float GetRoomCleanlinessFactor(Room room, float cleanlinessImpact)
    {
        if (room == null || cleanlinessImpact <= 0f)
        {
            return 1f;
        }

        float cleanliness = room.GetStat(RoomStatDefOf.Cleanliness);
        return Mathf.Clamp(1f - cleanliness * cleanlinessImpact, MinCleanlinessFactor, MaxCleanlinessFactor);
    }

    private static float GetFilthPotency(ContagionFilthEntry entry, Vector_FecalOralLiving vector)
    {
        return Mathf.Max(0f, entry.Potency)
            * Mathf.Exp(-Mathf.Max(0f, vector.potencyDecayPerDay) * GetElapsedDays(entry.Tick));
    }

    private static float GetHotspotPotency(ContagionHotspotEntry hotspot, Vector_FecalOralEating vector, Map map)
    {
        float potency = Mathf.Max(0f, hotspot.Potency)
            * Mathf.Exp(-Mathf.Max(0f, vector.hotspotDecayPerDay) * GetElapsedDays(hotspot.Tick));
        if (map != null && map.weatherManager.RainRate > 0.05f)
        {
            potency *= Mathf.Clamp01(vector.rainPotencyFactor);
        }

        if (map != null && map.mapTemperature.OutdoorTemp <= vector.freezingTemperature)
        {
            potency *= Mathf.Clamp01(vector.freezingPotencyFactor);
        }

        return potency;
    }

    private static bool IsHotspotExpired(int createdTick, Vector_FecalOralEating vector)
    {
        if (vector.hotspotDurationDays <= 0f)
        {
            return false;
        }

        return Find.TickManager.TicksGame - createdTick > Mathf.RoundToInt(vector.hotspotDurationDays * TicksPerDay);
    }

    private static float GetElapsedDays(int tick)
    {
        return Mathf.Max(0f, (Find.TickManager.TicksGame - tick) / (float)TicksPerDay);
    }

}
