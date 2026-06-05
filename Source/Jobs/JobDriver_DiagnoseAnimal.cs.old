using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Contagion;

// Proactive animal examination: a colonist manually examines a domestic animal for hidden
// illness, even if the animal is not presenting Contagion_AnimalSick. No workbench required.
//
// Outcomes (shared with the tending-path via ContagionAnimalDiagnosisUtility):
//   Pass  → disease revealed (letter sent), 1-week cooldown applied.
//   Fail  → "nothing found" message, 1-week cooldown applied.
//   No disease → same as fail.
//
// TargetA = the animal to examine.
public class JobDriver_DiagnoseAnimal : JobDriver
{
    private const int ExamineDurationTicks = 100; // milking a cow takes 400 ticks
    private const float ExamineMedicineXp = 87.5f; // Vanilla animal tend XP without medicine.

    private Pawn Animal => (Pawn)job.GetTarget(TargetIndex.A).Thing;

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        return pawn.Reserve(job.targetA, job, errorOnFailed: errorOnFailed);
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
        this.FailOnAggroMentalState(TargetIndex.A);
        this.FailOn(() => Animal.Dead);
        this.FailOn(() => Animal.Faction != Faction.OfPlayer);
        // Abort early if the cooldown was applied by a concurrent tending job.
        this.FailOn(() => ContagionAnimalDiagnosisUtility.HasDiagnosisCooldown(Animal));

        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch)
            .FailOn(() => Animal.Dead);

        Toil examine = Toils_General.WaitWith(
            TargetIndex.A,
            ExamineDurationTicks,
            useProgressBar: true,
            maintainPosture: true,
            maintainSleep: true,
            TargetIndex.A,
            PathEndMode.Touch);
        examine.PlaySustainerOrSound(() => SoundDefOf.Recipe_Surgery);
        examine.activeSkill = () => SkillDefOf.Medicine;
        examine.AddFinishAction(() =>
        {
            Pawn animal = Animal;
            if (animal?.CurJobDef == JobDefOf.Wait_MaintainPosture)
            {
                animal.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
        });
        yield return examine;

        Toil diagnose = ToilMaker.MakeToil("ContagionDiagnoseAnimal_Diagnose");
        diagnose.initAction = () =>
        {
            Pawn animal = Animal;
            pawn.skills?.Learn(SkillDefOf.Medicine, ExamineMedicineXp);
            ContagionAnimalDiagnosisUtility.ResolveDiagnosisAttempt(animal, pawn);
        };
        diagnose.defaultCompleteMode = ToilCompleteMode.Instant;
        yield return diagnose;
    }
}
