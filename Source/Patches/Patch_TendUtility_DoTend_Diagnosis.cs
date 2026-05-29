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

    public static void Postfix(Hediff __instance, float quality)
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

        // True positive — quality already encodes the doctor's Medical skill.
        if (!Rand.Chance(Mathf.Clamp01(quality)))
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
        Hediff_ContagionIncubation incubation = ContagionDiseaseUtility.FindIncubation(patient, resolvedProfile.DiseaseDef);
        if (incubation != null)
        {
            patient.health.RemoveHediff(incubation);
        }

        var addedHediffs = new System.Collections.Generic.List<Hediff>();
        bool applied = HediffGiverUtility.TryApply(
            patient,
            resolvedProfile.DiseaseDef,
            resolvedProfile.PartsToAffect,
            outAddedHediffs: addedHediffs);

        if (applied)
        {
            foreach (Hediff hediff in addedHediffs)
            {
                if (hediff?.def == resolvedProfile.DiseaseDef)
                {
                    hediff.Severity = Mathf.Min(hediff.def.maxSeverity, MildDiagnosedSeverity);
                }
            }
        }

        if (PawnUtility.ShouldSendNotificationAbout(patient) || PawnUtility.ShouldSendNotificationAbout(doctor))
        {
            Find.LetterStack.ReceiveLetter(
                "Contagion_LetterLabelAnimalDiagnosed".Translate(patient.LabelShortCap, resolvedProfile.DiseaseDef.LabelCap),
                "Contagion_LetterAnimalDiagnosed".Translate(doctor?.LabelShortCap ?? "?", patient.LabelShortCap, resolvedProfile.DiseaseDef.LabelCap),
                LetterDefOf.NegativeEvent,
                patient);
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
