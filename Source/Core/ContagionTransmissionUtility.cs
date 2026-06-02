using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

public enum ContagionCaseTrack
{
    None,
    Human,
    Animal
}

public static class ContagionTransmissionUtility
{
    private static readonly SimpleCurve DefaultActiveInfectivityCurve = CreateDefaultActiveInfectivityCurve();

    public static float GetSourceInfectivity(Pawn source, ResolvedTransmissionProfile resolvedProfile, TransmissionVector vector = null)
    {
        if (source?.health?.hediffSet == null || resolvedProfile?.Profile == null)
        {
            return 0f;
        }

        HediffDef sourceDef = resolvedProfile.ResolveHediffForPawn(source);
        Hediff_ContagionIncubation incubation = ContagionDiseaseUtility.FindIncubation(source, sourceDef);
        if (incubation != null)
        {
            SimpleCurve curve = vector?.incubationInfectivityCurveOverride ?? resolvedProfile.Profile.incubationInfectivityCurve;
            return curve == null ? 0f : Mathf.Max(0f, curve.Evaluate(incubation.Severity)) * GetSourceFactorProduct(source, resolvedProfile.Profile);
        }

        List<Hediff> hediffs = source.health.hediffSet.hediffs;
        for (int i = 0; i < hediffs.Count; i++)
        {
            Hediff hediff = hediffs[i];
            if (hediff?.def != sourceDef)
            {
                continue;
            }

            if (IsFullyImmuneActiveDisease(hediff))
            {
                return 0f;
            }

            SimpleCurve curve = vector?.activeInfectivityCurveOverride
                ?? resolvedProfile.Profile.activeInfectivityCurve
                ?? DefaultActiveInfectivityCurve;
            return Mathf.Max(0f, curve.Evaluate(hediff.Severity)) * GetSourceFactorProduct(source, resolvedProfile.Profile);
        }

        return 0f;
    }

    public static float GetTargetEligibilityFactor(
        Pawn target,
        ResolvedTransmissionProfile resolvedProfile,
        Pawn source,
        out HediffDef immunityCause)
    {
        immunityCause = null;
        if (target == null || target.Dead || resolvedProfile?.Profile == null)
        {
            return 0f;
        }

        if (!resolvedProfile.Profile.CanTransmitBetween(source, target, out float speciesFactor))
        {
            return 0f;
        }

        float animalSpeciesFactor = GetAnimalCrossSpeciesFactor(source?.Map ?? target.Map, source?.def, target, resolvedProfile);
        if (animalSpeciesFactor <= 0f)
        {
            return 0f;
        }

        if (target.health?.hediffSet == null || target.health.immunity == null)
        {
            return 0f;
        }

        HediffDef targetDef = resolvedProfile.ResolveHediffForPawn(target);
        if (target.health.hediffSet.HasHediff(targetDef)
            || ContagionDiseaseUtility.FindIncubation(target, targetDef) != null
            || ContagionDiseaseUtility.HasTemporaryImmunity(target, targetDef))
        {
            return 0f;
        }

        float vanillaFactor = GetVanillaContractFactor(target, resolvedProfile, out immunityCause);
        if (vanillaFactor <= 0f)
        {
            return 0f;
        }

        return speciesFactor
            * animalSpeciesFactor
            * vanillaFactor
            * GetSusceptibilityFactorProduct(target, resolvedProfile.Profile);
    }

