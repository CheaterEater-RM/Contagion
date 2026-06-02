using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Contagion;

internal static class ContagionDiseaseNotifier
{
    public static void NotifyDiseaseActivated(Pawn pawn, Hediff diseaseHediff, HediffDef diseaseDef,
        ContagionSeedSource seedSource = ContagionSeedSource.Unknown)
    {
        if (pawn == null || diseaseDef == null || !PawnUtility.ShouldSendNotificationAbout(pawn))
        {
            return;
        }

        if (diseaseHediff is Hediff_ContagionAnimalHiddenDisease { Diagnosed: false })
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

        NotifyOutbreakLetter(pawn, diseaseHediff, diseaseDef, seedSource);
    }

    // Sends either a red "first case" letter or a yellow "cluster case" letter depending on
    // whether an active outbreak is already being tracked on this map.
    private static void NotifyOutbreakLetter(Pawn pawn, Hediff diseaseHediff, HediffDef diseaseDef, ContagionSeedSource seedSource)
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

        bool isAnimal = pawn.RaceProps?.Animal == true;

        // Pure animal diseases (e.g. Animal_Flu) that don't affect humans get no outbreak letter —
        // they have their own notification path through the sick signal and animal examination system.
        if (isAnimal && !resolvedProfile.Profile.affectsHumans)
        {
            return;
        }

        Contagion_MapTransmissionComponent mapComp = map.GetComponent<Contagion_MapTransmissionComponent>();
        if (mapComp == null)
        {
            return;
        }

        // Use separate outbreak tracks for animals vs humanlike. An animal case never suppresses
        // the human first-case letter and vice versa.
        bool outbreakActive = isAnimal
            ? mapComp.IsAnimalOutbreakActive(resolvedProfile)
            : mapComp.IsHumanOutbreakActive(resolvedProfile);

