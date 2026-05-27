using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

public static class ContagionTransmissionUtility
{
    private static readonly SimpleCurve DefaultActiveInfectivityCurve = CreateDefaultActiveInfectivityCurve();

    public static float GetSourceInfectivity(Pawn source, ResolvedTransmissionProfile resolvedProfile, TransmissionVector vector = null)
    {
        if (source?.health?.hediffSet == null || resolvedProfile?.Profile == null)
        {
            return 0f;
        }

        Hediff_ContagionIncubation incubation = ContagionDiseaseUtility.FindIncubation(source, resolvedProfile.DiseaseDef);
        if (incubation != null)
        {
            SimpleCurve curve = vector?.incubationInfectivityCurveOverride ?? resolvedProfile.Profile.incubationInfectivityCurve;
            return curve == null ? 0f : Mathf.Max(0f, curve.Evaluate(incubation.Severity)) * GetSourceFactorProduct(source, resolvedProfile.Profile);
        }

        List<Hediff> hediffs = source.health.hediffSet.hediffs;
        for (int i = 0; i < hediffs.Count; i++)
        {
            Hediff hediff = hediffs[i];
            if (hediff?.def != resolvedProfile.DiseaseDef)
            {
                continue;
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

        if (target.health?.hediffSet == null || target.health.immunity == null)
        {
            return 0f;
        }

        if (target.health.hediffSet.HasHediff(resolvedProfile.DiseaseDef)
            || ContagionDiseaseUtility.FindIncubation(target, resolvedProfile.DiseaseDef) != null
            || ContagionDiseaseUtility.HasTemporaryImmunity(target, resolvedProfile.DiseaseDef))
        {
            return 0f;
        }

        float vanillaFactor = GetVanillaContractFactor(target, resolvedProfile, out immunityCause);
        if (vanillaFactor <= 0f)
        {
            return 0f;
        }

        return speciesFactor
            * vanillaFactor
            * GetSusceptibilityFactorProduct(target, resolvedProfile.Profile);
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

    public static bool IsProfileActiveOnMap(Map map, ResolvedTransmissionProfile resolvedProfile, int maxActiveCases)
    {
        return maxActiveCases > 0 && CountActiveCases(map, resolvedProfile) >= maxActiveCases;
    }

    // True for pawns the spread-suppression mechanic treats as part of "the colony". The suppression
    // fraction is measured over player-faction pawns, so it must only be applied when transmitting TO
    // a player-faction pawn — otherwise a fully-infected colony would wrongly throttle spread among
    // unrelated visitors or raiders, whose infection counts never entered the fraction.
    public static bool IsSuppressionTarget(Pawn pawn)
    {
        return pawn != null && pawn.Faction == Faction.OfPlayer;
    }

    // Spread suppression: as a larger share of the colony already carries the disease (active or
    // incubating), each remaining contagious transmission roll TO a colonist is dampened. This keeps
    // an outbreak from reliably hitting 100% of the colony and gives the player a window to react.
    // Factor = (1 - infectedColonyFraction) ^ effectiveStrength, where effectiveStrength comes from
    // the difficulty setting scaled by the disease's spreadSuppressionScale. A strength of 0
    // (Harder difficulty, or scale 0) disables suppression entirely.
    //
    // Applies to contagious vectors shed by infected colonists into shared space: airborne, social,
    // proximity, and fomite. It is deliberately NOT applied to foodborne (a contaminated-food source,
    // not herd transmission) or environmental seeding (sourced by the map, not the colony).
    public static float GetSpreadSuppressionFactor(Map map, ResolvedTransmissionProfile resolvedProfile)
    {
        if (map == null || resolvedProfile?.Profile == null)
        {
            return 1f;
        }

        float strength = (Contagion_Mod.Settings?.SpreadSuppressionStrength ?? 2f) * resolvedProfile.Profile.spreadSuppressionScale;
        if (strength <= 0f)
        {
            return 1f;
        }

        GetColonyInfectionCounts(map, resolvedProfile, out int infected, out int affectable);
        if (affectable <= 0 || infected <= 0)
        {
            return 1f;
        }

        float fraction = Mathf.Clamp01((float)infected / affectable);
        return Mathf.Pow(1f - fraction, strength);
    }

    // Counts player-faction pawns the disease can affect, and how many already carry it. Restricting
    // to the player faction keeps the "colony fraction" meaningful when raiders or visitors are present.
    private static void GetColonyInfectionCounts(Map map, ResolvedTransmissionProfile resolvedProfile, out int infected, out int affectable)
    {
        infected = 0;
        affectable = 0;
        IReadOnlyList<Pawn> pawns = map?.mapPawns?.AllPawnsSpawned;
        if (pawns == null || resolvedProfile?.Profile == null)
        {
            return;
        }

        for (int i = 0; i < pawns.Count; i++)
        {
            Pawn pawn = pawns[i];
            if (pawn == null || pawn.Dead || pawn.Faction != Faction.OfPlayer || !resolvedProfile.Profile.CanAffect(pawn))
            {
                continue;
            }

            affectable++;
            if (pawn.health?.hediffSet == null)
            {
                continue;
            }

            if (pawn.health.hediffSet.HasHediff(resolvedProfile.DiseaseDef)
                || ContagionDiseaseUtility.FindIncubation(pawn, resolvedProfile.DiseaseDef) != null)
            {
                infected++;
            }
        }
    }

    public static int CountActiveCases(Map map, ResolvedTransmissionProfile resolvedProfile)
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
            if (pawn?.health?.hediffSet == null)
            {
                continue;
            }

            if (pawn.health.hediffSet.HasHediff(resolvedProfile.DiseaseDef)
                || ContagionDiseaseUtility.FindIncubation(pawn, resolvedProfile.DiseaseDef) != null)
            {
                count++;
            }
        }

        return count;
    }

    private static float GetVanillaContractFactor(Pawn target, ResolvedTransmissionProfile resolvedProfile, out HediffDef immunityCause)
    {
        immunityCause = null;
        if (resolvedProfile.PartsToAffect.NullOrEmpty())
        {
            return Mathf.Max(0f, target.health.immunity.DiseaseContractChanceFactor(resolvedProfile.DiseaseDef, out immunityCause));
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

            float partFactor = target.health.immunity.DiseaseContractChanceFactor(resolvedProfile.DiseaseDef, out HediffDef partImmunityCause, part);
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