    public static string GetTargetEligibilityBlockReason(
        Pawn target,
        ResolvedTransmissionProfile resolvedProfile,
        Pawn source)
    {
        if (target == null)
        {
            return "no target";
        }

        if (target.Dead)
        {
            return "target is dead";
        }

        if (resolvedProfile?.Profile == null)
        {
            return "no disease profile";
        }

        if (!resolvedProfile.Profile.CanTransmitBetween(source, target, out float speciesFactor))
        {
            return speciesFactor <= 0f
                ? "species barrier"
                : "profile cannot affect target";
        }

        if (GetAnimalCrossSpeciesFactor(source?.Map ?? target.Map, source?.def, target, resolvedProfile) <= 0f)
        {
            return "species barrier";
        }

        if (target.health?.hediffSet == null || target.health.immunity == null)
        {
            return "no health/immunity tracker";
        }

        HediffDef targetDef = resolvedProfile.ResolveHediffForPawn(target);
        if (target.health.hediffSet.HasHediff(targetDef))
        {
            return "already has disease";
        }

        if (ContagionDiseaseUtility.FindIncubation(target, targetDef) != null)
        {
            return "already incubating";
        }

        if (ContagionDiseaseUtility.HasTemporaryImmunity(target, targetDef))
        {
            return "temporary immunity";
        }

        float vanillaFactor = GetVanillaContractFactor(target, resolvedProfile, out HediffDef immunityCause);
        if (vanillaFactor <= 0f)
        {
            return immunityCause != null
                ? "immune: " + immunityCause.LabelCap
                : "vanilla contract factor is 0";
        }

        if (GetSusceptibilityFactorProduct(target, resolvedProfile.Profile) <= 0f)
        {
            return "susceptibility factor is 0";
        }

        return null;
    }

    public static float GetSeasonalMultiplier(Map map, TransmissionProfile profile)
    {
        SeasonalInfectivity seasonal = profile?.seasonalInfectivity;
        if (map == null || seasonal == null)
        {
            return 1f;
        }

        Vector2 longLat = Find.WorldGrid.LongLatOf(map.Tile);
        float yearPct = GenDate.YearPercent(Find.TickManager.TicksAbs, longLat.x);
        SeasonUtility.GetSeason(
            yearPct,
            longLat.y,
            out float spring,
            out float summer,
            out float fall,
            out float winter,
            out float permanentSummer,
            out float permanentWinter);

        return Mathf.Max(0f,
            spring * seasonal.spring
            + summer * seasonal.summer
            + fall * seasonal.fall
            + winter * seasonal.winter
            + permanentSummer * seasonal.permanentSummer
            + permanentWinter * seasonal.permanentWinter);
    }

    public static float BuildSourceTargetChance(
        float baseChance,
        Pawn source,
        Pawn target,
        ResolvedTransmissionProfile resolvedProfile,
        TransmissionVector vector,
        Map map,
        float vectorContextFactor,
        float settingsMultiplier,
        out HediffDef immunityCause)
    {
        immunityCause = null;
        float infectivity = GetSourceInfectivity(source, resolvedProfile, vector);
        if (infectivity <= 0f)
        {
            return 0f;
        }

        float targetFactor = GetTargetEligibilityFactor(target, resolvedProfile, source, out immunityCause);
        if (targetFactor <= 0f)
        {
            return 0f;
        }

        return Mathf.Max(0f, baseChance)
            * infectivity
            * GetSeasonalMultiplier(map, resolvedProfile.Profile)
            * targetFactor
            * Mathf.Max(0f, vectorContextFactor)
            * Mathf.Max(0f, settingsMultiplier);
    }

    public static float BuildSeederChance(
        float baseChance,
        Pawn target,
        ResolvedTransmissionProfile resolvedProfile,
        Map map,
        float settingsMultiplier,
        out HediffDef immunityCause)
    {
        float targetFactor = GetTargetEligibilityFactor(target, resolvedProfile, null, out immunityCause);
        if (targetFactor <= 0f)
        {
            return 0f;
        }

        return Mathf.Max(0f, baseChance)
            * GetSeasonalMultiplier(map, resolvedProfile.Profile)
            * targetFactor
            * Mathf.Max(0f, settingsMultiplier);
    }

    private const float BaseActiveCaseChance = 0.30f;

    private const float ActiveCaseChancePerPawn = 0.01f;

    private const float MaxActiveCaseChance = 0.50f;

    // True for pawns the spread-suppression mechanic treats as part of "the colony". This is
    // deliberately narrower than Faction.OfPlayer for humanlike pawns so guest groups, visitors,
    // and other transient arrivals do not consume the colony's active-case budget.
    public static bool IsSuppressionTarget(Pawn pawn)
    {
        return IsColonyCasePawn(pawn);
    }

