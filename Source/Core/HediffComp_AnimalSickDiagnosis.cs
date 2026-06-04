using Contagion.Patches;
using RimWorld;
using Verse;
using Verse.AI;

namespace Contagion;

public sealed class HediffCompProperties_AnimalSickDiagnosis : HediffCompProperties
{
    public HediffCompProperties_AnimalSickDiagnosis()
    {
        compClass = typeof(HediffComp_AnimalSickDiagnosis);
    }
}

public sealed class HediffComp_AnimalSickDiagnosis : HediffComp
{
    public override void CompTended(float quality, float maxQuality, int batchPosition = 0)
    {
        Pawn patient = Pawn;
        Pawn doctor = Patch_TendUtility_DoTend_TrackDoctor.CurrentDoctor;

        // A revealed disease should start a fresh vanilla tend job with its own medicine rules.
        Job currentJob = doctor?.CurJob;
        if (currentJob?.def == JobDefOf.TendPatient && currentJob.targetA.Pawn == patient)
        {
            currentJob.endAfterTendedOnce = true;
        }

        ContagionAnimalDiagnosisUtility.ResolveDiagnosisAttempt(patient, doctor);
    }
}
