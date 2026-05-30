using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Contagion;

internal static class ContagionDiseaseNotifier
{
    public static void NotifyDiseaseActivated(Pawn pawn, Hediff diseaseHediff, HediffDef diseaseDef)
    {
        if (pawn == null || diseaseDef == null || !PawnUtility.ShouldSendNotificationAbout(pawn))
        {
            return;
        }

        string messageKey = $"ContagionDiseaseActivated-{pawn.thingIDNumber}-{diseaseDef.defName}";
        if (!MessagesRepeatAvoider.MessageShowAllowed(messageKey, 0.5f))
        {
            return;
        }

        string diseaseLabel = diseaseHediff?.LabelCap ?? diseaseDef.LabelCap;
        Messages.Message(
            "Contagion_MessageDiseaseActivated".Translate(pawn.LabelShortCap, diseaseLabel),
            pawn,
            MessageTypeDefOf.NegativeHealthEvent,
            historical: false);

        NotifyOutbreakIfFirstVisibleCase(pawn, diseaseHediff, diseaseDef);
    }

    private static void NotifyOutbreakIfFirstVisibleCase(Pawn pawn, Hediff diseaseHediff, HediffDef diseaseDef)
    {
        Map map = pawn.MapHeld;
        if (map == null)
        {
            return;
        }

        if (!DiseaseProfileCache.TryGetResolvedProfile(diseaseDef, out ResolvedTransmissionProfile resolvedProfile))
        {
            return;
        }

        if (resolvedProfile.Profile.outbreakNotification == OutbreakNotificationMode.None)
        {
            return;
        }

        string diseaseLabel = diseaseHediff?.LabelCap ?? diseaseDef.LabelCap;
        bool isAnimal = pawn.RaceProps?.Animal == true;

        if (isAnimal)
        {
            // Only fire an animal outbreak letter when the disease is also a threat to humans.
            // Pure animal diseases (e.g. Animal_Flu) get no outbreak letter — they have their
            // own notification path via the sick signal and animal examination system.
            if (!resolvedProfile.Profile.affectsHumans)
            {
                return;
            }

            if (resolvedProfile.Profile.outbreakNotification == OutbreakNotificationMode.FirstCase
                && HasOtherVisibleCaseOnMap(map, pawn, resolvedProfile, animalsOnly: true))
            {
                return;
            }

            Find.LetterStack.ReceiveLetter(
                "Contagion_LetterLabelAnimalOutbreak".Translate(diseaseDef.LabelCap),
                "Contagion_LetterAnimalOutbreakFirstCase".Translate(pawn.LabelShortCap, diseaseLabel),
                LetterDefOf.NegativeEvent,
                pawn);
        }
        else
        {
            // Human track: check only other humanlike pawns (colonists, slaves, prisoners).
            // Animals with the same disease do not suppress this letter — both notifications
            // can and should fire independently so the player gets the full picture.
            if (resolvedProfile.Profile.outbreakNotification == OutbreakNotificationMode.FirstCase
                && HasOtherVisibleCaseOnMap(map, pawn, resolvedProfile, animalsOnly: false))
            {
                return;
            }

            Find.LetterStack.ReceiveLetter(
                "Contagion_LetterLabelOutbreak".Translate(diseaseDef.LabelCap),
                "Contagion_LetterOutbreakFirstCase".Translate(pawn.LabelShortCap, diseaseLabel),
                LetterDefOf.NegativeEvent,
                pawn);
        }
    }

    // Checks whether any other pawn on the map already has a visible active case of the same
    // disease profile. animalsOnly=true restricts the search to animals; false restricts to
    // humanlike pawns (colonists, slaves, prisoners). The two tracks are intentionally separate
    // so that an animal case never suppresses a human outbreak letter, and vice versa.
    private static bool HasOtherVisibleCaseOnMap(Map map, Pawn currentPawn, ResolvedTransmissionProfile profile, bool animalsOnly)
    {
        IReadOnlyList<Pawn> spawnedPawns = map?.mapPawns?.AllPawnsSpawned;
        if (spawnedPawns == null)
        {
            return false;
        }

        for (int i = 0; i < spawnedPawns.Count; i++)
        {
            Pawn otherPawn = spawnedPawns[i];
            if (otherPawn == null || otherPawn == currentPawn)
            {
                continue;
            }

            bool otherIsAnimal = otherPawn.RaceProps?.Animal == true;
            if (animalsOnly != otherIsAnimal)
            {
                continue;
            }

            if (!PawnUtility.ShouldSendNotificationAbout(otherPawn))
            {
                continue;
            }

            if (HasVisibleProfileDisease(otherPawn, profile))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasVisibleProfileDisease(Pawn pawn, ResolvedTransmissionProfile profile)
    {
        List<Hediff> hediffs = pawn?.health?.hediffSet?.hediffs;
        if (hediffs == null)
        {
            return false;
        }

        for (int i = 0; i < hediffs.Count; i++)
        {
            Hediff hediff = hediffs[i];
            if (hediff == null || !hediff.Visible)
            {
                continue;
            }

            if (DiseaseProfileCache.TryGetResolvedProfile(hediff.def, out ResolvedTransmissionProfile hediffProfile)
                && hediffProfile == profile)
            {
                return true;
            }
        }

        return false;
    }
}
