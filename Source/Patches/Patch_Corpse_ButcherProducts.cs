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

        if (!ContagionCorpseUtility.TryGetInfectedDisease(__instance, out HediffDef contagiousDisease)
            || !DiseaseProfileCache.TryGetResolvedProfile(contagiousDisease, out ResolvedTransmissionProfile resolvedProfile)
            || !resolvedProfile.Profile.affectsHumans)
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

        if (!infectionWasKnown && Rand.Chance(ContagionDiagnosticSkillUtility.ComputeDiagnosticChance(butcher, isAnimalSubject: !isHuman, isButchery: true)))
        {
            // Butcher noticed an unknown infection — discard all products, forbid and alert.
            NotifyButcherNoticed(butcher, __instance, contagiousDisease);
            yield break;
        }

        // Didn't notice — contaminate produced meat items.
        foreach (Thing item in __result)
        {
            Comp_ContaminatedFood comp = item.TryGetComp<Comp_ContaminatedFood>();
            if (comp != null)
            {
                comp.SetContamination(contagiousDisease, 1.0f);
                ContagionDiagnostics.Record(ContagionDiagnosticCounter.MealsContaminated);
                ContagionDiagnostics.Trace($"Butchery contamination: {contagiousDisease.defName} baked into {item.def.defName} from {innerPawn.LabelShortCap}.");
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
