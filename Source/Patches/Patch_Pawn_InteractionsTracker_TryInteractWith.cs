using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion.Patches;

[HarmonyPatch(typeof(Pawn_InteractionsTracker), nameof(Pawn_InteractionsTracker.TryInteractWith), new[] { typeof(Pawn), typeof(InteractionDef) })]
internal static class Patch_Pawn_InteractionsTracker_TryInteractWith
{
    private static readonly AccessTools.FieldRef<Pawn_InteractionsTracker, Pawn> PawnField = AccessTools.FieldRefAccess<Pawn_InteractionsTracker, Pawn>("pawn");

    public static void Postfix(Pawn_InteractionsTracker __instance, Pawn recipient, bool __result)
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

        TryTransmitSocial(initiator, recipient);
        TryTransmitSocial(recipient, initiator);
    }

    private static void TryTransmitSocial(Pawn sourcePawn, Pawn targetPawn)
    {
        if (sourcePawn == null || targetPawn == null || sourcePawn.Dead || targetPawn.Dead || !sourcePawn.Spawned || !targetPawn.Spawned || sourcePawn.Map == null || sourcePawn.Map != targetPawn.Map)
        {
            return;
        }

        Room sourceRoom = sourcePawn.Position.GetRoom(sourcePawn.Map);
        Room targetRoom = targetPawn.Position.GetRoom(sourcePawn.Map);
        float transmissionMultiplier = Contagion_Mod.Settings?.transmissionRateMultiplier ?? 1f;

        foreach (ResolvedTransmissionProfile resolvedProfile in ContagionDiseaseUtility.GetContagiousProfiles(sourcePawn))
        {
            if (!ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Social socialVector))
            {
                continue;
            }

            ContagionDiagnostics.Record(ContagionDiagnosticCounter.SocialAttempted);
            float roomFactor = sourceRoom != null && sourceRoom == targetRoom && !sourceRoom.PsychologicallyOutdoors
                ? 1f
                : socialVector.outdoorFactor;
            float chance = socialVector.baseChancePerInteraction * roomFactor * transmissionMultiplier;
            if (!Rand.Chance(Mathf.Clamp01(chance)))
            {
                continue;
            }

            if (ContagionDiseaseUtility.TrySeedIncubation(targetPawn, resolvedProfile.DiseaseDef, resolvedProfile.PartsToAffect, out HediffDef _))
            {
                ContagionDiagnostics.Record(ContagionDiagnosticCounter.SocialSeeded);
                ContagionDiagnostics.Trace($"Social transmission: {resolvedProfile.DiseaseDef.defName} from {sourcePawn.LabelShortCap} to {targetPawn.LabelShortCap}.");
                break;
            }
        }
    }
}