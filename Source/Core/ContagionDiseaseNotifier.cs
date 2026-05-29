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

        if (resolvedProfile.Profile.outbreakNotification == OutbreakNotificationMode.FirstCase
            && HasOtherVisibleNotifiableCaseOnMap(map, pawn, resolvedProfile))
        {
            return;
        }

        string diseaseLabel = diseaseHediff?.LabelCap ?? diseaseDef.LabelCap;
        Find.LetterStack.ReceiveLetter(
            "Contagion_LetterLabelOutbreak".Translate(diseaseDef.LabelCap),
            "Contagion_LetterOutbreakFirstCase".Translate(pawn.LabelShortCap, diseaseLabel),
            LetterDefOf.NegativeEvent,
            pawn);
    }

    private static bool HasOtherVisibleNotifiableCaseOnMap(Map map, Pawn currentPawn, ResolvedTransmissionProfile profile)
    {
        IReadOnlyList<Pawn> spawnedPawns = map?.mapPawns?.AllPawnsSpawned;
        if (spawnedPawns == null)
        {
            return false;
        }

        for (int i = 0; i < spawnedPawns.Count; i++)
        {
            Pawn otherPawn = spawnedPawns[i];
            if (otherPawn == null || otherPawn == currentPawn || !PawnUtility.ShouldSendNotificationAbout(otherPawn))
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