        if (!outbreakActive)
        {
            SendFirstCaseLetter(pawn, diseaseDef, diseaseHediff, resolvedProfile, isAnimal, seedSource, mapComp);
        }
        else
        {
            SendClusterCaseLetter(pawn, diseaseDef, diseaseHediff, resolvedProfile, isAnimal, mapComp);
        }
    }

    private static void SendFirstCaseLetter(
        Pawn pawn,
        HediffDef diseaseDef,
        Hediff diseaseHediff,
        ResolvedTransmissionProfile resolvedProfile,
        bool isAnimal,
        ContagionSeedSource seedSource,
        Contagion_MapTransmissionComponent mapComp)
    {
        string diseaseLabel = diseaseHediff?.LabelCap ?? diseaseDef.LabelCap;

        // Attribute to the wave's recorded origin rather than this pawn's per-pawn seed source, so a
        // contact case (or a different species) showing symptoms first still names the true origin.
        // Falls back to the per-pawn source when no origin was recorded (e.g. a dev-forced disease).
        ContagionSeedSource origin = mapComp.GetOutbreakOrigin(resolvedProfile, seedSource);

        string label;
        string body;

        if (isAnimal)
        {
            label = "Contagion_LetterLabelAnimalOutbreak".Translate(diseaseDef.LabelCap);
            body = GetFirstCaseBody(pawn, diseaseLabel, origin, animal: true);
            mapComp.RecordAnimalOutbreakCase(resolvedProfile);
        }
        else
        {
            label = "Contagion_LetterLabelOutbreak".Translate(diseaseDef.LabelCap);
            body = GetFirstCaseBody(pawn, diseaseLabel, origin, animal: false);
            mapComp.RecordHumanOutbreakCase(resolvedProfile);
        }

        Find.LetterStack.ReceiveLetter(label, body, LetterDefOf.NegativeEvent, pawn);
    }

    private static void SendClusterCaseLetter(
        Pawn pawn,
        HediffDef diseaseDef,
        Hediff diseaseHediff,
        ResolvedTransmissionProfile resolvedProfile,
        bool isAnimal,
        Contagion_MapTransmissionComponent mapComp)
    {
        // Respect the suppress-animal-cluster-notifications toggle.
        if (isAnimal && Contagion_Mod.Settings.suppressAnimalClusterNotifications)
        {
            // The floating message already fired; just record the case for outbreak timing.
            mapComp.RecordAnimalOutbreakCase(resolvedProfile);
            return;
        }

        if (isAnimal)
        {
            mapComp.RecordAnimalOutbreakCase(resolvedProfile);
        }
        else
        {
            mapComp.RecordHumanOutbreakCase(resolvedProfile);
        }

        string diseaseLabel = diseaseHediff?.LabelCap ?? diseaseDef.LabelCap;
        int caseCount = CountActiveCasesOnMap(pawn.MapHeld, resolvedProfile, isAnimal);
        string countLabel = isAnimal
            ? "Contagion_ClusterAffectedAnimals".Translate(caseCount)
            : "Contagion_ClusterAffectedColonists".Translate(caseCount);

        string label = "Contagion_LetterLabelClusterCase".Translate(diseaseDef.LabelCap);
        string body = "Contagion_LetterClusterCase".Translate(pawn.LabelShortCap, diseaseLabel, countLabel);

        // Retrieve the existing cluster letter for this track and replace it if still on stack,
        // or create a new one if it has been dismissed.
        Letter existingLetter = isAnimal
            ? mapComp.GetAnimalClusterLetter(resolvedProfile)
            : mapComp.GetHumanClusterLetter(resolvedProfile);

        if (existingLetter != null && Find.LetterStack.LettersListForReading.Contains(existingLetter))
        {
            Find.LetterStack.RemoveLetter(existingLetter);
        }

        Letter newLetter = LetterMaker.MakeLetter(label, body, LetterDefOf.NeutralEvent, pawn);
        Find.LetterStack.ReceiveLetter(newLetter);

        if (isAnimal)
        {
            mapComp.SetAnimalClusterLetter(resolvedProfile, newLetter);
        }
        else
        {
            mapComp.SetHumanClusterLetter(resolvedProfile, newLetter);
        }
    }

    private static string GetFirstCaseBody(Pawn pawn, string diseaseLabel, ContagionSeedSource seedSource, bool animal)
    {
        string bodyKey = seedSource switch
        {
            ContagionSeedSource.Environmental => animal
                ? "Contagion_LetterAnimalOutbreakFirstCase_Environmental"
                : "Contagion_LetterOutbreakFirstCase_Environmental",
            ContagionSeedSource.Arrival => animal
                ? "Contagion_LetterAnimalOutbreakFirstCase_Arrival"
                : "Contagion_LetterOutbreakFirstCase_Arrival",
            ContagionSeedSource.Foodborne => animal
                ? "Contagion_LetterAnimalOutbreakFirstCase_Foodborne"
                : "Contagion_LetterOutbreakFirstCase_Foodborne",
            ContagionSeedSource.Cooking => animal
                ? "Contagion_LetterAnimalOutbreakFirstCase_Cooking"
                : "Contagion_LetterOutbreakFirstCase_Cooking",
            ContagionSeedSource.Corpse => animal
                ? "Contagion_LetterAnimalOutbreakFirstCase_Corpse"
                : "Contagion_LetterOutbreakFirstCase_Corpse",
            ContagionSeedSource.CorpseIngestion => animal
                ? "Contagion_LetterAnimalOutbreakFirstCase_CorpseIngestion"
                : "Contagion_LetterOutbreakFirstCase_CorpseIngestion",
            ContagionSeedSource.Contact or ContagionSeedSource.Storyteller => animal
                ? "Contagion_LetterAnimalOutbreakFirstCase"
                : "Contagion_LetterOutbreakFirstCase",
            _ => animal
                ? "Contagion_LetterAnimalOutbreakFirstCase_Unknown"
                : "Contagion_LetterOutbreakFirstCase_Unknown",
        };

        return bodyKey.Translate(pawn.LabelShortCap, diseaseLabel);
    }

    private static int CountActiveCasesOnMap(Map map, ResolvedTransmissionProfile resolvedProfile, bool animalsOnly)
    {
        IReadOnlyList<Pawn> spawnedPawns = map?.mapPawns?.AllPawnsSpawned;
        if (spawnedPawns == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < spawnedPawns.Count; i++)
        {
            Pawn p = spawnedPawns[i];
            if (p == null)
            {
                continue;
            }

            bool pIsAnimal = p.RaceProps?.Animal == true;
            if (animalsOnly != pIsAnimal)
            {
                continue;
            }

            if (!PawnUtility.ShouldSendNotificationAbout(p))
            {
                continue;
            }

            if (HasVisibleProfileDisease(p, resolvedProfile))
            {
                count++;
            }
        }

        return count;
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
