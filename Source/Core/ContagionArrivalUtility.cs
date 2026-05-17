using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Contagion;

public static class ContagionArrivalUtility
{
    private sealed class ArrivalCandidate
    {
        public ArrivalCandidate(ResolvedTransmissionProfile resolvedProfile, Seeder_Arrival seeder)
        {
            ResolvedProfile = resolvedProfile;
            Seeder = seeder;
        }

        public ResolvedTransmissionProfile ResolvedProfile { get; }

        public Seeder_Arrival Seeder { get; }
    }

    public static int SeedArrivals(IEnumerable<Pawn> pawns)
    {
        if (pawns == null)
        {
            return 0;
        }

        List<ArrivalCandidate> arrivalCandidates = BuildArrivalCandidates();
        if (arrivalCandidates.Count == 0)
        {
            return 0;
        }

        float outbreakMultiplier = Contagion_Mod.Settings?.outbreakFrequencyMultiplier ?? 1f;
        int seededCount = 0;

        foreach (Pawn pawn in pawns)
        {
            if (TrySeedArrivalDisease(pawn, arrivalCandidates, outbreakMultiplier))
            {
                seededCount++;
            }
        }

        return seededCount;
    }

    private static bool TrySeedArrivalDisease(Pawn pawn, List<ArrivalCandidate> arrivalCandidates, float outbreakMultiplier)
    {
        if (pawn == null || pawn.Dead || !pawn.Spawned)
        {
            return false;
        }

        ContagionDiagnostics.Record(ContagionDiagnosticCounter.ArrivalAttempted);

        List<ArrivalCandidate> applicableCandidates = new List<ArrivalCandidate>();
        for (int i = 0; i < arrivalCandidates.Count; i++)
        {
            ArrivalCandidate candidate = arrivalCandidates[i];
            if (candidate.ResolvedProfile.Profile.CanAffect(pawn))
            {
                applicableCandidates.Add(candidate);
            }
        }

        if (applicableCandidates.Count == 0)
        {
            return false;
        }

        applicableCandidates.Shuffle();
        for (int i = 0; i < applicableCandidates.Count; i++)
        {
            ArrivalCandidate candidate = applicableCandidates[i];
            float arrivalChance = Mathf.Clamp01(candidate.Seeder.arrivalChance * outbreakMultiplier);
            if (arrivalChance <= 0f || !Rand.Chance(arrivalChance))
            {
                continue;
            }

            if (ContagionDiseaseUtility.TrySeedIncubation(
                pawn,
                candidate.ResolvedProfile.DiseaseDef,
                candidate.ResolvedProfile.PartsToAffect,
                out HediffDef _))
            {
                ContagionDiagnostics.Record(ContagionDiagnosticCounter.ArrivalSeeded);
                ContagionDiagnostics.Trace($"Arrival seeded: {candidate.ResolvedProfile.DiseaseDef.defName} on {pawn.LabelShortCap}.");
                return true;
            }
        }

        return false;
    }

    private static List<ArrivalCandidate> BuildArrivalCandidates()
    {
        List<ArrivalCandidate> arrivalCandidates = new List<ArrivalCandidate>();

        foreach (ResolvedTransmissionProfile resolvedProfile in DiseaseProfileCache.AllProfiles)
        {
            Seeder_Arrival seeder = GetArrivalSeeder(resolvedProfile.Profile);
            if (seeder != null)
            {
                arrivalCandidates.Add(new ArrivalCandidate(resolvedProfile, seeder));
            }
        }

        return arrivalCandidates;
    }

    private static Seeder_Arrival GetArrivalSeeder(TransmissionProfile profile)
    {
        if (profile?.seeders == null)
        {
            return null;
        }

        for (int i = 0; i < profile.seeders.Count; i++)
        {
            if (profile.seeders[i] is Seeder_Arrival arrivalSeeder)
            {
                return arrivalSeeder;
            }
        }

        return null;
    }
}