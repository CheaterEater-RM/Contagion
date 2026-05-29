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
        if (innerPawn?.health?.hediffSet == null)
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
                return diseaseDef;
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
        if (animal?.health?.hediffSet == null || animal.RaceProps?.Animal != true)
        {
            return null;
        }

        List<Hediff> hediffs = animal.health.hediffSet.hediffs;
        for (int i = 0; i < hediffs.Count; i++)
        {
            HediffDef diseaseDef = GetDiseaseDef(hediffs[i]);
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
