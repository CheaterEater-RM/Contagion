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
        immunityCause = null;

        if (!CanContractDiseaseNow(pawn, diseaseDef, partsToAffect, out immunityCause))
        {
            ContagionDiagnostics.Record(ContagionDiagnosticCounter.IncubationBlocked);
            if (immunityCause != null)
            {
                ContagionDiagnostics.Record(ContagionDiagnosticCounter.IncubationBlockedByImmunity);
            }

            return false;
        }

        if (FindIncubation(pawn, diseaseDef) != null)
        {
            ContagionDiagnostics.Record(ContagionDiagnosticCounter.IncubationBlocked);
            return false;
        }

        if (!DiseaseProfileCache.TryGetResolvedProfile(diseaseDef, out ResolvedTransmissionProfile resolvedProfile))
        {
            Log.Warning($"[Contagion] Tried to seed incubation for {diseaseDef?.defName ?? "null"} without a resolved profile.");
            return false;
        }

        Hediff_ContagionIncubation incubation = HediffMaker.MakeHediff(ContagionDefOf.Contagion_Incubation, pawn) as Hediff_ContagionIncubation;
        if (incubation == null)
        {
            Log.Error($"[Contagion] Failed to create incubation hediff for {diseaseDef.defName}.");
            return false;
        }

        int activationTick = Find.TickManager.TicksGame + GetIncubationDurationTicks(resolvedProfile.Profile);
        List<BodyPartDef> resolvedParts = partsToAffect.NullOrEmpty() ? resolvedProfile.PartsToAffect : partsToAffect;
        incubation.Configure(diseaseDef, resolvedParts, activationTick);
        pawn.health.AddHediff(incubation);
        ContagionDiagnostics.Record(ContagionDiagnosticCounter.IncubationSeeded);
        ContagionDiagnostics.Trace($"Incubation seeded: {diseaseDef.defName} on {pawn.LabelShortCap}.");
        return true;
    }

    public static bool TryActivateIncubatedDisease(Hediff_ContagionIncubation incubation)
    {
        if (incubation?.pawn == null || incubation.TargetDiseaseDef == null)
        {
            return false;
        }

        Pawn pawn = incubation.pawn;
        if (!CanContractDiseaseNow(pawn, incubation.TargetDiseaseDef, incubation.PartsToAffect, out var _) && !pawn.health.hediffSet.HasHediff(incubation.TargetDiseaseDef))
        {
            pawn.health.RemoveHediff(incubation);
            return false;
        }

        HediffGiverUtility.TryApply(pawn, incubation.TargetDiseaseDef, incubation.PartsToAffect);
        pawn.health.RemoveHediff(incubation);
        return true;
    }

    public static bool CanContractDiseaseNow(Pawn pawn, HediffDef diseaseDef, List<BodyPartDef> partsToAffect, out HediffDef immunityCause)
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

        if (!resolvedProfile.Profile.CanAffect(pawn))
        {
            return false;
        }

        if (pawn.health.hediffSet.HasHediff(diseaseDef) || FindIncubation(pawn, diseaseDef) != null || HasTemporaryImmunity(pawn, diseaseDef))
        {
            return false;
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

                if (!incubationProfile.Profile.HasVectors || !incubationProfile.Profile.contagiousDuringIncubation || !seenDiseases.Add(incubationProfile.DiseaseDef))
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

            if (hediff.Severity < resolvedProfile.Profile.contagiousMinSeverity || hediff.Severity > resolvedProfile.Profile.contagiousMaxSeverity)
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

    private static int GetIncubationDurationTicks(TransmissionProfile profile)
    {
        float multiplier = Contagion_Mod.Settings?.incubationLengthMultiplier ?? 1f;
        return Mathf.Max(1, Mathf.RoundToInt(profile.incubationDays * multiplier * TicksPerDay));
    }
}