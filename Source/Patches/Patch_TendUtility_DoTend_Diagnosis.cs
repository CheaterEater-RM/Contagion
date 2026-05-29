using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion.Patches;

// When a vet tends the Contagion_AnimalSick hediff, run the diagnosis mechanic:
//   - True positive + good roll (Medical skill):  reveal the disease (mild active), send letter.
//   - True positive + bad roll (false negative):  send "nothing found" message. Disease stays hidden.
//   - False positive (no underlying disease):     send "nothing found" message.
// The Contagion_AnimalSick hediff is always removed after examination.
[HarmonyPatch(typeof(TendUtility), nameof(TendUtility.DoTend))]
internal static class Patch_TendUtility_DoTend_Diagnosis
{
    private const float MildDiagnosedSeverity = 0.10f;

    public static void Postfix(Pawn doctor, Pawn patient, Medicine medicine)
    {
        if (doctor == null || patient == null || patient.RaceProps?.Animal != true)
        {
            return;
        }

        Hediff sickHediff = patient.health.hediffSet.GetFirstHediffOfDef(ContagionDefOf.Contagion_AnimalSick);
        if (sickHediff == null)
        {
            return;
        }

        patient.health.RemoveHediff(sickHediff);

        ResolvedTransmissionProfile resolvedProfile = ContagionAnimalDiseaseUtility.GetSickSignalProfile(patient);
        if (resolvedProfile == null)
        {
            SendExamClearMessage(doctor, patient);
            return;
        }

        // True positive — roll Medical skill for diagnosis accuracy.
        float diagnosisChance = Mathf.Clamp01(doctor.skills?.GetSkill(SkillDefOf.Medicine)?.Level / 15f ?? 0f);
        if (!Rand.Chance(diagnosisChance))
        {
            SendExamClearMessage(doctor, patient);
            ContagionDiagnostics.Trace($"Diagnosis false negative: {doctor.LabelShortCap} missed {resolvedProfile.DiseaseDef.defName} in {patient.LabelShortCap}.");
            return;
        }

        RevealDiagnosis(patient, resolvedProfile, doctor);
        ContagionDiagnostics.Trace($"Diagnosis success: {doctor.LabelShortCap} identified {resolvedProfile.DiseaseDef.defName} in {patient.LabelShortCap}.");
    }

    private static void RevealDiagnosis(Pawn patient, ResolvedTransmissionProfile resolvedProfile, Pawn doctor)
    {
        // Remove existing incubation and apply a mild active disease — the diagnosis effectively
        // collapses the hidden incubation into an early active case.
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
                "Contagion_LetterAnimalDiagnosed".Translate(doctor.LabelShortCap, patient.LabelShortCap, resolvedProfile.DiseaseDef.LabelCap),
                LetterDefOf.NegativeEvent,
                patient);
        }
    }

    private static void SendExamClearMessage(Pawn doctor, Pawn patient)
    {
        if (PawnUtility.ShouldSendNotificationAbout(patient) || PawnUtility.ShouldSendNotificationAbout(doctor))
        {
            Messages.Message(
                "Contagion_MessageAnimalExamClear".Translate(doctor.LabelShortCap, patient.LabelShortCap),
                patient,
                MessageTypeDefOf.NeutralEvent,
                historical: false);
        }
    }
}
