using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Contagion.Patches;

// Two-class pattern:
//   1. Patch_TendUtility_DoTend_TrackDoctor — prefix/postfix on DoTend to capture the doctor
//      for the duration of the call.
//   2. Patch_Hediff_Tended_AnimalDiagnosis — postfix on Hediff.Tended, guarded to
//      Contagion_AnimalSick only. Fires exclusively when the sick-signal hediff is actually
//      among the hediffs being tended, which is the correct gate for H3.
//
// Diagnosis logic lives in ContagionAnimalDiagnosisUtility and is shared with
// JobDriver_DiagnoseAnimal (proactive examination of non-presenting animals).

[HarmonyPatch(typeof(TendUtility), nameof(TendUtility.DoTend))]
internal static class Patch_TendUtility_DoTend_TrackDoctor
{
    [System.ThreadStatic]
    private static Stack<Pawn> _doctorStack;

    internal static Pawn CurrentDoctor => _doctorStack?.Count > 0 ? _doctorStack.Peek() : null;

    public static void Prefix(Pawn doctor)
    {
        _doctorStack ??= new Stack<Pawn>();
        _doctorStack.Push(doctor);
    }

    public static void Postfix()
    {
        if (_doctorStack?.Count > 0)
        {
            _doctorStack.Pop();
        }
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

