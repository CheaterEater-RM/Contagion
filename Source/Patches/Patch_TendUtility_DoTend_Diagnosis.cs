using HarmonyLib;
using RimWorld;
using Verse;

namespace Contagion.Patches;

// Two-class pattern:
//   1. Patch_TendUtility_DoTend_TrackDoctor — prefix/postfix on DoTend to capture the doctor
//      in a static field for the duration of the call. Single-threaded; static field is safe.
//   2. Patch_Hediff_Tended_AnimalDiagnosis — postfix on Hediff.Tended, guarded to
//      Contagion_AnimalSick only. Fires exclusively when the sick-signal hediff is actually
//      among the hediffs being tended, which is the correct gate for H3.
//
// Diagnosis logic lives in ContagionAnimalDiagnosisUtility and is shared with
// JobDriver_DiagnoseAnimal (proactive examination of non-presenting animals).

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

        // Remove the sick signal — the examination always concludes it, regardless of outcome.
        patient.health.RemoveHediff(__instance);

        ContagionAnimalDiagnosisUtility.TryDiagnoseAnimal(patient, doctor);
        ContagionAnimalDiagnosisUtility.ApplyDiagnosisCooldown(patient);
    }
}