    public static ContagionCaseTrack GetCaseTrack(Pawn pawn)
    {
        if (pawn?.RaceProps?.Animal == true)
        {
            return ContagionCaseTrack.Animal;
        }

        if (pawn?.RaceProps?.Humanlike == true)
        {
            return ContagionCaseTrack.Human;
        }

        return ContagionCaseTrack.None;
    }

    public static float GetSourceSpeciesFactor(ThingDef sourcePawnDef, Pawn target, ResolvedTransmissionProfile resolvedProfile, Map map)
    {
        RaceProperties sourceRace = sourcePawnDef?.race;
        RaceProperties targetRace = target?.RaceProps;
        TransmissionProfile profile = resolvedProfile?.Profile;
        if (sourceRace == null || targetRace == null || profile == null)
        {
            return 1f;
        }

        if ((sourceRace.Humanlike && targetRace.Animal) || (sourceRace.Animal && targetRace.Humanlike))
        {
            return Mathf.Max(0f, profile.crossSpeciesTransmissionFactor);
        }

        return GetAnimalCrossSpeciesFactor(map, sourcePawnDef, target, resolvedProfile);
    }

    private static float GetAnimalCrossSpeciesFactor(
        Map map,
        ThingDef sourcePawnDef,
        Pawn target,
        ResolvedTransmissionProfile resolvedProfile)
    {
        TransmissionProfile profile = resolvedProfile?.Profile;
        if (sourcePawnDef?.race?.Animal != true
            || target?.RaceProps?.Animal != true
            || sourcePawnDef == target.def
            || !IsColonyCasePawn(target)
            || profile?.animalCrossSpeciesFactorCurve == null)
        {
            return 1f;
        }

        HashSet<ThingDef> infectedColonyAnimalSpecies = GetInfectedColonyAnimalSpecies(map, resolvedProfile);
        if (infectedColonyAnimalSpecies.Contains(target.def))
        {
            return 1f;
        }

        return Mathf.Max(0f, profile.animalCrossSpeciesFactorCurve.Evaluate(infectedColonyAnimalSpecies.Count));
    }

    public static bool IsAtActiveCaseCapacity(Map map, ResolvedTransmissionProfile resolvedProfile, Pawn targetPawn)
    {
        ContagionCaseTrack track = GetCaseTrack(targetPawn);
        if (track == ContagionCaseTrack.None)
        {
            return false;
        }

        int capacity = GetActiveCaseCapacity(map, resolvedProfile, track);
        return capacity > 0 && CountActiveCases(map, resolvedProfile, track) >= capacity;
    }

    public static bool IsAnyTrackAtActiveCaseCapacity(Map map, ResolvedTransmissionProfile resolvedProfile)
    {
        if (resolvedProfile?.Profile == null || !resolvedProfile.Profile.useScaledActiveCaseCap)
        {
            return false;
        }

        bool checkedAny = false;
        if (resolvedProfile.Profile.affectsHumans)
        {
            checkedAny = true;
            int capacity = GetActiveCaseCapacity(map, resolvedProfile, ContagionCaseTrack.Human);
            if (capacity > 0 && CountActiveCases(map, resolvedProfile, ContagionCaseTrack.Human) >= capacity)
            {
                return true;
            }
        }

        if (resolvedProfile.Profile.affectsAnimals)
        {
            checkedAny = true;
            int capacity = GetActiveCaseCapacity(map, resolvedProfile, ContagionCaseTrack.Animal);
            if (capacity > 0 && CountActiveCases(map, resolvedProfile, ContagionCaseTrack.Animal) >= capacity)
            {
                return true;
            }
        }

        return checkedAny && GetRemainingActiveCaseCapacity(map, resolvedProfile) <= 0;
    }

