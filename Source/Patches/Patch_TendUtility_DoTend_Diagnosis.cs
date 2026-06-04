using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Contagion.Patches;

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

    // A void finalizer always runs, including when DoTend or another patch throws.
    [HarmonyFinalizer]
    public static void Finalizer()
    {
        if (_doctorStack?.Count > 0)
        {
            _doctorStack.Pop();
        }
    }
}

[HarmonyPatch(typeof(TendUtility), nameof(TendUtility.DoTend))]
internal static class Patch_TendUtility_DoTend_NoMedicineForDiagnosis
{
    public static void Prefix(Pawn patient, ref Medicine medicine)
    {
        if (medicine != null
            && ContagionAnimalDiagnosisUtility.IsNextTreatmentOnlyAnimalSick(patient, usingMedicine: true))
        {
            medicine = null;
        }
    }
}

[HarmonyPatch(typeof(Medicine), nameof(Medicine.GetMedicineCountToFullyHeal))]
internal static class Patch_Medicine_GetMedicineCountToFullyHeal_AnimalDiagnosis
{
    public static void Postfix(Pawn pawn, ref int __result)
    {
        int diagnosisTreatments = ContagionAnimalDiagnosisUtility.CountTendableAnimalSickSignals(pawn);
        if (diagnosisTreatments > 0)
        {
            __result = System.Math.Max(0, __result - diagnosisTreatments);
        }
    }
}

[HarmonyPatch(typeof(JobDriver_TendPatient), nameof(JobDriver_TendPatient.TryMakePreToilReservations))]
internal static class Patch_JobDriver_TendPatient_NoStaleDiagnosisMedicineReservation
{
    public static void Prefix(JobDriver_TendPatient __instance)
    {
        Pawn patient = __instance.job?.targetA.Pawn;
        if (ContagionAnimalDiagnosisUtility.CountTendableAnimalSickSignals(patient) > 0
            && Medicine.GetMedicineCountToFullyHeal(patient) == 0)
        {
            __instance.job.SetTarget(TargetIndex.B, LocalTargetInfo.Invalid);
            __instance.job.SetTarget(TargetIndex.C, LocalTargetInfo.Invalid);
        }
    }
}
