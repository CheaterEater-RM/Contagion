using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Contagion;

public static class ContagionAnimalDiseaseUtility
{
    public static bool IsAnimalCorpseContagious(Pawn innerPawn)
    {
        return GetAnimalCorpseContagiousDisease(innerPawn) != null;
    }

    public static bool IsHumanCorpseContagious(Pawn innerPawn)
    {
        return GetHumanCorpseContagiousDisease(innerPawn) != null;
    }

    public static HediffDef GetHumanCorpseContagiousDisease(Pawn innerPawn)
    {
        if (innerPawn?.health?.hediffSet == null || innerPawn.RaceProps?.Humanlike != true)
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
                && resolvedProfile.Profile.corpseContagious
                && resolvedProfile.Profile.affectsHumans)
            {
                return resolvedProfile.DiseaseDef;
            }
        }

        return null;
    }

    public static HediffDef GetAnimalCorpseContagiousDisease(Pawn innerPawn)
    {
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
        if (animal?.health?.hediffSet == null || animal.RaceProps?.Animal != true)
        {
            return null;
        }

        List<Hediff> hediffs = animal.health.hediffSet.hediffs;
        for (int i = 0; i < hediffs.Count; i++)
        {
            Hediff hediff = hediffs[i];
            if (hediff is Hediff_ContagionIncubation incubation)
            {
                HediffDef diseaseDef = incubation.TargetDiseaseDef;
                if (diseaseDef != null
                    && DiseaseProfileCache.TryGetResolvedProfile(diseaseDef, out ResolvedTransmissionProfile incubationProfile)
                    && incubationProfile.Profile.showsSickSignal)
                {
                    return incubationProfile;
                }

                continue;
            }

            if (hediff is Hediff_ContagionAnimalHiddenDisease { Diagnosed: false }
                && DiseaseProfileCache.TryGetResolvedProfile(hediff.def, out ResolvedTransmissionProfile activeProfile)
                && activeProfile.Profile.showsSickSignal)
            {
                return activeProfile;
            }
        }

        return null;
    }

    private static HediffDef GetDiseaseDef(Hediff hediff)
    {
        // Incubation: disease never manifested — the pawn showed no symptoms and the player had
        // no prior indication. The corpse should not appear visibly infected.
        if (hediff is Hediff_ContagionIncubation)
        {
            return null;
        }

        // Active but undiagnosed: disease is present and spreading, but still invisible to the player.
        // Same principle: no visible warning before death means no infected-corpse marker.
        if (hediff is Hediff_ContagionAnimalHiddenDisease { Diagnosed: false })
        {
            return null;
        }

        return hediff?.def;
    }
}