    public static int GetRemainingActiveCaseCapacity(Map map, ResolvedTransmissionProfile resolvedProfile)
    {
        if (resolvedProfile?.Profile == null || !resolvedProfile.Profile.useScaledActiveCaseCap)
        {
            return int.MaxValue;
        }

        int remaining = 0;
        bool sawPopulation = false;
        if (resolvedProfile.Profile.affectsHumans)
        {
            int capacity = GetActiveCaseCapacity(map, resolvedProfile, ContagionCaseTrack.Human);
            if (capacity == 0)
            {
                return int.MaxValue;
            }

            if (capacity > 0)
            {
                sawPopulation = true;
                remaining += Mathf.Max(0, capacity - CountActiveCases(map, resolvedProfile, ContagionCaseTrack.Human));
            }
        }

        if (resolvedProfile.Profile.affectsAnimals)
        {
            int capacity = GetActiveCaseCapacity(map, resolvedProfile, ContagionCaseTrack.Animal);
            if (capacity == 0)
            {
                return int.MaxValue;
            }

            if (capacity > 0)
            {
                sawPopulation = true;
                remaining += Mathf.Max(0, capacity - CountActiveCases(map, resolvedProfile, ContagionCaseTrack.Animal));
            }
        }

        return sawPopulation ? remaining : int.MaxValue;
    }

    public static int GetRemainingActiveCaseCapacity(Map map, ResolvedTransmissionProfile resolvedProfile, ContagionCaseTrack track)
    {
        int capacity = GetActiveCaseCapacity(map, resolvedProfile, track);
        if (capacity == int.MaxValue)
        {
            return int.MaxValue;
        }

        return Mathf.Max(0, capacity - CountActiveCases(map, resolvedProfile, track));
    }

    public static int GetActiveCaseCapacity(Map map, ResolvedTransmissionProfile resolvedProfile, ContagionCaseTrack track)
    {
        if (map == null || resolvedProfile?.Profile == null || !resolvedProfile.Profile.useScaledActiveCaseCap)
        {
            return int.MaxValue;
        }

        int population = CountAffectablePlayerPawns(map, resolvedProfile, track);
        if (population <= 0)
        {
            return 0;
        }

        float chance = Mathf.Clamp(
            BaseActiveCaseChance + population * ActiveCaseChancePerPawn + resolvedProfile.Profile.maxActiveCaseChanceOffset,
            0f,
            MaxActiveCaseChance);
        return Mathf.Max(1, Mathf.FloorToInt(population * chance));
    }

    // Spread suppression: as a target track approaches its active-case capacity, contagious
    // transmission rolls TO that track are dampened by a clamped smoothstep. It applies to
    // contagious vectors shed into shared space: airborne, social, proximity, and fomite. It is
    // deliberately NOT applied to foodborne or environmental exposure.
    public static float GetSpreadSuppressionFactor(Map map, ResolvedTransmissionProfile resolvedProfile, Pawn targetPawn)
    {
        if (map == null || resolvedProfile?.Profile == null || !IsSuppressionTarget(targetPawn))
        {
            return 1f;
        }

        if (resolvedProfile.Profile.spreadSuppressionScale <= 0f)
        {
            return 1f;
        }

        ContagionSuppressionMode mode = Contagion_Mod.Settings?.suppressionMode ?? ContagionSuppressionMode.Medium;
        if (mode == ContagionSuppressionMode.LetErRip)
        {
            return 1f;
        }

        ContagionCaseTrack track = GetCaseTrack(targetPawn);
        int capacity = GetActiveCaseCapacity(map, resolvedProfile, track);
        if (capacity <= 0 || capacity == int.MaxValue)
        {
            return 1f;
        }

        int activeCases = CountActiveCases(map, resolvedProfile, track);
        if (activeCases <= 0)
        {
            return 1f;
        }

        float load = activeCases / (float)capacity;
        float factor = mode switch
        {
            ContagionSuppressionMode.Strong => EvaluateSuppressionSmoothstep(load, 0.50f, 1.00f, 0f),
            ContagionSuppressionMode.Weak => EvaluateSuppressionSmoothstep(load, 0.98f, 2.00f, 0.15f),
            _ => EvaluateSuppressionSmoothstep(load, 0.90f, 1.10f, 0.05f)
        };

        return Mathf.Lerp(1f, factor, Mathf.Clamp01(resolvedProfile.Profile.spreadSuppressionScale));
    }

    private static float EvaluateSuppressionSmoothstep(float load, float startLoad, float stopLoad, float floor)
    {
        if (load <= startLoad)
        {
            return 1f;
        }

        if (load >= stopLoad)
        {
            return Mathf.Clamp01(floor);
        }

        float t = Mathf.InverseLerp(startLoad, stopLoad, load);
        float drop = t * t * (3f - 2f * t);
        return Mathf.Lerp(1f, Mathf.Clamp01(floor), drop);
    }

