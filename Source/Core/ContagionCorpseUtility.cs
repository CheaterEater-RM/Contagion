using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

public static class ContagionCorpseUtility
{
    public static bool IsInfectedCorpse(Thing thing)
    {
        if (thing is not Corpse corpse)
        {
            return false;
        }

        Comp_InfectedCorpse comp = corpse.TryGetComp<Comp_InfectedCorpse>();
        if (comp?.IsSuspectedInfected == true)
        {
            return true;
        }

        return TryGetInfectedDisease(corpse, out HediffDef _);
    }

    public static bool IsUninfectedCorpse(Thing thing)
    {
        return thing is Corpse && !IsInfectedCorpse(thing);
    }

    public static bool TryGetInfectedDisease(Corpse corpse, out HediffDef diseaseDef)
    {
        diseaseDef = null;
        if (corpse == null)
        {
            return false;
        }

        // Comp-driven only: the infected flag and display reveal are set on the comp at spawn
        // (visible-before-death) or after a post-mortem inspection. Reading straight off the
        // inner pawn here would leak hidden/undiagnosed diseases into rendering, filters, and
        // the inspect string with no roll. Transmission uses a separate include-hidden path.
        Comp_InfectedCorpse comp = corpse.TryGetComp<Comp_InfectedCorpse>();
        if (comp?.IsInfected == true)
        {
            diseaseDef = comp.InfectedDiseaseDef;
            return diseaseDef != null;
        }

        return false;
    }

    public static bool TryGetCorpseContagiousDiseaseFromInnerPawn(Pawn innerPawn, out HediffDef diseaseDef)
    {
        diseaseDef = null;
        if (innerPawn == null)
        {
            return false;
        }

        if (innerPawn.RaceProps?.Animal == true)
        {
            diseaseDef = ContagionAnimalDiseaseUtility.GetAnimalCorpseContagiousDisease(innerPawn);
        }
        else if (innerPawn.RaceProps?.Humanlike == true)
        {
            diseaseDef = ContagionAnimalDiseaseUtility.GetHumanCorpseContagiousDisease(innerPawn);
        }

        return diseaseDef != null;
    }

    // Transmission-only detector: unlike the display-facing TryGetInfectedDisease, this does NOT
    // gate on diagnosis. Infected meat is infectious whether or not the disease was ever revealed,
    // so this includes undiagnosed hidden disease hediffs. Prefers the comp flag, then scans the
    // inner pawn with includeHidden.
    public static bool TryGetCorpseInfectionForTransmission(Corpse corpse, out HediffDef diseaseDef)
    {
        diseaseDef = null;
        if (corpse == null)
        {
            return false;
        }

        Comp_InfectedCorpse comp = corpse.TryGetComp<Comp_InfectedCorpse>();
        if (comp?.IsInfected == true)
        {
            diseaseDef = comp.InfectedDiseaseDef;
            return diseaseDef != null;
        }

        Pawn innerPawn = corpse.InnerPawn;
        if (innerPawn == null)
        {
            return false;
        }

        if (innerPawn.RaceProps?.Animal == true)
        {
            diseaseDef = ContagionAnimalDiseaseUtility.GetAnimalCorpseContagiousDisease(innerPawn, includeHidden: true);
        }
        else if (innerPawn.RaceProps?.Humanlike == true)
        {
            diseaseDef = ContagionAnimalDiseaseUtility.GetHumanCorpseContagiousDisease(innerPawn, includeHidden: true);
        }

        return diseaseDef != null;
    }

