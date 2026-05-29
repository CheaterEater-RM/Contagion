using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion.Patches;

[HarmonyPatch(typeof(Pawn_InteractionsTracker), nameof(Pawn_InteractionsTracker.TryInteractWith), new[] { typeof(Pawn), typeof(InteractionDef) })]
internal static class Patch_Pawn_InteractionsTracker_TryInteractWith
{
    private const float FalsePositiveChance = 0.03f;

    private static readonly AccessTools.FieldRef<Pawn_InteractionsTracker, Pawn> PawnField = AccessTools.FieldRefAccess<Pawn_InteractionsTracker, Pawn>("pawn");

    public static void Postfix(Pawn_InteractionsTracker __instance, Pawn recipient, InteractionDef interactionDef, bool __result)
    {
        if (!__result || recipient == null)
        {
            return;
        }

        Pawn initiator = PawnField(__instance);
        if (initiator == null || initiator == recipient)
        {
            return;
        }

        // AnimalChat is the handler-to-animal interaction from JobDriver_InteractAnimal. Use it for
        // disease detection (Animals skill roll) rather than social vector transmission.
        if (interactionDef == InteractionDefOf.AnimalChat)
        {
            if (recipient.RaceProps?.Animal == true)
            {
                TryDetectAnimalDisease(initiator, recipient);
            }
            return;
        }

        TryTransmitSocial(initiator, recipient);
        TryTransmitSocial(recipient, initiator);
    }

    private static void TryDetectAnimalDisease(Pawn handler, Pawn animal)
    {
        if (handler == null || animal == null || animal.Dead || !animal.Spawned)
        {
            return;
        }

        if (animal.health.hediffSet.HasHediff(ContagionDefOf.Contagion_AnimalSick))
        {
            return;
        }

        bool isInfected = ContagionAnimalDiseaseUtility.HasSickSignalDisease(animal);
        float detectionChance = isInfected
            ? Mathf.Clamp01(handler.skills?.GetSkill(SkillDefOf.Animals)?.Level / 20f ?? 0f)
            : FalsePositiveChance;

        if (!Rand.Chance(detectionChance))
        {
            return;
        }

        Hediff sickHediff = HediffMaker.MakeHediff(ContagionDefOf.Contagion_AnimalSick, animal);
        animal.health.AddHediff(sickHediff);

        if (PawnUtility.ShouldSendNotificationAbout(animal) || PawnUtility.ShouldSendNotificationAbout(handler))
        {
            Messages.Message(
                "Contagion_AnimalSickDetected".Translate(handler.LabelShortCap, animal.LabelShortCap),
                animal,
                MessageTypeDefOf.CautionInput,
                historical: false);
        }

        ContagionDiagnostics.Trace($"Animal sick signal: {handler.LabelShortCap} noticed {animal.LabelShortCap} seems unwell ({(isInfected ? "true" : "false positive")}).");
    }

    private static void TryTransmitSocial(Pawn sourcePawn, Pawn targetPawn)
    {
        if (sourcePawn == null || targetPawn == null || sourcePawn.Dead || targetPawn.Dead || !sourcePawn.Spawned || !targetPawn.Spawned || sourcePawn.Map == null || sourcePawn.Map != targetPawn.Map)
        {
            return;
        }

        float transmissionMultiplier = Contagion_Mod.Settings?.EffectiveTransmissionMultiplier ?? 1f;
        Map map = sourcePawn.Map;

        foreach (ResolvedTransmissionProfile resolvedProfile in ContagionDiseaseUtility.GetContagiousProfiles(sourcePawn))
        {
            if (!ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Social socialVector))
            {
                continue;
            }

            ContagionDiagnostics.Record(ContagionDiagnosticCounter.SocialAttempted);
            bool sourceRoofed = map.roofGrid.Roofed(sourcePawn.Position);
            bool targetRoofed = map.roofGrid.Roofed(targetPawn.Position);
            bool hasLineOfSight = GenSight.LineOfSight(sourcePawn.Position, targetPawn.Position, map);
            float enclosureFactor = sourceRoofed && targetRoofed ? 1f : socialVector.outdoorFactor;
            float obstructionFactor = hasLineOfSight ? 1f : 0f;
            float maskFactor = ContagionMaskUtility.GetRespiratoryMaskFactor(sourcePawn, targetPawn, socialVector);
            float suppressionFactor = ContagionTransmissionUtility.IsSuppressionTarget(targetPawn)
                ? ContagionTransmissionUtility.GetSpreadSuppressionFactor(map, resolvedProfile)
                : 1f;
            if (!ContagionDeveloperDiagnosticsUtility.TryBuildSocialBreakdown(
                sourcePawn,
                targetPawn,
                resolvedProfile,
                socialVector,
                map,
                transmissionMultiplier,
                enclosureFactor,
                obstructionFactor,
                maskFactor,
                suppressionFactor,
                out ContagionSpreadBreakdown breakdown)
                || !Rand.Chance(Mathf.Clamp01(breakdown.FinalChance)))
            {
                continue;
            }

            if (ContagionDiseaseUtility.TrySeedIncubation(
                targetPawn,
                resolvedProfile.DiseaseDef,
                resolvedProfile.PartsToAffect,
                sourcePawn,
                ContagionDiagnosticOrigin.Spread,
                out HediffDef _))
            {
                ContagionDiagnostics.Record(ContagionDiagnosticCounter.SocialSeeded);
                ContagionDiagnostics.Trace($"Social transmission: {resolvedProfile.DiseaseDef.defName} from {sourcePawn.LabelShortCap} to {targetPawn.LabelShortCap}.");
                map.GetComponent<Contagion_MapTransmissionComponent>()?.DeveloperDiagnostics.RecordTransmissionTrace(
                    sourcePawn,
                    targetPawn,
                    resolvedProfile.DiseaseDef,
                    ContagionDebugVectorKind.Social);
                break;
            }
        }
    }
}