    // ── Per-tick count memoization ───────────────────────────────────────────────────────────
    // CountActiveCases (O(pawns × hediffs)) and CountAffectablePlayerPawns (O(pawns)) are called
    // once per (source × in-range target × vector) in the transmission pass and twice per social
    // interaction, so recomputing them every call dominates CPU during an outbreak. We memoize
    // per (map, disease, track) for the duration of a game tick.
    //
    // Population depends only on faction/species, so per-tick caching is exact. Active-case counts
    // change when a case is added mid-tick — SeedIncubationToPawns seeds a whole storyteller batch
    // in one tick and re-checks capacity per pawn — so any case add invalidates the active-case
    // cache via NotifyCaseAdded to keep capacity gating correct. Case removals (death/cure) only
    // make the cached count too high, which is the safe direction (more suppression, never
    // over-seeding) and self-corrects on the next tick.
    private static int _countCacheTick = -1;

    private static readonly Dictionary<(int map, HediffDef disease, ContagionCaseTrack track), int> _activeCaseCountCache =
        new Dictionary<(int, HediffDef, ContagionCaseTrack), int>();

    private static readonly Dictionary<(int map, HediffDef disease, ContagionCaseTrack track), int> _affectablePopulationCache =
        new Dictionary<(int, HediffDef, ContagionCaseTrack), int>();

    private static readonly Dictionary<(int map, HediffDef disease), HashSet<ThingDef>> _infectedColonyAnimalSpeciesCache =
        new Dictionary<(int, HediffDef), HashSet<ThingDef>>();

    private static void EnsureCountCacheFresh()
    {
        int tick = Find.TickManager.TicksGame;
        if (tick == _countCacheTick)
        {
            return;
        }

        _countCacheTick = tick;
        _activeCaseCountCache.Clear();
        _affectablePopulationCache.Clear();
        _infectedColonyAnimalSpeciesCache.Clear();
    }

    // Invalidates the active-case count cache when a new case (incubation or active disease) is
    // added, so capacity checks later in the same tick observe it. Population is unaffected and is
    // intentionally left cached. Called from the seeding choke points.
    public static void NotifyCaseAdded()
    {
        _activeCaseCountCache.Clear();
        _infectedColonyAnimalSpeciesCache.Clear();
    }

    private static HashSet<ThingDef> GetInfectedColonyAnimalSpecies(Map map, ResolvedTransmissionProfile resolvedProfile)
    {
        if (map == null || resolvedProfile?.DiseaseDef == null)
        {
            return new HashSet<ThingDef>();
        }

        EnsureCountCacheFresh();
        (int, HediffDef) key = (map.uniqueID, resolvedProfile.DiseaseDef);
        if (!_infectedColonyAnimalSpeciesCache.TryGetValue(key, out HashSet<ThingDef> infectedSpecies))
        {
            infectedSpecies = CountInfectedColonyAnimalSpeciesUncached(map, resolvedProfile);
            _infectedColonyAnimalSpeciesCache[key] = infectedSpecies;
        }

        return infectedSpecies;
    }

    private static HashSet<ThingDef> CountInfectedColonyAnimalSpeciesUncached(Map map, ResolvedTransmissionProfile resolvedProfile)
    {
        HashSet<ThingDef> infectedSpecies = new HashSet<ThingDef>();
        IReadOnlyList<Pawn> pawns = map?.mapPawns?.AllPawnsSpawned;
        if (pawns == null || resolvedProfile?.DiseaseDef == null)
        {
            return infectedSpecies;
        }

        for (int i = 0; i < pawns.Count; i++)
        {
            Pawn pawn = pawns[i];
            if (pawn?.health?.hediffSet == null || pawn.def == null || !IsColonyCasePawn(pawn) || pawn.RaceProps?.Animal != true)
            {
                continue;
            }

            HediffDef pawnDef = resolvedProfile.ResolveHediffForPawn(pawn);
            if (pawn.health.hediffSet.HasHediff(pawnDef)
                || ContagionDiseaseUtility.FindIncubation(pawn, pawnDef) != null)
            {
                infectedSpecies.Add(pawn.def);
            }
        }

        return infectedSpecies;
    }

