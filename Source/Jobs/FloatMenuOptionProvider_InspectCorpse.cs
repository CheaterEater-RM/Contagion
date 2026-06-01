using System.Collections.Generic;
using System.Linq;
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

        // Find the nearest reachable butchery table (a worktable that can butcher corpses).
        RecipeDef butcherRecipe = DefDatabase<RecipeDef>.GetNamedSilentFail("ButcherCorpseFlesh");
        Building_WorkTable table = (Building_WorkTable)GenClosest.ClosestThingReachable(
            pawn.Position,
            pawn.Map,
            ThingRequest.ForGroup(ThingRequestGroup.PotentialBillGiver),
            PathEndMode.InteractionCell,
            TraverseParms.For(pawn),
            validator: t => t is Building_WorkTable wt
                && butcherRecipe != null
                && wt.def.AllRecipes != null
                && wt.def.AllRecipes.Contains(butcherRecipe)
                && !t.IsForbidden(pawn)
                && pawn.CanReserve(t));

        if (table == null)
        {
            yield return new FloatMenuOption(
                "Contagion_InspectCorpseNoTable".Translate(),
                null)
            {
                Disabled = true
            };
            yield break;
        }

        FloatMenuOption option = FloatMenuUtility.DecoratePrioritizedTask(
            new FloatMenuOption("Contagion_InspectCorpse".Translate(), () =>
            {
                Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Contagion_InspectCorpse"), table, corpse);
                pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }),
            pawn,
            new LocalTargetInfo(corpse));

        yield return option;
    }
}