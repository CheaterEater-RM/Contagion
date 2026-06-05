using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Contagion;

public class FloatMenuOptionProvider_InspectCorpse : FloatMenuOptionProvider
{
    protected override bool Drafted => false;

    protected override bool Undrafted => true;

    protected override bool Multiselect => false;

    protected override bool RequiresManipulation => true;

    public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing clickedThing, FloatMenuContext context)
    {
        Corpse corpse = clickedThing as Corpse;
        if (corpse == null)
        {
            yield break;
        }

        // Only flesh corpses can carry a contagious disease — skip mech/insect corpses,
        // which still receive the comp via the corpse-def injector.
        if (corpse.InnerPawn?.RaceProps?.IsFlesh != true)
        {
            yield break;
        }

        // Skip once inspected (one-shot) or once the disease is already named — a corpse
        // flagged infected-and-identified at spawn already shows its disease in the inspect
        // string, so there is nothing left to discover.
        Comp_InfectedCorpse comp = corpse.TryGetComp<Comp_InfectedCorpse>();
        if (comp != null && (comp.HasBeenInspected || comp.DiseaseIdentified))
        {
            yield break;
        }

        Pawn pawn = context.FirstSelectedPawn;

        FloatMenuOption option = FloatMenuUtility.DecoratePrioritizedTask(
            new FloatMenuOption("Contagion_InspectCorpse".Translate(), () =>
            {
                // Drive the vanilla interaction job against the corpse's Comp_CorpseInspectable.
                // The diagnosis runs in Comp_CorpseInspectable.OnInteracted. Using the vanilla
                // JobDriver_InteractThing keeps the in-flight save free of custom job/driver
                // classes, so dropping Contagion mid-inspection can't lock the pawn.
                Job job = JobMaker.MakeJob(JobDefOf.InteractThing, corpse);
                // Corpses carry exactly one CompInteractable; force the single-comp lookup path
                // (TryGetComp) rather than a possibly-stale pooled index.
                job.interactableIndex = -1;
                pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }),
            pawn,
            new LocalTargetInfo(corpse));

        yield return option;
    }
}