    private static int CountAffectablePlayerPawns(Map map, ResolvedTransmissionProfile resolvedProfile, ContagionCaseTrack track)
    {
        if (map == null || resolvedProfile?.DiseaseDef == null)
        {
            return 0;
        }

        EnsureCountCacheFresh();
        (int, HediffDef, ContagionCaseTrack) key = (map.uniqueID, resolvedProfile.DiseaseDef, track);
        if (_affectablePopulationCache.TryGetValue(key, out int cached))
        {
            return cached;
        }

        int count = CountAffectablePlayerPawnsUncached(map, resolvedProfile, track);
        _affectablePopulationCache[key] = count;
        return count;
    }

    private static int CountAffectablePlayerPawnsUncached(Map map, ResolvedTransmissionProfile resolvedProfile, ContagionCaseTrack track)
    {
        IReadOnlyList<Pawn> pawns = map?.mapPawns?.AllPawnsSpawned;
        if (pawns == null || resolvedProfile?.Profile == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < pawns.Count; i++)
        {
            Pawn pawn = pawns[i];
            if (pawn == null
                || pawn.Dead
                || !IsColonyCasePawn(pawn)
                || GetCaseTrack(pawn) != track
                || !resolvedProfile.Profile.CanAffect(pawn))
            {
                continue;
            }

            count++;
        }

        return count;
    }

    public static int CountActiveCases(Map map, ResolvedTransmissionProfile resolvedProfile)
    {
        return CountActiveCases(map, resolvedProfile, ContagionCaseTrack.Human)
            + CountActiveCases(map, resolvedProfile, ContagionCaseTrack.Animal);
    }

    public static int CountActiveCases(Map map, ResolvedTransmissionProfile resolvedProfile, ContagionCaseTrack track)
    {
        if (map == null || resolvedProfile?.DiseaseDef == null)
        {
            return 0;
        }

        EnsureCountCacheFresh();
        (int, HediffDef, ContagionCaseTrack) key = (map.uniqueID, resolvedProfile.DiseaseDef, track);
        if (_activeCaseCountCache.TryGetValue(key, out int cached))
        {
            return cached;
        }

        int count = CountActiveCasesUncached(map, resolvedProfile, track);
        _activeCaseCountCache[key] = count;
        return count;
    }

