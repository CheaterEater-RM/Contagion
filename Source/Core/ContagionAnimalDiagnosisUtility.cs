using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

// Shared logic for live-animal vet examination.
//
// Two paths call this, both through vanilla tending (HediffWithComps.CompTended):
//   1. Visible sickness — HediffComp_AnimalSickDiagnosis on Contagion_AnimalSick.
//   2. Proactive screening — HediffComp_PendingExamDiagnosis on Contagion_PendingExam, applied
//      when the player flags a not-visibly-sick domestic animal for examination.
//
// After any examination (pass or fail, both paths) the animal receives
// Contagion_AnimalDiagnosisCooldown (1 week) so the player cannot spam attempts to
// statistically guarantee finding a hidden disease before it has had a chance to present.
internal static class ContagionAnimalDiagnosisUtility
{
    [System.ThreadStatic]
    private static List<Hediff> _nextTreatment;

    // Freshly-revealed diseases start at mild severity so the player has time to act.
    internal const float MildDiagnosedSeverity = 0.10f;

    // 1 game-week (7 × 60,000 ticks).
    internal const int DiagnosisCooldownTicks = 420_000;

    internal static bool HasDiagnosisCooldown(Pawn animal)
    {
        return animal?.health?.hediffSet?.HasHediff(ContagionDefOf.Contagion_AnimalDiagnosisCooldown) == true;
    }

    // True when the animal already shows the visible sick signal. The proactive exam defers to the
    // vanilla tend/diagnosis path in that case (no duplicate work), and the pending-exam marker
    // self-removes if this becomes true while it is still flagged.
    internal static bool HasVisibleSickSignal(Pawn animal)
    {
        return animal?.health?.hediffSet?.HasHediff(ContagionDefOf.Contagion_AnimalSick) == true;
    }

    // The two diagnostic signals tended through the vanilla tend path: the visible sick signal and
    // the proactive examination flag. Both are no-medicine treatments (see the medicine-suppression
    // patches) and both resolve a diagnosis attempt on CompTended.
    internal static bool IsDiagnosticSignalDef(HediffDef def)
    {
        return def == ContagionDefOf.Contagion_AnimalSick || def == ContagionDefOf.Contagion_PendingExam;
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

        var cooldown = (Hediff_ContagionExpiry)HediffMaker.MakeHediff(
            ContagionDefOf.Contagion_AnimalDiagnosisCooldown, animal);
        cooldown.Configure(Find.TickManager.TicksGame + DiagnosisCooldownTicks);
        animal.health.AddHediff(cooldown);
    }

    // Resolves exactly one diagnosis attempt. Cleanup is guaranteed so an error cannot leave
    // the signal immediately tendable and restart vanilla's TendPatient loop.
    internal static bool ResolveDiagnosisAttempt(Pawn patient, Pawn doctor)
    {
        try
        {
            if (doctor == null)
            {
                ContagionDiagnostics.Trace(
                    $"Animal diagnosis resolved without doctor context: {patient?.LabelShortCap ?? "null"}.");
                return false;
            }

            return TryDiagnoseAnimal(patient, doctor);
        }
        finally
        {
            try
            {
                RemoveAllSickSignals(patient);
            }
            finally
            {
                ApplyDiagnosisCooldown(patient);
            }
        }
    }

    // Vanilla uses this count for medicine selection, reservations, pickup, and replenishment.
    // Diagnostic signals (sick signal + proactive exam flag) are no-medicine treatments, so they
    // must not contribute to that count.
    internal static int CountTendableDiagnosticSignals(Pawn patient)
    {
        if (patient?.health?.hediffSet == null)
        {
            return 0;
        }

        int count = 0;
        List<Hediff> hediffs = patient.health.hediffSet.hediffs;
        for (int i = 0; i < hediffs.Count; i++)
        {
            if (IsDiagnosticSignalDef(hediffs[i].def) && hediffs[i].TendableNow())
            {
                count++;
            }
        }

        return count;
    }

    // A mixed tend job may already be carrying medicine for a real injury or disease. Suppress
    // that medicine only when vanilla has selected a diagnostic signal as this treatment.
    internal static bool IsNextTreatmentOnlyDiagnosticSignal(Pawn patient, bool usingMedicine)
    {
        if (patient?.health?.hediffSet == null)
        {
            return false;
        }

        _nextTreatment ??= new List<Hediff>();
        try
        {
            TendUtility.GetOptimalHediffsToTendWithSingleTreatment(patient, usingMedicine, _nextTreatment);
            if (_nextTreatment.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < _nextTreatment.Count; i++)
            {
                if (!IsDiagnosticSignalDef(_nextTreatment[i].def))
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            _nextTreatment.Clear();
        }
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

    private static void RemoveAllSickSignals(Pawn patient)
    {
        if (patient?.health?.hediffSet == null)
        {
            return;
        }

        for (int i = patient.health.hediffSet.hediffs.Count - 1; i >= 0; i--)
        {
            Hediff hediff = patient.health.hediffSet.hediffs[i];
            if (hediff.def == ContagionDefOf.Contagion_AnimalSick)
            {
                patient.health.RemoveHediff(hediff);
            }
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
