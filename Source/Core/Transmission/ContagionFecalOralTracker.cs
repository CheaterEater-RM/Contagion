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

    private List<Filth> _contaminatedFilth = new List<Filth>();

    private List<HediffDef> _contaminatedFilthDiseases = new List<HediffDef>();

    private List<int> _contaminatedFilthTicks = new List<int>();

    private List<float> _contaminatedFilthPotencies = new List<float>();

    private List<ThingDef> _contaminatedFilthSourceDefs = new List<ThingDef>();

    private List<IntVec3> _hotspotCells = new List<IntVec3>();

    private List<HediffDef> _hotspotDiseases = new List<HediffDef>();

    private List<int> _hotspotTicks = new List<int>();

    private List<float> _hotspotPotencies = new List<float>();

    private List<ThingDef> _hotspotSourceDefs = new List<ThingDef>();

    public void ExposeData()
    {
        Scribe_Collections.Look(ref _contaminatedFilth, "fecalOralContaminatedFilth", LookMode.Reference);
        Scribe_Collections.Look(ref _contaminatedFilthDiseases, "fecalOralContaminatedFilthDiseases", LookMode.Def);
        Scribe_Collections.Look(ref _contaminatedFilthTicks, "fecalOralContaminatedFilthTicks", LookMode.Value);
        Scribe_Collections.Look(ref _contaminatedFilthPotencies, "fecalOralContaminatedFilthPotencies", LookMode.Value);
        Scribe_Collections.Look(ref _contaminatedFilthSourceDefs, "fecalOralContaminatedFilthSourceDefs", LookMode.Def);
        Scribe_Collections.Look(ref _hotspotCells, "fecalOralHotspotCells", LookMode.Value);
        Scribe_Collections.Look(ref _hotspotDiseases, "fecalOralHotspotDiseases", LookMode.Def);
        Scribe_Collections.Look(ref _hotspotTicks, "fecalOralHotspotTicks", LookMode.Value);
        Scribe_Collections.Look(ref _hotspotPotencies, "fecalOralHotspotPotencies", LookMode.Value);
        Scribe_Collections.Look(ref _hotspotSourceDefs, "fecalOralHotspotSourceDefs", LookMode.Def);

        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            _contaminatedFilth ??= new List<Filth>();
            _contaminatedFilthDiseases ??= new List<HediffDef>();
            _contaminatedFilthTicks ??= new List<int>();
            _contaminatedFilthPotencies ??= new List<float>();
            _contaminatedFilthSourceDefs ??= new List<ThingDef>();
            _hotspotCells ??= new List<IntVec3>();
            _hotspotDiseases ??= new List<HediffDef>();
            _hotspotTicks ??= new List<int>();
            _hotspotPotencies ??= new List<float>();
            _hotspotSourceDefs ??= new List<ThingDef>();
        }
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
                Filth filth = _contaminatedFilth[filthIndex];
                if (filth == null || filth.Destroyed || !filth.Spawned || filth.Map != map)
                {
                    continue;
                }

                if (filth.Position.GetRoom(map) != pawnRoom)
                {
                    continue;
                }

                if (!DiseaseProfileCache.TryGetResolvedProfile(_contaminatedFilthDiseases[filthIndex], out ResolvedTransmissionProfile resolvedProfile)
                    || !ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_FecalOralLiving vector))
                {
                    continue;
                }

                float sourceSpeciesFactor = GetSourceSpeciesFactor(GetListValue(_contaminatedFilthSourceDefs, filthIndex), pawn, resolvedProfile.Profile);
                if (sourceSpeciesFactor <= 0f)
                {
                    continue;
                }

                float potency = GetFilthPotency(filthIndex, vector);
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

                AddOrRefreshHotspot(pawn.Position, resolvedProfile.DiseaseDef, pawn.def, sourceInfectivity, vector);
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

        int index = _contaminatedFilth.IndexOf(filth);
        if (index >= 0)
        {
            _contaminatedFilthDiseases[index] = bestProfile.DiseaseDef;
            _contaminatedFilthTicks[index] = Find.TickManager.TicksGame;
            SetListValue(_contaminatedFilthPotencies, index, bestPotency, 1f);
            SetListValue(_contaminatedFilthSourceDefs, index, sourcePawn.def, null);
        }
        else
        {
            _contaminatedFilth.Add(filth);
            _contaminatedFilthDiseases.Add(bestProfile.DiseaseDef);
            _contaminatedFilthTicks.Add(Find.TickManager.TicksGame);
            _contaminatedFilthPotencies.Add(bestPotency);
            _contaminatedFilthSourceDefs.Add(sourcePawn.def);
            EnforceFilthCap(bestProfile.DiseaseDef, bestVector.maxFilthPerDisease);
        }

        ContagionDiagnostics.Record(ContagionDiagnosticCounter.FecalOralLivingFilthContaminated);
        ContagionDiagnostics.Trace($"Animal filth contaminated: {bestProfile.DiseaseDef.defName} from {sourcePawn.LabelShortCap}.");
        ContagionTrace.Transmission(sourcePawn, filth, bestProfile.DiseaseDef, ContagionDebugVectorKind.FecalOralLiving);
    }

    public void NotifyAnimalIngested(Pawn ingester, ContagionIngestionContext context)
    {
        if (!context.IsValid || !IsAnimalExposureTarget(ingester, context.Map) || _hotspotCells.Count == 0)
        {
            return;
        }

        float transmissionMultiplier = Contagion_Mod.Settings?.EffectiveTransmissionMultiplier ?? 1f;
        for (int hotspotIndex = 0; hotspotIndex < _hotspotCells.Count; hotspotIndex++)
        {
            HediffDef diseaseDef = GetListValue(_hotspotDiseases, hotspotIndex);
            if (diseaseDef == null
                || !DiseaseProfileCache.TryGetResolvedProfile(diseaseDef, out ResolvedTransmissionProfile resolvedProfile)
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

            IntVec3 hotspotCell = _hotspotCells[hotspotIndex];
            if (!context.Cell.InHorDistOf(hotspotCell, vector.hotspotRadius))
            {
                continue;
            }

            float sourceSpeciesFactor = GetSourceSpeciesFactor(GetListValue(_hotspotSourceDefs, hotspotIndex), ingester, resolvedProfile.Profile);
            if (sourceSpeciesFactor <= 0f)
            {
                continue;
            }

            float potency = GetHotspotPotency(hotspotIndex, vector, context.Map);
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
        for (int i = _contaminatedFilth.Count - 1; i >= 0; i--)
        {
            Filth filth = _contaminatedFilth[i];
            HediffDef diseaseDef = GetListValue(_contaminatedFilthDiseases, i);
            bool remove = filth == null || filth.Destroyed || !filth.Spawned || filth.Map != map || diseaseDef == null;

            if (!remove
                && (!DiseaseProfileCache.TryGetResolvedProfile(diseaseDef, out ResolvedTransmissionProfile resolvedProfile)
                    || !ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_FecalOralLiving vector)
                    || GetFilthPotency(i, vector) <= MinPotency))
            {
                remove = true;
            }

            if (remove)
            {
                RemoveFilthAt(i);
            }
        }
    }

    private void CleanupHotspots(Map map)
    {
        for (int i = _hotspotCells.Count - 1; i >= 0; i--)
        {
            HediffDef diseaseDef = GetListValue(_hotspotDiseases, i);
            bool remove = diseaseDef == null || !_hotspotCells[i].InBounds(map);
            if (!remove
                && (!DiseaseProfileCache.TryGetResolvedProfile(diseaseDef, out ResolvedTransmissionProfile resolvedProfile)
                    || !ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_FecalOralEating vector)
                    || IsHotspotExpired(GetListValue(_hotspotTicks, i), vector)
                    || GetHotspotPotency(i, vector, map) <= MinPotency))
            {
                remove = true;
            }

            if (remove)
            {
                RemoveHotspotAt(i);
            }
        }
    }

    private void AddOrRefreshHotspot(IntVec3 cell, HediffDef diseaseDef, ThingDef sourceDef, float potency, Vector_FecalOralEating vector)
    {
        int now = Find.TickManager.TicksGame;
        int mergeRadius = Mathf.Max(0, vector.hotspotMergeRadius);
        for (int i = 0; i < _hotspotCells.Count; i++)
        {
            if (_hotspotDiseases[i] == diseaseDef && _hotspotCells[i].InHorDistOf(cell, mergeRadius))
            {
                _hotspotCells[i] = cell;
                _hotspotTicks[i] = now;
                _hotspotPotencies[i] = Mathf.Max(GetListValue(_hotspotPotencies, i, 1f), potency);
                SetListValue(_hotspotSourceDefs, i, sourceDef, null);
                return;
            }
        }

        _hotspotCells.Add(cell);
        _hotspotDiseases.Add(diseaseDef);
        _hotspotTicks.Add(now);
        _hotspotPotencies.Add(Mathf.Max(0f, potency));
        _hotspotSourceDefs.Add(sourceDef);
        EnforceHotspotCap(diseaseDef, vector.maxHotspotsPerDisease);
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

    private float GetFilthPotency(int index, Vector_FecalOralLiving vector)
    {
        return GetListValue(_contaminatedFilthPotencies, index, 1f)
            * Mathf.Exp(-Mathf.Max(0f, vector.potencyDecayPerDay) * GetElapsedDays(GetListValue(_contaminatedFilthTicks, index)));
    }

    private float GetHotspotPotency(int index, Vector_FecalOralEating vector, Map map)
    {
        float potency = GetListValue(_hotspotPotencies, index, 1f)
            * Mathf.Exp(-Mathf.Max(0f, vector.hotspotDecayPerDay) * GetElapsedDays(GetListValue(_hotspotTicks, index)));
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

    private static float GetSourceSpeciesFactor(ThingDef sourcePawnDef, Pawn target, TransmissionProfile profile)
    {
        RaceProperties sourceRace = sourcePawnDef?.race;
        RaceProperties targetRace = target?.RaceProps;
        if (sourceRace == null || targetRace == null || profile == null)
        {
            return 1f;
        }

        if ((sourceRace.Humanlike && targetRace.Animal) || (sourceRace.Animal && targetRace.Humanlike))
        {
            return Mathf.Max(0f, profile.crossSpeciesTransmissionFactor);
        }

        if (sourceRace.Animal && targetRace.Animal && sourcePawnDef != target.def)
        {
            return Mathf.Max(0f, profile.animalCrossSpeciesFactor);
        }

        return 1f;
    }

    private void EnforceFilthCap(HediffDef diseaseDef, int maxPerDisease)
    {
        if (maxPerDisease <= 0)
        {
            return;
        }

        while (CountDiseaseEntries(_contaminatedFilthDiseases, diseaseDef) > maxPerDisease)
        {
            int oldestIndex = FindOldestDiseaseIndex(_contaminatedFilthDiseases, _contaminatedFilthTicks, diseaseDef);
            if (oldestIndex < 0)
            {
                return;
            }

            RemoveFilthAt(oldestIndex);
        }
    }

    private void EnforceHotspotCap(HediffDef diseaseDef, int maxPerDisease)
    {
        if (maxPerDisease <= 0)
        {
            return;
        }

        while (CountDiseaseEntries(_hotspotDiseases, diseaseDef) > maxPerDisease)
        {
            int oldestIndex = FindOldestDiseaseIndex(_hotspotDiseases, _hotspotTicks, diseaseDef);
            if (oldestIndex < 0)
            {
                return;
            }

            RemoveHotspotAt(oldestIndex);
        }
    }

    private static int CountDiseaseEntries(List<HediffDef> diseases, HediffDef diseaseDef)
    {
        int count = 0;
        for (int i = 0; i < diseases.Count; i++)
        {
            if (diseases[i] == diseaseDef)
            {
                count++;
            }
        }

        return count;
    }

    private static int FindOldestDiseaseIndex(List<HediffDef> diseases, List<int> ticks, HediffDef diseaseDef)
    {
        int oldestIndex = -1;
        int oldestTick = int.MaxValue;
        for (int i = 0; i < diseases.Count; i++)
        {
            if (diseases[i] != diseaseDef)
            {
                continue;
            }

            int tick = GetListValue(ticks, i);
            if (tick < oldestTick)
            {
                oldestTick = tick;
                oldestIndex = i;
            }
        }

        return oldestIndex;
    }

    private void RemoveFilthAt(int index)
    {
        RemoveListValue(_contaminatedFilth, index);
        RemoveListValue(_contaminatedFilthDiseases, index);
        RemoveListValue(_contaminatedFilthTicks, index);
        RemoveListValue(_contaminatedFilthPotencies, index);
        RemoveListValue(_contaminatedFilthSourceDefs, index);
    }

    private void RemoveHotspotAt(int index)
    {
        RemoveListValue(_hotspotCells, index);
        RemoveListValue(_hotspotDiseases, index);
        RemoveListValue(_hotspotTicks, index);
        RemoveListValue(_hotspotPotencies, index);
        RemoveListValue(_hotspotSourceDefs, index);
    }

    private static T GetListValue<T>(List<T> list, int index, T defaultValue = default)
    {
        return list != null && index >= 0 && index < list.Count ? list[index] : defaultValue;
    }

    private static void SetListValue<T>(List<T> list, int index, T value, T defaultValue)
    {
        while (list.Count <= index)
        {
            list.Add(defaultValue);
        }

        list[index] = value;
    }

    private static void RemoveListValue<T>(List<T> list, int index)
    {
        if (list != null && index >= 0 && index < list.Count)
        {
            list.RemoveAt(index);
        }
    }
}