    private static int CountActiveCasesUncached(Map map, ResolvedTransmissionProfile resolvedProfile, ContagionCaseTrack track)
    {
        IReadOnlyList<Pawn> pawns = map?.mapPawns?.AllPawnsSpawned;
        if (pawns == null || resolvedProfile?.DiseaseDef == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < pawns.Count; i++)
        {
            Pawn pawn = pawns[i];
            if (pawn?.health?.hediffSet == null || !IsColonyCasePawn(pawn) || GetCaseTrack(pawn) != track)
            {
                continue;
            }

            HediffDef pawnDef = resolvedProfile.ResolveHediffForPawn(pawn);
            if (pawn.health.hediffSet.HasHediff(pawnDef)
                || ContagionDiseaseUtility.FindIncubation(pawn, pawnDef) != null)
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsColonyCasePawn(Pawn pawn)
    {
        if (pawn == null || pawn.Dead)
        {
            return false;
        }

        if (pawn.RaceProps?.Animal == true)
        {
            return pawn.Faction == Faction.OfPlayer;
        }

        if (pawn.RaceProps?.Humanlike == true)
        {
            return pawn.IsColonist || pawn.IsSlaveOfColony || pawn.IsPrisonerOfColony;
        }

        return false;
    }

    private static float GetVanillaContractFactor(Pawn target, ResolvedTransmissionProfile resolvedProfile, out HediffDef immunityCause)
    {
        immunityCause = null;

        // Part targeting (e.g. GutWorms → Stomach) is a human-gameplay detail. Animals vary
        // enormously in body layout and may lack the targeted part entirely, which would block
        // all seeding. Skip part targeting for animal pawns and use the whole-pawn factor instead.
        HediffDef contractDef = resolvedProfile.ResolveHediffForPawn(target);
        if (resolvedProfile.PartsToAffect.NullOrEmpty() || target.RaceProps?.Animal == true)
        {
            return Mathf.Max(0f, target.health.immunity.DiseaseContractChanceFactor(contractDef, out immunityCause));
        }

        float bestFactor = 0f;
        HediffDef bestImmunityCause = null;
        for (int i = 0; i < resolvedProfile.PartsToAffect.Count; i++)
        {
            BodyPartRecord part = target.health.hediffSet.GetBodyPartRecord(resolvedProfile.PartsToAffect[i]);
            if (part == null)
            {
                continue;
            }

            float partFactor = target.health.immunity.DiseaseContractChanceFactor(contractDef, out HediffDef partImmunityCause, part);
            if (partFactor > bestFactor)
            {
                bestFactor = partFactor;
                bestImmunityCause = null;
            }
            else if (partImmunityCause != null && bestImmunityCause == null)
            {
                bestImmunityCause = partImmunityCause;
            }
        }

        immunityCause = bestFactor > 0f ? null : bestImmunityCause;
        return Mathf.Max(0f, bestFactor);
    }

    private static bool IsFullyImmuneActiveDisease(Hediff hediff)
    {
        return hediff is HediffWithComps hediffWithComps
            && hediffWithComps.GetComp<HediffComp_Immunizable>()?.FullyImmune == true;
    }

    private static float GetSusceptibilityFactorProduct(Pawn target, TransmissionProfile profile)
    {
        if (profile?.susceptibilityFactors == null)
        {
            return 1f;
        }

        float product = 1f;
        for (int i = 0; i < profile.susceptibilityFactors.Count; i++)
        {
            product *= Mathf.Max(0f, profile.susceptibilityFactors[i]?.Evaluate(target) ?? 1f);
            if (product <= 0f)
            {
                return 0f;
            }
        }

        return product;
    }

    private static float GetSourceFactorProduct(Pawn source, TransmissionProfile profile)
    {
        if (profile?.sourceInfectivityFactors == null)
        {
            return 1f;
        }

        float product = 1f;
        for (int i = 0; i < profile.sourceInfectivityFactors.Count; i++)
        {
            product *= Mathf.Max(0f, profile.sourceInfectivityFactors[i]?.Evaluate(source) ?? 1f);
            if (product <= 0f)
            {
                return 0f;
            }
        }

        return product;
    }

    public static float GetHorizontalDistance(IntVec3 first, IntVec3 second)
    {
        float deltaX = first.x - second.x;
        float deltaZ = first.z - second.z;
        return Mathf.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
    }

    public static float GetDistanceFactor(float distance, float distanceFalloffRate)
    {
        return Mathf.Exp(-Mathf.Max(0.01f, distanceFalloffRate) * distance);
    }

    public static bool TryGetRoomAirFactor(
        Map map,
        IntVec3 source,
        IntVec3 target,
        Vector_Airborne vector,
        out float effectiveRoomDistance,
        out float roomAirFactor)
    {
        effectiveRoomDistance = 0f;
        roomAirFactor = 0f;
        if (map == null
            || vector == null
            || vector.roomAirBaseChanceFactor <= 0f
            || vector.roomAirMaxRange <= 0
            || vector.roomAirMaxCells <= 0
            || !source.InBounds(map)
            || !target.InBounds(map)
            || !source.InHorDistOf(target, vector.roomAirMaxRange))
        {
            return false;
        }

        Room sourceRoom = source.GetRoom(map);
        Room targetRoom = target.GetRoom(map);
        if (sourceRoom == null
            || sourceRoom != targetRoom
            || IsOutdoors(sourceRoom)
            || sourceRoom.CellCount > vector.roomAirMaxCells)
        {
            return false;
        }

        effectiveRoomDistance = Mathf.Sqrt(Mathf.Max(1f, sourceRoom.CellCount));
        roomAirFactor = GetDistanceFactor(effectiveRoomDistance, vector.distanceFalloffRate);
        return roomAirFactor > 0f;
    }

    public static void CollectReachablePathDistances(
        Map map,
        IntVec3 origin,
        float maxDistance,
        Dictionary<int, float> distanceByCell,
        Queue<IntVec3> openCells)
    {
        distanceByCell?.Clear();
        openCells?.Clear();
        if (map == null || distanceByCell == null || openCells == null || maxDistance < 0f || !origin.InBounds(map))
        {
            return;
        }

        int originIndex = map.cellIndices.CellToIndex(origin);
        distanceByCell[originIndex] = 0f;
        openCells.Enqueue(origin);

        while (openCells.Count > 0)
        {
            IntVec3 current = openCells.Dequeue();
            float currentDistance = distanceByCell[map.cellIndices.CellToIndex(current)];

            for (int i = 0; i < GenAdj.AdjacentCells.Length; i++)
            {
                IntVec3 offset = GenAdj.AdjacentCells[i];
                IntVec3 next = current + offset;
                if (!next.InBounds(map) || !CanPhysicalSpreadPassThrough(map, next))
                {
                    continue;
                }

                bool diagonal = offset.x != 0 && offset.z != 0;
                if (diagonal)
                {
                    IntVec3 sideA = new IntVec3(current.x + offset.x, 0, current.z);
                    IntVec3 sideB = new IntVec3(current.x, 0, current.z + offset.z);
                    if (!CanPhysicalSpreadPassThrough(map, sideA) || !CanPhysicalSpreadPassThrough(map, sideB))
                    {
                        continue;
                    }
                }

                float stepDistance = diagonal ? 1.41421356f : 1f;
                float nextDistance = currentDistance + stepDistance;
                if (nextDistance > maxDistance)
                {
                    continue;
                }

                int nextIndex = map.cellIndices.CellToIndex(next);
                if (!distanceByCell.TryGetValue(nextIndex, out float existingDistance)
                    || nextDistance + 0.0001f < existingDistance)
                {
                    distanceByCell[nextIndex] = nextDistance;
                    openCells.Enqueue(next);
                }
            }
        }
    }

    private static bool CanPhysicalSpreadPassThrough(Map map, IntVec3 cell)
    {
        if (map == null || !cell.InBounds(map))
        {
            return false;
        }

        TerrainDef terrain = map.terrainGrid.TerrainAt(cell);
        if (terrain == null || terrain.passability == Traversability.Impassable)
        {
            return false;
        }

        List<Thing> things = map.thingGrid.ThingsListAtFast(cell);
        for (int i = 0; i < things.Count; i++)
        {
            Thing thing = things[i];
            if (thing?.def?.passability != Traversability.Impassable)
            {
                continue;
            }

            if (thing is Building_Door door && door.Open)
            {
                continue;
            }

            return false;
        }

        return true;
    }

    public static bool IsOutdoors(Room room)
    {
        return room == null || room.PsychologicallyOutdoors;
    }

    public static float GetLocalCleanlinessFactor(IntVec3 position, Room room, Map map, float cleanlinessImpact, int outdoorFilthRadius)
    {
        if (cleanlinessImpact <= 0f)
        {
            return 1f;
        }

        if (room == null || room.PsychologicallyOutdoors)
        {
            return GetOutdoorFilthCleanlinessFactor(position, map, cleanlinessImpact, outdoorFilthRadius);
        }

        float cleanliness = room.GetStat(RoomStatDefOf.Cleanliness);
        return Mathf.Clamp(1f - cleanliness * cleanlinessImpact, MinCleanlinessFactor, MaxCleanlinessFactor);
    }

    private const float MinCleanlinessFactor = 0.1f;

    private const float MaxCleanlinessFactor = 3f;

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
        return Mathf.Clamp(1f + filthDensity * cleanlinessImpact, MinCleanlinessFactor, MaxCleanlinessFactor);
    }

    private static SimpleCurve CreateDefaultActiveInfectivityCurve()
    {
        SimpleCurve curve = new SimpleCurve();
        curve.Add(0f, 0.3f, sort: false);
        curve.Add(0.15f, 0.7f, sort: false);
        curve.Add(0.35f, 1f, sort: false);
        curve.Add(0.65f, 1f, sort: false);
        curve.Add(0.85f, 0.3f, sort: false);
        curve.Add(1f, 0f, sort: false);
        return curve;
    }
}
