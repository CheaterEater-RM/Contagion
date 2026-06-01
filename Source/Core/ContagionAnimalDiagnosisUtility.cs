using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

// Shared logic for live-animal vet examination.
//
// Two paths call this:
//   1. Tending-path patch (Patch_Hediff_Tended_AnimalDiagnosis) — fires when a vet tends
//      Contagion_AnimalSick on a presenting animal.
//   2. Proactive examination job (JobDriver_DiagnoseAnimal) — fires when the player manually
//      assigns a colonist to examine a domestic animal, even when no sick signal is visible.
//
// After any examination (pass or fail, both paths) the animal receives
// Contagion_AnimalDiagnosisCooldown (1 week) so the player cannot spam attempts to
// statistically guarantee finding a hidden disease before it has had a chance to present.
internal static class ContagionAnimalDiagnosisUtility
{
    // Freshly-revealed diseases start at mild severity so the player has time to act.
    internal const float MildDiagnosedSeverity = 0.10f;

    // 1 game-week (7 × 60,000 ticks).
    internal const int DiagnosisCooldownTicks = 420_000;

    internal static bool HasDiagnosisCooldown(Pawn animal)
    {
        return animal?.health?.hediffSet?.HasHediff(ContagionDefOf.Contagion_AnimalDiagnosisCooldown) == true;
    }

    // Applies (or resets) the 1-week cooldown on the examined animal.
    internal static void ApplyDiagnosisCooldown(Pawn animal)
    {
        if (animal?.health == null)
        {
            return;
        }

        // Remove any existing instance before adding a fresh one.
        Hediff existing = animal.health.hediffSet.GetFirstHediffOfDef(ContagionDefOf.Contagion_AnimalDiagnosisCooldown);
        if (existing != null)
        {
            animal.health.RemoveHediff(existing);
        }

        var cooldown = (Hediff_ContagionTraitSeedCooldown)HediffMaker.MakeHediff(
            ContagionDefOf.Contagion_AnimalDiagnosisCooldown, animal);
        cooldown.Configure(Find.TickManager.TicksGame + DiagnosisCooldownTicks);
        animal.health.AddHediff(cooldown);
    }

    // Core diagnosis roll. Returns true when the vet identifies a disease.
    // Does NOT touch the Contagion_AnimalSick signal or apply the cooldown — the
    // caller handles both to stay consistent across the two call paths.
    internal static bool TryDiagnoseAnimal(Pawn patient, Pawn doctor)
    {
        if (patient == null || doctor == null)
        {
            return false;
        }

        ResolvedTransmissionProfile resolvedProfile = ContagionAnimalDiseaseUtility.GetSickSignalProfile(patient);
        if (resolvedProfile == null)
        {
            // Nothing to find — vet confirms the animal is fine.
            SendExamClearMessage(doctor, patient);
            return false;
        }

        if (!Rand.Chance(ContagionDiagnosticSkillUtility.ComputeDiagnosticChance(
                doctor, isAnimalSubject: true, isButchery: false)))
        {
            // Present disease but the vet missed it — false negative.
            SendExamClearMessage(doctor, patient);
            ContagionDiagnostics.Trace($"Diagnosis false negative: {doctor.LabelShortCap} missed {resolvedProfile.DiseaseDef.defName} in {patient.LabelShortCap}.");
            return false;
        }

        RevealDiagnosis(patient, resolvedProfile, doctor);
        ContagionDiagnostics.Trace($"Diagnosis success: {doctor.LabelShortCap} identified {resolvedProfile.DiseaseDef.defName} in {patient.LabelShortCap}.");
        return true;
    }

    // Reveals a confirmed disease: removes incubation, applies the visible hediff,
    // sets mild severity, and sends a letter to the player.
    internal static void RevealDiagnosis(Pawn patient, ResolvedTransmissionProfile resolvedProfile, Pawn doctor)
    {
        HediffDef targetDiseaseDef = resolvedProfile.ResolveHediffForPawn(patient);
        Hediff existingDisease = patient.health.hediffSet.GetFirstHediffOfDef(targetDiseaseDef);

        Hediff_ContagionIncubation incubation = ContagionDiseaseUtility.FindIncubation(patient, targetDiseaseDef);
        if (incubation == null && targetDiseaseDef != resolvedProfile.DiseaseDef)
        {
            incubation = ContagionDiseaseUtility.FindIncubation(patient, resolvedProfile.DiseaseDef);
        }

        if (incubation != null)
        {
            patient.health.RemoveHediff(incubation);
        }

        var addedHediffs = new List<Hediff>();
        var partsToAffect = ContagionDiseaseUtility.ResolvePartsForPawn(patient, resolvedProfile, resolvedProfile.PartsToAffect);
        bool applied = existingDisease != null
            || HediffGiverUtility.TryApply(
                patient,
                targetDiseaseDef,
                partsToAffect,
                outAddedHediffs: addedHediffs);

        if (existingDisease != null)
        {
            RevealHediff(existingDisease, targetDiseaseDef, setMildSeverity: false);
        }

        if (applied)
        {
            foreach (Hediff hediff in addedHediffs)
            {
                if (hediff?.def == targetDiseaseDef)
                {
                    RevealHediff(hediff, targetDiseaseDef, setMildSeverity: true);
                }
            }
        }

        if (PawnUtility.ShouldSendNotificationAbout(patient) || PawnUtility.ShouldSendNotificationAbout(doctor))
        {
            Find.LetterStack.ReceiveLetter(
                "Contagion_LetterLabelAnimalDiagnosed".Translate(patient.LabelShortCap, targetDiseaseDef.LabelCap),
                "Contagion_LetterAnimalDiagnosed".Translate(
                    doctor?.LabelShortCap ?? "?", patient.LabelShortCap, targetDiseaseDef.LabelCap),
                LetterDefOf.NegativeEvent,
                patient);
        }
    }

    private static void RevealHediff(Hediff hediff, HediffDef targetDiseaseDef, bool setMildSeverity)
    {
        if (hediff == null)
        {
            return;
        }

        if (hediff is Hediff_ContagionAnimalHiddenDisease hiddenDisease)
        {
            hiddenDisease.MarkDiagnosed();
        }

        if (setMildSeverity && hediff.def == targetDiseaseDef)
        {
            hediff.Severity = Mathf.Min(hediff.def.maxSeverity, MildDiagnosedSeverity);
        }
    }

    internal static void SendExamClearMessage(Pawn doctor, Pawn patient)
    {
        if (PawnUtility.ShouldSendNotificationAbout(patient) || PawnUtility.ShouldSendNotificationAbout(doctor))
        {
            Messages.Message(
                "Contagion_MessageAnimalExamClear".Translate(doctor?.LabelShortCap ?? "?", patient.LabelShortCap),
                patient,
                MessageTypeDefOf.NeutralEvent,
                historical: false);
        }
    }
}
