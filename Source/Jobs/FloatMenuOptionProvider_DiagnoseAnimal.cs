using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Contagion;

// Right-click "Examine for illness" option on domestic animals.
//
// Registered automatically: FloatMenuMakerMap.Init() discovers all FloatMenuOptionProvider
// subclasses via reflection — no XML registration needed.
//
// Shows for: any living, spawned domestic animal without a diagnosis cooldown.
// Disabled with reason when a cooldown is active (player knows the mechanic exists).
// Wild animals are excluded — colonists cannot safely approach them for an exam.
public class FloatMenuOptionProvider_DiagnoseAnimal : FloatMenuOptionProvider
{
    protected override bool Drafted => false;

    protected override bool Undrafted => true;

    protected override bool Multiselect => false;

    protected override bool RequiresManipulation => true;

    public override IEnumerable<FloatMenuOption> GetOptionsFor(Pawn clickedPawn, FloatMenuContext context)
    {
        // Only domestic animals that are alive and on the map.
        if (clickedPawn.Dead
            || !clickedPawn.Spawned
            || clickedPawn.RaceProps?.Animal != true
            || clickedPawn.Faction != Faction.OfPlayer)
        {
            yield break;
        }

        // Show a disabled option so the player knows the mechanic exists and can anticipate reset.
        if (ContagionAnimalDiagnosisUtility.HasDiagnosisCooldown(clickedPawn))
        {
            yield return new FloatMenuOption("Contagion_ExamineAnimalCooldown".Translate(), null)
            {
                Disabled = true
            };
            yield break;
        }

        Pawn selectedPawn = context.FirstSelectedPawn;

        yield return FloatMenuUtility.DecoratePrioritizedTask(
            new FloatMenuOption("Contagion_ExamineAnimal".Translate(), () =>
            {
                Job job = JobMaker.MakeJob(
                    DefDatabase<JobDef>.GetNamed("Contagion_DiagnoseAnimal"),
                    clickedPawn);
                selectedPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }),
            selectedPawn,
            new LocalTargetInfo(clickedPawn));
    }
}
