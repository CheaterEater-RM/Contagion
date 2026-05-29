using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

public static class ContagionDiseaseUtility
{
    private const int TicksPerDay = 60000;

    public static bool TrySeedIncubation(Pawn pawn, HediffDef diseaseDef, List<BodyPartDef> partsToAffect, out HediffDef immunityCause)
    {
        return TrySeedIncubation(pawn, diseaseDef, partsToAffect, ContagionDiagnosticOrigin.Unknown, out immunityCause);
    }

    public static bool TrySeedIncubation(
        Pawn pawn,
        HediffDef diseaseDef,
        List<BodyPartDef> partsToAffect,
        ContagionDiagnosticOrigin origin,
        out HediffDef immunityCause)
    {
        return TrySeedIncubation(pawn, diseaseDef, partsToAffect, null, origin, out immunityCause);
    }

    public static bool TrySeedIncubation(Pawn pawn, HediffDef diseaseDef, List<BodyPartDef> partsToAffect, Pawn sourcePawn, out HediffDef immunityCause)
    {
        return TrySeedIncubation(pawn, diseaseDef, partsToAffect, sourcePawn, ContagionDiagnosticOrigin.Unknown, out immunityCause);
    }

    public static bool TrySeedIncubation(
        Pawn pawn,
        HediffDef diseaseDef,
        List<BodyPartDef> partsToAffect,
        Pawn sourcePawn,
        ContagionDiagnosticOrigin origin,
        out HediffDef immunityCause)
    {
        immunityCause = null;

        if (!DiseaseProfileCache.TryGetResolvedProfile(diseaseDef, out ResolvedTransmissionProfile resolvedProfile))
        {
            Log.Warning($"[Contagion] Tried to seed incubation for {diseaseDef?.defName ?? "null"} without a resolved profile.");
            return false;
        }

        HediffDef targetDiseaseDef = resolvedProfile.ResolveHediffForPawn(pawn);
        if (!CanContractDiseaseNow(pawn, targetDiseaseDef, partsToAffect, sourcePawn, out immunityCause, null))
        {
            ContagionDiagnostics.RecordApplicationResult(origin, seeded: false, immunityCause);
            return false;
        }

        if (FindIncubation(pawn, targetDiseaseDef) != null)
        {
            ContagionDiagnostics.RecordApplicationResult(origin, seeded: false, immunityCause: null);
            return false;
        }

        Hediff_ContagionIncubation incubation = HediffMaker.MakeHediff(ContagionDefOf.Contagion_Incubation, pawn) as Hediff_ContagionIncubation;
        if (incubation == null)
        {
            Log.Error($"[Contagion] Failed to create incubation hediff for {targetDiseaseDef.defName}.");
            return false;
        }

        int activationTick = Find.TickManager.TicksGame + GetIncubationDurationTicks(resolvedProfile.Profile);
        List<BodyPartDef> resolvedParts = ResolvePartsForPawn(pawn, resolvedProfile, partsToAffect);
        incubation.Configure(targetDiseaseDef, resolvedParts, activationTick);
        pawn.health.AddHediff(incubation);
        ContagionDiagnostics.RecordApplicationResult(origin, seeded: true, immunityCause: null);
        ContagionDiagnostics.Trace($"Incubation seeded ({origin}): {targetDiseaseDef.defName} on {pawn.LabelShortCap}.");
        return true;
    }

    public static bool TryActivateIncubatedDisease(Hediff_ContagionIncubation incubation)
    {
        if (incubation?.pawn == null || incubation.TargetDiseaseDef == null)
        {
            return false;
        }

        Pawn pawn = incubation.pawn;
        if (!DiseaseProfileCache.TryGetResolvedProfile(incubation.TargetDiseaseDef, out ResolvedTransmissionProfile resolvedProfile))
        {
            return false;
        }

        HediffDef targetDiseaseDef = resolvedProfile.ResolveHediffForPawn(pawn);
        if (pawn.health.hediffSet.HasHediff(targetDiseaseDef))
        {
            pawn.health.RemoveHediff(incubation);
            return true;
        }

        List<BodyPartDef> partsToAffect = ResolvePartsForPawn(pawn, resolvedProfile, incubation.PartsToAffect);
        if (!CanContractDiseaseNow(pawn, targetDiseaseDef, partsToAffect, null, out var _, incubation))
        {
            pawn.health.RemoveHediff(incubation);
            return false;
        }

        List<Hediff> addedHediffs = new List<Hediff>();
        bool activated = HediffGiverUtility.TryApply(
            pawn,
            targetDiseaseDef,
            partsToAffect,
            outAddedHediffs: addedHediffs);
        if (!activated)
        {
            Log.Warning($"[Contagion] Incubation for {targetDiseaseDef.defName} on {pawn.LabelShortCap} resolved without creating the disease hediff.");
            pawn.health.RemoveHediff(incubation);
            return false;
        }

        TaleRecorder.RecordTale(TaleDefOf.IllnessRevealed, pawn, targetDiseaseDef);
        ContagionDiseaseNotifier.NotifyDiseaseActivated(pawn, addedHediffs.Count > 0 ? addedHediffs[0] : null, targetDiseaseDef);
        pawn.health.RemoveHediff(incubation);
        return true;
    }