    // On-death roll: if an animal dies while carrying a hidden active disease (never diagnosed
    // and never passively presented), roll posthumousSymptomChance. On success the corpse is
    // marked infected — disease visible upon post-mortem inspection even though the animal
    // showed no visible symptoms before death.
    public static void TryApplyPosthumousPresentation(Pawn innerPawn, Comp_InfectedCorpse infectedComp)
    {
        if (innerPawn?.health?.hediffSet == null || infectedComp == null)
        {
            return;
        }

        List<Hediff> hediffs = innerPawn.health.hediffSet.hediffs;
        for (int i = 0; i < hediffs.Count; i++)
        {
            if (hediffs[i] is not Hediff_ContagionAnimalHiddenDisease { Diagnosed: false } hidden)
            {
                continue;
            }

            if (!DiseaseProfileCache.TryGetResolvedProfile(hidden.def, out ResolvedTransmissionProfile resolvedProfile))
            {
                continue;
            }

            if (!resolvedProfile.Profile.corpseContagious)
            {
                continue;
            }

            bool alreadyShowingSick = innerPawn.health.hediffSet.HasHediff(ContagionDefOf.Contagion_AnimalSick);
            if (!alreadyShowingSick && !Rand.Chance(resolvedProfile.Profile.posthumousSymptomChance))
            {
                continue;
            }

            infectedComp.SetInfection(resolvedProfile.DiseaseDef, identified: false);
            return;
        }

        // Fallback: if the animal had the sick signal at death but no confirmed active
        // disease, mark the corpse as suspected infected. Post-mortem inspection will
        // clear it as a false positive once the roll passes.
        if (!infectedComp.IsInfected && !infectedComp.IsSuspectedInfected
            && innerPawn.health.hediffSet.HasHediff(ContagionDefOf.Contagion_AnimalSick))
        {
            infectedComp.SetSuspectedInfection();
        }
    }

    public static void NotifyCorpseIngested(Corpse corpse, Pawn ingester)
    {
        if (corpse == null || ingester?.MapHeld == null || !TryGetCorpseInfectionForTransmission(corpse, out HediffDef diseaseDef))
        {
            return;
        }

        if (!DiseaseProfileCache.TryGetResolvedProfile(diseaseDef, out ResolvedTransmissionProfile resolvedProfile))
        {
            return;
        }

        // Eating a raw corpse is the most dangerous corpse interaction: it combines ingesting
        // contaminated tissue with direct, unprotected flea and fluid contact (tearing into the
        // carcass with bare hands and mouth). Roll each available vector independently. Once any
        // one seeds an incubation the remaining rolls naturally no-op, because the target is no
        // longer eligible (FindIncubation != null in GetTargetEligibilityFactor).

        // Foodborne: contaminated tissue, mitigated by the ingester's stomach resistance.
        if (ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Foodborne foodborneVector))
        {
            ContagionDiagnostics.Record(ContagionDiagnosticCounter.FoodborneAttempted);

            float foodborneBase = ContagionIngestionUtility.ApplyTaintedFoodInfectionFactor(
                foodborneVector.baseChancePerMeal,
                ingester);
            float transmissionMultiplier = Contagion_Mod.Settings?.EffectiveTransmissionMultiplier ?? 1f;
            float chance = ContagionTransmissionUtility.BuildSeederChance(
                foodborneBase,
                ingester,
                resolvedProfile,
                ingester.MapHeld,
                transmissionMultiplier,
                out HediffDef _);

            if (Rand.Chance(Mathf.Clamp01(chance))
                && ContagionDiseaseUtility.TrySeedIncubation(
                    ingester,
                    resolvedProfile.DiseaseDef,
                    resolvedProfile.PartsToAffect,
                    ContagionDiagnosticOrigin.Spread,
                    ContagionSeedSource.CorpseIngestion,
                    out HediffDef _))
            {
                ContagionDiagnostics.Record(ContagionDiagnosticCounter.FoodborneSeeded);
                ContagionDiagnostics.Trace($"Corpse ingestion (foodborne) transmission: {resolvedProfile.DiseaseDef.defName} to {ingester.LabelShortCap}.");
            }
        }

