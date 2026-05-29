using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion.Patches;

// When an infected corpse is butchered, either the butcher notices and discards the carcass
// (skill check pass) or contamination is baked into the raw meat (skill check fail).
//
// Notice chance uses a sigmoid of combined Animals + Cooking skill:
// ~75% at combined 10, ~95% at combined 15, hard cap 99.5%.
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
        bool isAnimal = innerPawn.RaceProps?.Animal == true;
        bool isHuman = innerPawn.RaceProps?.Humanlike == true;

        HediffDef contagiousDisease = isAnimal
            ? ContagionAnimalDiseaseUtility.GetAnimalCorpseContagiousDisease(innerPawn)
            : isHuman
                ? ContagionAnimalDiseaseUtility.GetHumanCorpseContagiousDisease(innerPawn)
                : null;

        if (contagiousDisease == null
            || !DiseaseProfileCache.TryGetResolvedProfile(contagiousDisease, out ResolvedTransmissionProfile resolvedProfile)
            || !resolvedProfile.Profile.affectsHumans)
        {
            foreach (Thing item in __result)
            {
                yield return item;
            }

            yield break;
        }

        float noticeChance = ComputeNoticeChance(butcher);

        if (Rand.Chance(noticeChance))
        {
            // Butcher noticed — discard all products, forbid and alert.
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

    // Sigmoid of (Animals skill + Cooking), capped at 99.5%.
    // k=0.37, x0=7 → ~75% at combined 10, ~95% at combined 15.
    private static float ComputeNoticeChance(Pawn butcher)
    {
        if (butcher?.skills == null)
        {
            return 0f;
        }

        float domain = butcher.skills.GetSkill(SkillDefOf.Animals).Level;
        float cooking = butcher.skills.GetSkill(SkillDefOf.Cooking).Level;
        float combined = domain + cooking;

        float sigmoid = 1f / (1f + Mathf.Exp(-0.37f * (combined - 7f)));
        return Mathf.Min(sigmoid, 0.995f);
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