    public static bool CanContractDiseaseNow(Pawn pawn, HediffDef diseaseDef, List<BodyPartDef> partsToAffect, out HediffDef immunityCause)
    {
        return CanContractDiseaseNow(pawn, diseaseDef, partsToAffect, null, out immunityCause, null);
    }

    private static bool CanContractDiseaseNow(
        Pawn pawn,
        HediffDef diseaseDef,
        List<BodyPartDef> partsToAffect,
        Pawn sourcePawn,
        out HediffDef immunityCause,
        Hediff_ContagionIncubation ignoredIncubation)
    {
        immunityCause = null;

        if (pawn == null || diseaseDef == null)
        {
            return false;
        }

        if (!DiseaseProfileCache.TryGetResolvedProfile(diseaseDef, out ResolvedTransmissionProfile resolvedProfile))
        {
            return false;
        }

        diseaseDef = resolvedProfile.ResolveHediffForPawn(pawn);
        if (!resolvedProfile.Profile.CanTransmitBetween(sourcePawn, pawn, out float _))
        {
            return false;
        }

        Hediff_ContagionIncubation existingIncubation = FindIncubation(pawn, diseaseDef);
        if (pawn.health.hediffSet.HasHediff(diseaseDef)
            || (existingIncubation != null && existingIncubation != ignoredIncubation)
            || HasTemporaryImmunity(pawn, diseaseDef))
        {
            return false;
        }

        if (ignoredIncubation == null)
        {
            return ContagionTransmissionUtility.GetTargetEligibilityFactor(pawn, resolvedProfile, sourcePawn, out immunityCause) > 0f;
        }

        if (partsToAffect.NullOrEmpty())
        {
            return pawn.health.immunity.DiseaseContractChanceFactor(diseaseDef, out immunityCause) > 0f;
        }

        HediffDef lastImmunityCause = null;
        for (int i = 0; i < partsToAffect.Count; i++)
        {
            BodyPartRecord part = pawn.health.hediffSet.GetBodyPartRecord(partsToAffect[i]);
            if (part == null)
            {
                continue;
            }

            float chanceFactor = pawn.health.immunity.DiseaseContractChanceFactor(diseaseDef, out HediffDef partImmunityCause, part);
            if (chanceFactor > 0f)
            {
                immunityCause = null;
                return true;
            }

            if (partImmunityCause != null)
            {
                lastImmunityCause = partImmunityCause;
            }
        }

        immunityCause = lastImmunityCause;
        return false;
    }

    public static bool HasTemporaryImmunity(Pawn pawn, HediffDef diseaseDef)
    {
        return FindTemporaryImmunity(pawn, diseaseDef) != null;
    }

    public static void GiveTemporaryImmunity(Pawn pawn, HediffDef diseaseDef, int durationTicks)
    {
        if (pawn == null || diseaseDef == null || durationTicks <= 0)
        {
            return;
        }

        Hediff_ContagionTemporaryImmunity immunity = FindTemporaryImmunity(pawn, diseaseDef);
        if (immunity == null)
        {
            immunity = HediffMaker.MakeHediff(ContagionDefOf.Contagion_TemporaryImmunity, pawn) as Hediff_ContagionTemporaryImmunity;
            if (immunity == null)
            {
                Log.Error($"[Contagion] Failed to create temporary immunity hediff for {diseaseDef.defName}.");
                return;
            }

            pawn.health.AddHediff(immunity);
        }

        immunity.Configure(diseaseDef, Find.TickManager.TicksGame + durationTicks);
    }

    public static void GiveCustomImmunity(Pawn pawn, HediffDef immunityHediffDef)
    {
        if (pawn == null || immunityHediffDef == null)
        {
            return;
        }

        Hediff immunity = HediffMaker.MakeHediff(immunityHediffDef, pawn);
        if (immunity == null)
        {
            Log.Error($"[Contagion] Failed to create custom immunity hediff {immunityHediffDef.defName}.");
            return;
        }

        pawn.health.AddHediff(immunity);
    }

    public static Hediff_ContagionIncubation FindIncubation(Pawn pawn, HediffDef diseaseDef)
    {
        if (pawn?.health?.hediffSet == null || diseaseDef == null)
        {
            return null;
        }

        List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
        for (int i = 0; i < hediffs.Count; i++)
        {
            if (hediffs[i] is Hediff_ContagionIncubation incubation && incubation.TargetDiseaseDef == diseaseDef)
            {
                return incubation;
            }
        }

        return null;
    }

