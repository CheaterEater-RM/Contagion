using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Contagion.Patches;

// Intercepts Corpse.ButcherProducts when the corpse carries a contagious disease.
//
// Two paths depending on whether the infection was already known:
//   Known (Comp_InfectedCorpse.IsInfected): corpse was labeled at spawn; player consciously
//     allowed butchering via the job filter. Proceed to meat contamination silently.
//   Unknown (found by scanning inner pawn): butcher discovered it mid-job. Roll notice chance:
//     pass → discard all products + alert; fail → contaminate meat.
//
// Notice chance via ContagionDiagnosticSkillUtility: Medical primary, Animals/Cooking as
// diminishing-return support, Sight-scaled, Medical Specialist bonus if Ideology active.
//
// IEnumerable<Thing> postfix: Harmony pipes __result through this method so we can
// conditionally yield or modify items before they reach GenRecipe.
[HarmonyPatch(typeof(Corpse), nameof(Corpse.ButcherProducts))]
internal static class Patch_Corpse_ButcherProducts
{
    public static IEnumerable<Thing> Postfix(IEnumerable<Thing> __result, Corpse __instance, Pawn butcher)
    {
        if (__instance?.InnerPawn == null)
        {
            foreach (Thing item in __result)
            {
                yield return item;
            }

            yield break;
        }

        Pawn innerPawn = __instance.InnerPawn;
        bool isHuman = innerPawn.RaceProps?.Humanlike == true;

        // Detect with the transmission-facing path (includes hidden/undiagnosed disease): infected
        // meat is infectious whether or not the infection was ever revealed. The diagnosis-gated
        // TryGetInfectedDisease would skip undiagnosed corpses entirely and make the "butcher
        // notices mid-job" branch below unreachable.
        ResolvedTransmissionProfile resolvedProfile = null;
        bool infected = ContagionCorpseUtility.TryGetCorpseInfectionForTransmission(__instance, out HediffDef contagiousDisease);
        bool profileUsable = infected
            && DiseaseProfileCache.TryGetResolvedProfile(contagiousDisease, out resolvedProfile)
            && resolvedProfile.Profile.affectsHumans;

        ContagionDiagnostics.Trace(
            $"Butchery start: {butcher?.LabelShortCap ?? "?"} on corpse of {innerPawn.LabelShortCap} — "
            + $"infected={infected} ({contagiousDisease?.defName ?? "none"}), profileUsable={profileUsable}.");

        if (!profileUsable)
        {
            foreach (Thing item in __result)
            {
                yield return item;
            }

            yield break;
        }

        // Notice-and-discard only fires when the infection was *unknown* at butchering time —
        // the butcher stumbled onto it mid-job. If the corpse was already flagged as infected
        // at spawn (Comp_InfectedCorpse.IsInfected == true), the player saw the "Infected
        // corpse" label and consciously allowed butchering via the job filter. Nothing to
        // discover; proceed straight to meat contamination.
        bool infectionWasKnown = __instance.TryGetComp<Comp_InfectedCorpse>()?.IsInfected == true;

        ApplyButcheryExposure(butcher, __instance, resolvedProfile);

        if (!infectionWasKnown)
        {
            float noticeChance = ContagionDiagnosticSkillUtility.ComputeDiagnosticChance(butcher, isAnimalSubject: !isHuman, isButchery: true);
            bool noticed = Rand.Chance(noticeChance);
            ContagionDiagnostics.LogRoll(ContagionDebugVectorKind.CorpseFluid, __instance, butcher, contagiousDisease, noticeChance, noticed);
            if (noticed)
            {
                // Butcher noticed an unknown infection — discard all products, forbid and alert.
                NotifyButcherNoticed(butcher, __instance, contagiousDisease);
                yield break;
            }
        }

        // Tie the corpse to the butchery bench so the meat chain has a clear origin point.
        int benchNodeId = -1;
        Building bench = butcher?.CurJob?.GetTarget(Verse.AI.TargetIndex.A).Thing as Building;
        if (bench != null)
        {
            int corpseNodeId = ContagionTrace.EnsureNode(__instance, contagiousDisease);
            benchNodeId = ContagionTrace.EnsureNode(bench, contagiousDisease);
            ContagionTrace.Edge(bench.Map, corpseNodeId, benchNodeId, ContagionDebugVectorKind.CorpseFluid);
        }

        // Didn't notice — contaminate produced meat items.
        foreach (Thing item in __result)
        {
            Comp_ContaminatedFood comp = item.TryGetComp<Comp_ContaminatedFood>();
            if (comp != null)
            {
                comp.SetContamination(contagiousDisease, 1.0f);
                // Stamp the upstream node so each meat stack links from the bench when it spawns.
                comp.sourceTraceNodeId = benchNodeId;
                ContagionDiagnostics.Record(ContagionDiagnosticCounter.MealsContaminated);
                ContagionDiagnostics.Trace($"Butchery contamination: {contagiousDisease.defName} baked into {item.def.defName} from {innerPawn.LabelShortCap}.");
            }
            else
            {
                // No Comp_ContaminatedFood on the product means it can't carry contamination — the
                // most likely cause is the comp not being injected onto this meat def. Surface it
                // so the path is auditable rather than silently dropping the infection.
                ContagionDiagnostics.Trace($"Butchery contamination SKIPPED: {item.def.defName} has no Comp_ContaminatedFood — contamination from {contagiousDisease.defName} lost.");
            }

            yield return item;
        }
    }

    private static void ApplyButcheryExposure(Pawn butcher, Corpse corpse, ResolvedTransmissionProfile resolvedProfile)
    {
        if (butcher == null || corpse == null || resolvedProfile?.Profile == null)
        {
            return;
        }

        float butcheryExposureFactor = ContagionCorpseExposureUtility.GetButcheryExposureFactor(butcher, corpse);

        if (ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_CorpseFlea fleaVector))
        {
            ContagionCorpseExposureUtility.UpdateCorpseFleas(corpse, resolvedProfile, fleaVector, 0);
            ContagionCorpseExposureUtility.TryApplyFleaExposure(
                butcher,
                corpse,
                resolvedProfile,
                fleaVector,
                fleaVector.butcherBaseChance,
                butcheryExposureFactor);
        }

        if (ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_CorpseFluid fluidVector))
        {
            ContagionCorpseExposureUtility.TryApplyFluidExposure(
                butcher,
                corpse,
                resolvedProfile,
                fluidVector,
                ContagionCorpseFluidExposureKind.Butcher,
                butcheryExposureFactor);
        }
    }

    private static void NotifyButcherNoticed(Pawn butcher, Corpse corpse, HediffDef disease)
    {
        if (butcher == null || corpse == null)
        {
            return;
        }

        // Forbid the map cell so the carcass remnants aren't handled.
        if (corpse.Spawned)
        {
            corpse.SetForbidden(true, warnOnFail: false);
        }

        ContagionDiagnostics.Trace($"Butchery aborted: {butcher.LabelShortCap} noticed {disease.defName} in {corpse.InnerPawn?.LabelShortCap} and discarded the products.");

        if (!PawnUtility.ShouldSendNotificationAbout(butcher))
        {
            return;
        }

        Messages.Message(
            "Contagion_MessageButcherNoticed".Translate(butcher.LabelShortCap, corpse.InnerPawn?.LabelShortCap ?? corpse.Label, disease.LabelCap),
            butcher,
            MessageTypeDefOf.CautionInput,
            historical: false);
    }
}
