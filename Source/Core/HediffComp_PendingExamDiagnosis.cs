using Contagion.Patches;
using RimWorld;
using Verse;
using Verse.AI;

namespace Contagion;

public sealed class HediffCompProperties_PendingExamDiagnosis : HediffCompProperties
{
    public HediffCompProperties_PendingExamDiagnosis()
    {
        compClass = typeof(HediffComp_PendingExamDiagnosis);
    }
}

// Fires the proactive animal examination through the vanilla tend path. The player flags an animal
// (FloatMenuOptionProvider_DiagnoseAnimal adds Contagion_PendingExam); a vet tends it with vanilla
// JobDriver_TendPatient, which calls CompTended here. Routing through vanilla tending means an
// in-flight save holds only a vanilla TendPatient job/driver, so dropping Contagion mid-exam can't
// lock the pawn — the same reason the visible-sick path (HediffComp_AnimalSickDiagnosis) survives
// removal.
//
// The marker is guaranteed to clear three ways, so an animal is never left silently flagged:
//   1. Tended       → CompTended resolves the roll and removes the marker (finally).
//   2. Never tended → HediffCompProperties_Disappears auto-removes it after a day.
//   3. Becomes sick → CompPostTickInterval yields to Contagion_AnimalSick.
public sealed class HediffComp_PendingExamDiagnosis : HediffComp
{
    public override void CompTended(float quality, float maxQuality, int batchPosition = 0)
    {
        Pawn patient = Pawn;
        Pawn doctor = Patch_TendUtility_DoTend_TrackDoctor.CurrentDoctor;

        // A revealed disease should start a fresh vanilla tend job with its own medicine rules
        // rather than continuing this no-medicine exam tend. Mirrors HediffComp_AnimalSickDiagnosis.
        Job currentJob = doctor?.CurJob;
        if (currentJob?.def == JobDefOf.TendPatient && currentJob.targetA.Pawn == patient)
        {
            currentJob.endAfterTendedOnce = true;
        }

        try
        {
            ContagionAnimalDiagnosisUtility.ResolveDiagnosisAttempt(patient, doctor);
        }
        finally
        {
            // Always clear the flag, even if the roll threw, so the animal is never left flagged.
            RemoveSelf(patient);
        }
    }

    // Yield to the real illness path: if the animal becomes visibly sick while still only flagged,
    // drop the marker so the vanilla Contagion_AnimalSick tend/diagnosis owns it (no double exam).
    public override void CompPostTickInterval(ref float severityAdjustment, int delta)
    {
        Pawn pawn = Pawn;
        if (pawn != null && ContagionAnimalDiagnosisUtility.HasVisibleSickSignal(pawn))
        {
            RemoveSelf(pawn);
        }
    }

    private void RemoveSelf(Pawn pawn)
    {
        if (parent != null && pawn?.health?.hediffSet?.hediffs.Contains(parent) == true)
        {
            pawn.health.RemoveHediff(parent);
        }
    }
}