    public static bool HasDiseaseOrIncubation(Pawn pawn, HediffDef diseaseDef)
    {
        if (pawn?.health?.hediffSet == null || diseaseDef == null)
        {
            return false;
        }

        return pawn.health.hediffSet.HasHediff(diseaseDef)
            || FindIncubation(pawn, diseaseDef) != null;
    }

    public static bool HasTraitSeedCooldown(Pawn pawn)
    {
        return FindTraitSeedCooldown(pawn) != null;
    }

    public static void GiveTraitSeedCooldown(Pawn pawn, int durationTicks)
    {
        if (pawn?.health == null || durationTicks <= 0)
        {
            return;
        }

        Hediff_ContagionTraitSeedCooldown cooldown = FindTraitSeedCooldown(pawn);
        if (cooldown == null)
        {
            cooldown = HediffMaker.MakeHediff(ContagionDefOf.Contagion_TraitSeedCooldown, pawn) as Hediff_ContagionTraitSeedCooldown;
            if (cooldown == null)
            {
                Log.Error("[Contagion] Failed to create trait-seed cooldown hediff.");
                return;
            }

            pawn.health.AddHediff(cooldown);
        }

        cooldown.Configure(Find.TickManager.TicksGame + durationTicks);
    }

    public static Hediff_ContagionTraitSeedCooldown FindTraitSeedCooldown(Pawn pawn)
    {
        if (pawn?.health?.hediffSet == null)
        {
            return null;
        }

        List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
        for (int i = 0; i < hediffs.Count; i++)
        {
            if (hediffs[i] is Hediff_ContagionTraitSeedCooldown cooldown)
            {
                return cooldown;
            }
        }

        return null;
    }

    public static Hediff_ContagionTemporaryImmunity FindTemporaryImmunity(Pawn pawn, HediffDef diseaseDef)
    {
        if (pawn?.health?.hediffSet == null || diseaseDef == null)
        {
            return null;
        }

        List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
        for (int i = 0; i < hediffs.Count; i++)
        {
            if (hediffs[i] is Hediff_ContagionTemporaryImmunity immunity && immunity.ProtectedDiseaseDef == diseaseDef)
            {
                return immunity;
            }
        }

        return null;
    }

    public static IEnumerable<ResolvedTransmissionProfile> GetContagiousProfiles(Pawn pawn)
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
            if (hediff is Hediff_ContagionIncubation incubation)
            {
                if (incubation.TargetDiseaseDef == null || !DiseaseProfileCache.TryGetResolvedProfile(incubation.TargetDiseaseDef, out ResolvedTransmissionProfile incubationProfile))
                {
                    continue;
                }

                if (!incubationProfile.Profile.HasVectors
                    || ContagionTransmissionUtility.GetSourceInfectivity(pawn, incubationProfile) <= 0f
                    || !seenDiseases.Add(incubationProfile.DiseaseDef))
                {
                    continue;
                }

                yield return incubationProfile;
                continue;
            }

            if (hediff?.def == null || !DiseaseProfileCache.TryGetResolvedProfile(hediff.def, out ResolvedTransmissionProfile resolvedProfile))
            {
                continue;
            }

            if (!resolvedProfile.Profile.HasVectors)
            {
                continue;
            }

            if (ContagionTransmissionUtility.GetSourceInfectivity(pawn, resolvedProfile) <= 0f)
            {
                continue;
            }

            if (!seenDiseases.Add(resolvedProfile.DiseaseDef))
            {
                continue;
            }

            yield return resolvedProfile;
        }
    }

    public static bool TryGetVector<TVector>(TransmissionProfile profile, out TVector vector)
        where TVector : TransmissionVector
    {
        vector = null;

        if (profile?.vectors == null)
        {
            return false;
        }

        for (int i = 0; i < profile.vectors.Count; i++)
        {
            if (profile.vectors[i] is TVector typedVector)
            {
                vector = typedVector;
                return true;
            }
        }

        return false;
    }

    public static bool TryGetSeeder<TSeeder>(TransmissionProfile profile, out TSeeder seeder)
        where TSeeder : TransmissionSeeder
    {
        seeder = null;

        if (profile?.seeders == null)
        {
            return false;
        }

        for (int i = 0; i < profile.seeders.Count; i++)
        {
            if (profile.seeders[i] is TSeeder typedSeeder)
            {
                seeder = typedSeeder;
                return true;
            }
        }

        return false;
    }

    private static int GetIncubationDurationTicks(TransmissionProfile profile)
    {
        float multiplier = Contagion_Mod.Settings?.incubationLengthMultiplier ?? 1f;
        return Mathf.Max(1, Mathf.RoundToInt(profile.incubationDays * multiplier * TicksPerDay));
    }

    public static List<BodyPartDef> ResolvePartsForPawn(
        Pawn pawn,
        ResolvedTransmissionProfile resolvedProfile,
        List<BodyPartDef> requestedParts)
    {
        if (pawn?.RaceProps?.Animal == true)
        {
            return null;
        }

        return requestedParts.NullOrEmpty() ? resolvedProfile?.PartsToAffect : requestedParts;
    }

}
