using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Contagion.Patches;

// Before recipe work starts, a cook may notice contaminated raw meat that has already
// been gathered for the bill. This is a weaker version of the passive butchery notice:
// meat is less informative than a whole corpse, but a skilled cook/medic can still spot it.
[HarmonyPatch(typeof(Toils_Recipe), nameof(Toils_Recipe.DoRecipeWork))]
internal static class Patch_Toils_Recipe_DoRecipeWork
{
    private const float CookNoticeButcheryFactor = 0.333f;

    public static void Postfix(Toil __result)
    {
        if (__result == null)
        {
            return;
        }

        Action originalInitAction = __result.initAction;
        __result.initAction = delegate
        {
            Pawn cook = __result.actor;
            if (TryNoticeAndDiscardInfectedMeat(cook))
            {
                return;
            }

            originalInitAction?.Invoke();
        };
    }

    private static bool TryNoticeAndDiscardInfectedMeat(Pawn cook)
    {
        Job job = cook?.CurJob;
        if (cook == null || job?.placedThings == null || job.placedThings.Count == 0)
        {
            return false;
        }

        List<ThingCountClass> infectedMeatEntries = null;
        HediffDef noticedDisease = null;
        bool hasHumanlikeMeat = false;
        bool hasAnimalMeat = false;

        for (int i = 0; i < job.placedThings.Count; i++)
        {
            ThingCountClass entry = job.placedThings[i];
            Thing thing = entry?.thing;
            if (!IsContaminatedRawMeat(thing, out Comp_ContaminatedFood comp))
            {
                continue;
            }

            if (entry.Count <= 0)
            {
                continue;
            }

            infectedMeatEntries ??= new List<ThingCountClass>();
            infectedMeatEntries.Add(entry);
            noticedDisease ??= comp.ContaminatedDiseaseDef;

            if (thing.def.ingestible?.sourceDef?.race?.Humanlike == true)
            {
                hasHumanlikeMeat = true;
            }
            else
            {
                hasAnimalMeat = true;
            }
        }

        if (infectedMeatEntries == null || infectedMeatEntries.Count == 0)
        {
            return false;
        }

        float noticeChance = ComputeCookNoticeChance(cook, hasAnimalMeat, hasHumanlikeMeat);
        bool noticed = Rand.Chance(noticeChance);
        ContagionDiagnostics.LogRoll(ContagionDebugVectorKind.Cooking, null, cook, noticedDisease, noticeChance, noticed);
        if (!noticed)
        {
            return false;
        }

        DiscardInfectedMeat(job, infectedMeatEntries);
        NotifyCookNoticed(cook, noticedDisease);
        cook.jobs.EndCurrentJob(JobCondition.Incompletable);
        return true;
    }

    private static float ComputeCookNoticeChance(Pawn cook, bool hasAnimalMeat, bool hasHumanlikeMeat)
    {
        float bestChance = 0f;
        if (hasAnimalMeat)
        {
            bestChance = Mathf.Max(
                bestChance,
                ContagionDiagnosticSkillUtility.ComputeDiagnosticChance(cook, isAnimalSubject: true, isButchery: true));
        }

        if (hasHumanlikeMeat)
        {
            bestChance = Mathf.Max(
                bestChance,
                ContagionDiagnosticSkillUtility.ComputeDiagnosticChance(cook, isAnimalSubject: false, isButchery: true));
        }

        return Mathf.Clamp01(bestChance * CookNoticeButcheryFactor);
    }

    private static bool IsContaminatedRawMeat(Thing thing, out Comp_ContaminatedFood comp)
    {
        comp = null;
        if (thing?.def == null || !thing.def.IsMeat)
        {
            return false;
        }

        comp = thing.TryGetComp<Comp_ContaminatedFood>();
        return comp?.IsContaminated == true;
    }

    private static void DiscardInfectedMeat(Job job, List<ThingCountClass> infectedMeatEntries)
    {
        for (int i = 0; i < infectedMeatEntries.Count; i++)
        {
            ThingCountClass entry = infectedMeatEntries[i];
            Thing thing = entry.thing;
            int discardCount = Mathf.Min(entry.Count, thing?.stackCount ?? 0);
            if (thing == null || thing.Destroyed || discardCount <= 0)
            {
                entry.Count = 0;
                continue;
            }

            if (discardCount < thing.stackCount)
            {
                Thing split = thing.SplitOff(discardCount);
                split.Destroy(DestroyMode.Vanish);
            }
            else
            {
                thing.Destroy(DestroyMode.Vanish);
            }

            entry.Count = 0;
        }

        job.placedThings.RemoveAll(entry => entry == null || entry.Count <= 0 || entry.thing == null || entry.thing.Destroyed);
        if (job.placedThings.Count == 0)
        {
            job.placedThings = null;
        }
    }

    private static void NotifyCookNoticed(Pawn cook, HediffDef disease)
    {
        ContagionDiagnostics.Trace($"Cooking aborted: {cook.LabelShortCap} noticed {disease?.defName ?? "unknown disease"} in raw meat and discarded it.");

        if (!PawnUtility.ShouldSendNotificationAbout(cook))
        {
            return;
        }

        Messages.Message(
            "Contagion_MessageCookNoticedInfectedMeat".Translate(cook.LabelShortCap, disease?.LabelCap ?? "Contagion_CorpseInfectivityUnknownDisease".Translate()),
            cook,
            MessageTypeDefOf.CautionInput,
            historical: false);
    }
}
