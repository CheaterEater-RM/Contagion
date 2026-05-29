using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Contagion;

public static class ContagionAnimalDiseaseUtility
{
    public static bool IsAnimalCorpseContagious(Pawn innerPawn)
    {
        return GetCorpseContagiousDisease(innerPawn) != null;
    }

    public static HediffDef GetCorpseContagiousDisease(Pawn innerPawn)
    {
        // Design intent: human corpses are unaffected. corpseContagious only applies to
        // animal carcasses entering the butchery chain.
        if (innerPawn?.health?.hediffSet == null || innerPawn.RaceProps?.Animal != true)
        {
            return null;
        }

        List<Hediff> hediffs = innerPawn.health.hediffSet.hediffs;
        for (int i = 0; i < hediffs.Count; i++)
        {
            HediffDef diseaseDef = GetDiseaseDef(hediffs[i]);
            if (diseaseDef == null)
            {
                continue;
            }

            if (DiseaseProfileCache.TryGetResolvedProfile(diseaseDef, out ResolvedTransmissionProfile resolvedProfile)
                && resolvedProfile.Profile.corpseContagious)
            {
                // Return the primary def so downstream callers (meat contamination, food
                // exposure) always work with the human-variant hediff, not the animal variant.
                return resolvedProfile.DiseaseDef;
            }
        }

        return null;
    }

    public static bool HasSickSignalDisease(Pawn animal)
    {
        return GetSickSignalProfile(animal) != null;
    }

    public static ResolvedTransmissionProfile GetSickSignalProfile(Pawn animal)
    {
        // Only match hidden incubation, not already-visible active disease. If the disease has
        // been diagnosed and made visible, the health tab already shows it — re-triggering the
        // sick signal and diagnosis loop would create feedback noise.
        if (animal?.health?.hediffSet == null || animal.RaceProps?.Animal != true)
        {
            return null;
        }

        List<Hediff> hediffs = animal.health.hediffSet.hediffs;
        for (int i = 0; i < hediffs.Count; i++)
        {
            if (hediffs[i] is not Hediff_ContagionIncubation incubation)
            {
                continue;
            }

            HediffDef diseaseDef = incubation.TargetDiseaseDef;
            if (diseaseDef == null)
            {
                continue;
            }

            if (DiseaseProfileCache.TryGetResolvedProfile(diseaseDef, out ResolvedTransmissionProfile resolvedProfile)
                && resolvedProfile.Profile.showsSickSignal)
            {
                return resolvedProfile;
            }
        }

        return null;
    }

    private static HediffDef GetDiseaseDef(Hediff hediff)
    {
        if (hediff is Hediff_ContagionIncubation incubation)
        {
            return incubation.TargetDiseaseDef;
        }

        return hediff?.def;
    }
}
