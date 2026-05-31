using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion.Patches;

// Two-class pattern:
//   1. Patch_TendUtility_DoTend_TrackDoctor — prefix/postfix on DoTend to capture the doctor
//      in a static field for the duration of the call. Single-threaded; static field is safe.
//   2. Patch_Hediff_Tended_AnimalDiagnosis — postfix on Hediff.Tended, guarded to
//      Contagion_AnimalSick only. Fires exclusively when the sick-signal hediff is actually
//      among the hediffs being tended, which is the correct gate for H3.
//
// Diagnosis chance via ContagionDiagnosticSkillUtility: Medical primary (with Specialist
// bonus and Sight scaling), Animals as diminishing-return support, isButchery false.

[HarmonyPatch(typeof(TendUtility), nameof(TendUtility.DoTend))]
internal static class Patch_TendUtility_DoTend_TrackDoctor
{
    internal static Pawn CurrentDoctor;

    public static void Prefix(Pawn doctor)
    {
        CurrentDoctor = doctor;
    }

    public static void Postfix()
    {
        CurrentDoctor = null;
    }
}

[HarmonyPatch(typeof(Hediff), nameof(Hediff.Tended))]
internal static class Patch_Hediff_Tended_AnimalDiagnosis
{
    private const float MildDiagnosedSeverity = 0.10f;

    public static void Postfix(Hediff __instance)
    {
        if (__instance.def != ContagionDefOf.Contagion_AnimalSick)
        {
            return;
        }

        Pawn patient = __instance.pawn;
        if (patient?.RaceProps?.Animal != true)
        {
            return;
        }

        Pawn doctor = Patch_TendUtility_DoTend_TrackDoctor.CurrentDoctor;

        // Remove the sick signal — diagnosis always concludes it, regardless of outcome.
        patient.health.RemoveHediff(__instance);

        ResolvedTransmissionProfile resolvedProfile = ContagionAnimalDiseaseUtility.GetSickSignalProfile(patient);
        if (resolvedProfile == null)
        {
            SendExamClearMessage(doctor, patient);
            return;
        }

        // True positive — roll the unified diagnostic chance (Medical + Animals support + Sight).
        if (!Rand.Chance(ContagionDiagnosticSkillUtility.ComputeDiagnosticChance(doctor, isAnimalSubject: true, isButchery: false)))
        {
            SendExamClearMessage(doctor, patient);
            ContagionDiagnostics.Trace($"Diagnosis false negative: {doctor?.LabelShortCap} missed {resolvedProfile.DiseaseDef.defName} in {patient.LabelShortCap}.");
            return;
        }

        RevealDiagnosis(patient, resolvedProfile, doctor);
        ContagionDiagnostics.Trace($"Diagnosis success: {doctor?.LabelShortCap} identified {resolvedProfile.DiseaseDef.defName} in {patient.LabelShortCap}.");
    }

    private static void RevealDiagnosis(Pawn patient, ResolvedTransmissionProfile resolvedProfile, Pawn doctor)
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

        var addedHediffs = new System.Collections.Generic.List<Hediff>();
        var partsToAffect = ContagionDiseaseUtility.ResolvePartsForPawn(patient, resolvedProfile, resolvedProfile.PartsToAffect);
        bool applied = existingDisease != null
            || HediffGiverUtility.TryApply(
                patient,
                targetDiseaseDef,
                partsToAffect,
                outAddedHediffs: addedHediffs);

        if (existingDisease != null)
        {
            RevealHediff(existingDisease, targetDiseaseDef);
        }

        if (applied)
        {
            foreach (Hediff hediff in addedHediffs)
            {
                if (hediff?.def == targetDiseaseDef)
                {
                    RevealHediff(hediff, targetDiseaseDef);
                }
            }
        }

        if (PawnUtility.ShouldSendNotificationAbout(patient) || PawnUtility.ShouldSendNotificationAbout(doctor))
        {
            Find.LetterStack.ReceiveLetter(
                "Contagion_LetterLabelAnimalDiagnosed".Translate(patient.LabelShortCap, targetDiseaseDef.LabelCap),
                "Contagion_LetterAnimalDiagnosed".Translate(doctor?.LabelShortCap ?? "?", patient.LabelShortCap, targetDiseaseDef.LabelCap),
                LetterDefOf.NegativeEvent,
                patient);
        }
    }

    private static void RevealHediff(Hediff hediff, HediffDef targetDiseaseDef)
    {
        if (hediff == null)
        {
            return;
        }

        if (hediff is Hediff_ContagionAnimalHiddenDisease hiddenDisease)
        {
            hiddenDisease.MarkDiagnosed();
        }

        if (hediff.def == targetDiseaseDef)
        {
            hediff.Severity = Mathf.Min(hediff.def.maxSeverity, MildDiagnosedSeverity);
        }
    }

    private static void SendExamClearMessage(Pawn doctor, Pawn patient)
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
