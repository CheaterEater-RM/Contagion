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
        return GetHumanCorpseContagiousDisease(innerPawn, includeHidden: false);
    }

    // includeHidden: transmission paths (eating infected meat) treat the meat as infectious
    // even when the disease was never diagnosed — undiagnosed hidden disease hediffs are
    // included. Display paths pass false so hidden diseases are never leaked without a roll.
    public static HediffDef GetHumanCorpseContagiousDisease(Pawn innerPawn, bool includeHidden)
    {
        if (innerPawn?.health?.hediffSet == null || innerPawn.RaceProps?.Humanlike != true)
        {
            return null;
        }

        List<Hediff> hediffs = innerPawn.health.hediffSet.hediffs;
        for (int i = 0; i < hediffs.Count; i++)
        {
            HediffDef diseaseDef = GetDiseaseDef(hediffs[i], includeHidden);
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
        return GetAnimalCorpseContagiousDisease(innerPawn, includeHidden: false);
    }

    public static HediffDef GetAnimalCorpseContagiousDisease(Pawn innerPawn, bool includeHidden)
    {
        if (innerPawn?.health?.hediffSet == null || innerPawn.RaceProps?.Animal != true)
        {
            return null;
        }

        List<Hediff> hediffs = innerPawn.health.hediffSet.hediffs;
        for (int i = 0; i < hediffs.Count; i++)
        {
            HediffDef diseaseDef = GetDiseaseDef(hediffs[i], includeHidden);
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

    private static HediffDef GetDiseaseDef(Hediff hediff, bool includeHidden)
    {
        // Incubation: disease never manifested — the pawn showed no symptoms and the meat is
        // not yet carrying an active infection. Excluded for both display and transmission.
        if (hediff is Hediff_ContagionIncubation)
        {
            return null;
        }

        // Active but undiagnosed: disease is present and spreading, but invisible to the player.
        // Excluded from display (no visible warning before death means no infected-corpse marker),
        // but included for transmission — infected meat is infectious whether or not it was diagnosed.
        if (!includeHidden && hediff is Hediff_ContagionAnimalHiddenDisease { Diagnosed: false })
        {
            return null;
        }

        return hediff?.def;
    }
}