        // Direct contact vectors at full exposure (contextFactor 1f — no skill mitigation, unlike
        // butchering). Flea contact uses the same butcher-level base chance as tearing the carcass open.
        if (ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_CorpseFlea fleaVector))
        {
            ContagionCorpseExposureUtility.UpdateCorpseFleas(corpse, resolvedProfile, fleaVector, 0);
            ContagionCorpseExposureUtility.TryApplyFleaExposure(
                ingester,
                corpse,
                resolvedProfile,
                fleaVector,
                fleaVector.butcherBaseChance,
                1f);
        }

        if (ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_CorpseFluid fluidVector))
        {
            ContagionCorpseExposureUtility.TryApplyFluidExposure(
                ingester,
                corpse,
                resolvedProfile,
                fluidVector,
                ContagionCorpseFluidExposureKind.Butcher,
                1f);
        }
    }

    // Post-mortem inspection at a butchery table. One attempt per corpse, forever.
    // Skill pass: reveals the truth (disease named, or clean confirmed).
    // Skill fail: reports an inconclusive result — disease stays hidden (no false positives).
    public static void TryInspectCorpse(Corpse corpse, Pawn inspector)
    {
        if (corpse == null || inspector == null)
        {
            return;
        }

        Comp_InfectedCorpse comp = corpse.TryGetComp<Comp_InfectedCorpse>();
        // Allow re-inspection of suspected-infected corpses until a passing roll clears the
        // flag. The MarkInspectedFailed path sets HasBeenInspected, but a suspected corpse
        // with no real disease should remain diagnosable so the player isn't locked out.
        if (comp == null || (comp.HasBeenInspected && !comp.IsSuspectedInfected))
        {
            return;
        }

        bool isAnimalSubject = corpse.InnerPawn?.RaceProps?.Animal == true;

        // Determine if the corpse has a real disease to find.
        bool hasDisease = comp.IsInfected
            || TryGetCorpseContagiousDiseaseFromInnerPawn(corpse.InnerPawn, out HediffDef _);

        bool rollPassed = Rand.Chance(ContagionDiagnosticSkillUtility.ComputeInspectionChance(inspector, isAnimalSubject));

        if (rollPassed)
        {
            if (hasDisease)
            {
                // Ensure the comp has the disease flagged so MarkIdentified can reveal it.
                if (!comp.IsInfected)
                {
                    if (TryGetCorpseContagiousDiseaseFromInnerPawn(corpse.InnerPawn, out HediffDef diseaseDef))
                    {
                        comp.SetInfection(diseaseDef);
                    }
                }

                comp.MarkIdentified();
                Messages.Message(
                    "Contagion_InspectCorpseSuccessDisease".Translate(
                        inspector.LabelShortCap,
                        comp.InfectedDiseaseDef?.LabelCap ?? "unknown disease",
                        corpse.InnerPawn?.LabelShortCap ?? corpse.Label),
                    new LookTargets(corpse),
                    MessageTypeDefOf.PositiveEvent);
                ContagionDiagnostics.Trace($"Corpse inspection success: {inspector.LabelShortCap} identified {comp.InfectedDiseaseDef?.defName} in {corpse.InnerPawn?.LabelShortCap}.");
            }
            else
            {
                comp.MarkInspectedClean();
                Messages.Message(
                    "Contagion_InspectCorpseSuccessClean".Translate(
                        inspector.LabelShortCap,
                        corpse.InnerPawn?.LabelShortCap ?? corpse.Label),
                    new LookTargets(corpse),
                    MessageTypeDefOf.NeutralEvent);
                ContagionDiagnostics.Trace($"Corpse inspection success: {inspector.LabelShortCap} confirmed {corpse.InnerPawn?.LabelShortCap} is clean.");
            }
        }
        else
        {
            comp.MarkInspectedFailed();
            Messages.Message(
                "Contagion_InspectCorpseFail".Translate(
                    inspector.LabelShortCap,
                    corpse.InnerPawn?.LabelShortCap ?? corpse.Label),
                new LookTargets(corpse),
                MessageTypeDefOf.NeutralEvent);
            ContagionDiagnostics.Trace($"Corpse inspection failed: {inspector.LabelShortCap} found nothing conclusive on {corpse.InnerPawn?.LabelShortCap} (actual disease: {hasDisease}).");
        }
    }
}
