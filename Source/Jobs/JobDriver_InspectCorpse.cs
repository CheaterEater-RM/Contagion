using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Contagion;

public class JobDriver_InspectCorpse : JobDriver
{
    private const int InspectDurationTicks = 400; // butcher bills are 300

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
        // NOTE: no global FailOnDespawned for the corpse (TargetIndex.B) — the driver deliberately
        // picks the corpse up and carries it to the table, and a carried thing is despawned. A
        // global despawn check would fail the job the instant after pickup. The goto toil below
        // has its own scoped FailOnDespawnedNullOrForbidden, which only applies before pickup.
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
            ContagionDiagnostics.Trace($"Corpse inspection: {pawn.LabelShortCap} performing diagnosis on {Corpse?.InnerPawn?.LabelShortCap ?? Corpse?.Label}.");
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