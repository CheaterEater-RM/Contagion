using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion.Patches;

internal static class Patch_IncidentWorker_Disease_Helper
{
    public static bool TryGetProfile(IncidentWorker_Disease worker, out ResolvedTransmissionProfile resolvedProfile)
    {
        resolvedProfile = null;

        HediffDef diseaseDef = worker?.def?.diseaseIncident;
        return diseaseDef != null && DiseaseProfileCache.TryGetResolvedProfile(diseaseDef, out resolvedProfile);
    }

    public static bool TryGetStorytellerSeeder(TransmissionProfile profile, out Seeder_Storyteller seeder)
        => ContagionDiseaseUtility.TryGetSeeder(profile, out seeder);

    public static bool UsesEnvironmentalSeedingOnly(TransmissionProfile profile)
        => ContagionSeedingCoordinator.UsesEnvironmentalSeedingOnly(profile);
}

[HarmonyPatch(typeof(IncidentWorker_Disease), nameof(IncidentWorker_Disease.ApplyToPawns))]
internal static class Patch_IncidentWorker_Disease_ApplyToPawns
{
    // Because Patch_IncidentWorker_Disease_TryExecuteWorker cancels the storyteller's outer call,
    // any call that reaches this prefix is by elimination the trait-driven path (vanilla
    // Pawn_HealthTracker.HealthTickInterval invokes ApplyToPawns directly when a trait's
    // randomDiseaseMtbDays MTB fires — e.g. Sickly). Treat it accordingly: per-pawn cooldown
    // before seeding, vanilla-style trait letter after seeding so the player has a quarantine
    // window even though the storyteller letter is suppressed.
    private const int TraitSeedCooldownDays = 10;

    private const int TicksPerDay = 60000;

    public static bool Prefix(IncidentWorker_Disease __instance, IEnumerable<Pawn> pawns, ref string blockedInfo, ref List<Pawn> __result)
    {
        if (!Patch_IncidentWorker_Disease_Helper.TryGetProfile(__instance, out ResolvedTransmissionProfile resolvedProfile))
        {
            return true;
        }

        if (Patch_IncidentWorker_Disease_Helper.UsesEnvironmentalSeedingOnly(resolvedProfile.Profile))
        {
            blockedInfo = string.Empty;
            __result = new List<Pawn>();
            return false;
        }

        if (!Patch_IncidentWorker_Disease_Helper.TryGetStorytellerSeeder(resolvedProfile.Profile, out Seeder_Storyteller storytellerSeeder))
        {
            return true;
        }

        List<Pawn> pawnList = pawns?.ToList();
        if (pawnList == null || pawnList.Count == 0 || pawnList.Any(pawn => pawn == null || !pawn.Spawned))
        {
            return true;
        }

        // Trait-driven path: skip pawns who are still on cooldown from a previous trait seed.
        pawnList.RemoveAll(pawn => ContagionDiseaseUtility.HasTraitSeedCooldown(pawn));
        if (pawnList.Count == 0)
        {
            blockedInfo = string.Empty;
            __result = new List<Pawn>();
            return false;
        }
        Map map = pawnList[0].Map;
        if (map?.GetComponent<Contagion_MapTransmissionComponent>()?.CanRunSeeder(resolvedProfile, storytellerSeeder) == false)
        {
            blockedInfo = string.Empty;
            __result = new List<Pawn>();
            return false;
        }

        ContagionDiagnostics.Record(ContagionDiagnosticCounter.StorytellerAttempted, pawnList.Count);
        __result = ContagionSeedingExecutionUtility.SeedIncubationToPawns(resolvedProfile, pawnList, storytellerSeeder, map, out blockedInfo);
        if (__result.Count > 0)
        {
            ContagionDiagnostics.Record(ContagionDiagnosticCounter.StorytellerSeeded, __result.Count);
            ContagionDiagnostics.Trace($"Trait-driven seed of {resolvedProfile.DiseaseDef.defName} on {__result.Count} pawn(s).");

            int cooldownTicks = TraitSeedCooldownDays * TicksPerDay;
            for (int i = 0; i < __result.Count; i++)
            {
                Pawn seededPawn = __result[i];
                ContagionDiseaseUtility.GiveTraitSeedCooldown(seededPawn, cooldownTicks);
                SendTraitDiseaseLetter(seededPawn);
            }
        }

        return false;
    }

    private static void SendTraitDiseaseLetter(Pawn pawn)
    {
        if (pawn == null || !PawnUtility.ShouldSendNotificationAbout(pawn))
        {
            return;
        }

        Find.LetterStack.ReceiveLetter(
            "Contagion_LetterLabelTraitDisease".Translate(pawn.LabelShortCap),
            "Contagion_LetterTraitDisease".Translate(pawn.LabelShortCap),
            LetterDefOf.NegativeEvent,
            pawn);
    }
}

[HarmonyPatch(typeof(IncidentWorker_Disease), "TryExecuteWorker")]
internal static class Patch_IncidentWorker_Disease_TryExecuteWorker
{
    public static bool Prefix(IncidentWorker_Disease __instance, IncidentParms parms, ref bool __result)
    {
        if (parms.target is not Map || !Patch_IncidentWorker_Disease_Helper.TryGetProfile(__instance, out ResolvedTransmissionProfile resolvedProfile))
        {
            return true;
        }

        if (!ContagionSeedingCoordinator.TryHandleStorytellerRequest(__instance, parms, resolvedProfile, out __result))
        {
            return true;
        }

        return false;
    }
}
