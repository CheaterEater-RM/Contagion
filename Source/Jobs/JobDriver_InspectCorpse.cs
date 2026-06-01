using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Contagion;

public class JobDriver_InspectCorpse : JobDriver
{
    private const int InspectDurationTicks = 200;

    private Corpse Corpse => (Corpse)job.GetTarget(TargetIndex.B).Thing;

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        return pawn.Reserve(job.targetA, job, errorOnFailed: errorOnFailed)
            && pawn.Reserve(job.targetB, job, errorOnFailed: errorOnFailed);
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDestroyedOrNull(TargetIndex.A);
        this.FailOnDestroyedOrNull(TargetIndex.B);
        this.FailOnDespawnedOrNull(TargetIndex.B);
        this.FailOn(() => Corpse.TryGetComp<Comp_InfectedCorpse>()?.HasBeenInspected == true);

        // Go to the corpse.
        yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch)
            .FailOnDespawnedNullOrForbidden(TargetIndex.B);

        // Pick up the corpse.
        yield return Toils_Haul.StartCarryThing(TargetIndex.B);

        // Carry it to the butchery table.
        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell)
            .FailOnDespawnedNullOrForbidden(TargetIndex.A);

        // Inspect.
        Toil inspect = Toils_General.Wait(InspectDurationTicks, TargetIndex.A);
        inspect.WithProgressBarToilDelay(TargetIndex.A);
        inspect.PlaySustainerOrSound(() => SoundDefOf.Recipe_Surgery);
        yield return inspect;

        // Perform the diagnosis.
        Toil diagnose = ToilMaker.MakeToil("ContagionInspectCorpse_Diagnose");
        diagnose.initAction = () =>
        {
            ContagionCorpseUtility.TryInspectCorpse(Corpse, pawn);
        };
        diagnose.defaultCompleteMode = ToilCompleteMode.Instant;
        yield return diagnose;

        // Drop the corpse at the table.
        Toil drop = ToilMaker.MakeToil("ContagionInspectCorpse_Drop");
        drop.initAction = () =>
        {
            if (pawn.carryTracker.CarriedThing != null)
            {
                pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Direct, out _);
            }
        };
        drop.defaultCompleteMode = ToilCompleteMode.Instant;
        yield return drop;
    }
}