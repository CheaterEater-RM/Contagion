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
    private const int ExamineDurationTicks = 300;

    private Pawn Animal => (Pawn)job.GetTarget(TargetIndex.A).Thing;

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        return pawn.Reserve(job.targetA, job, errorOnFailed: errorOnFailed);
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDestroyedOrNull(TargetIndex.A);
        this.FailOn(() => Animal.Dead);
        this.FailOn(() => Animal.Faction != Faction.OfPlayer);
        // Abort early if the cooldown was applied by a concurrent tending job.
        this.FailOn(() => ContagionAnimalDiagnosisUtility.HasDiagnosisCooldown(Animal));

        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch)
            .FailOn(() => Animal.Dead);

        Toil examine = Toils_General.Wait(ExamineDurationTicks, TargetIndex.A);
        examine.WithProgressBarToilDelay(TargetIndex.A);
        examine.PlaySustainerOrSound(() => SoundDefOf.Recipe_Surgery);
        yield return examine;

        Toil diagnose = ToilMaker.MakeToil("ContagionDiagnoseAnimal_Diagnose");
        diagnose.initAction = () =>
        {
            Pawn animal = Animal;

            // If the animal is also showing the sick signal, clear it — examination
            // concludes that check regardless of what the proactive roll finds.
            Hediff sickHediff = animal.health.hediffSet.GetFirstHediffOfDef(ContagionDefOf.Contagion_AnimalSick);
            if (sickHediff != null)
            {
                animal.health.RemoveHediff(sickHediff);
            }

            ContagionAnimalDiagnosisUtility.TryDiagnoseAnimal(animal, pawn);
            ContagionAnimalDiagnosisUtility.ApplyDiagnosisCooldown(animal);
        };
        diagnose.defaultCompleteMode = ToilCompleteMode.Instant;
        yield return diagnose;
    }
}
