using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Contagion;

// Right-click "Examine for illness" option on domestic animals.
//
// Registered automatically: FloatMenuMakerMap.Init() discovers all FloatMenuOptionProvider
// subclasses via reflection — no XML registration needed.
//
// Proactive screening flags the animal with the tendable Contagion_PendingExam hediff; a vet then
// examines it through the vanilla tend path (JobDriver_TendPatient), which fires the diagnosis in
// HediffComp_PendingExamDiagnosis. There is no custom job, so an in-flight exam saved with this mod
// survives Contagion's removal.
//
// Shows for: any living, spawned domestic animal without a diagnosis cooldown that is not already
// flagged and not already visibly sick. A visibly-sick animal (Contagion_AnimalSick) is left to the
// vanilla tend/diagnosis path, so screening defers and the option is hidden.
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

        // Already visibly sick → the vanilla Tend Animal workflow diagnoses it; don't duplicate.
        if (ContagionAnimalDiagnosisUtility.HasVisibleSickSignal(clickedPawn))
        {
            yield break;
        }

        // Already flagged → the marker is visible on the animal; nothing to add.
        if (clickedPawn.health?.hediffSet?.HasHediff(ContagionDefOf.Contagion_PendingExam) == true)
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

        Pawn animal = clickedPawn;

        // Flag the animal for examination. A vet tends it through the vanilla tend path when it is
        // resting; no colonist job is reserved here, so this is a plain (non-prioritized) option.
        yield return new FloatMenuOption("Contagion_ExamineAnimal".Translate(), () =>
        {
            if (animal.health == null
                || animal.health.hediffSet.HasHediff(ContagionDefOf.Contagion_PendingExam)
                || ContagionAnimalDiagnosisUtility.HasVisibleSickSignal(animal))
            {
                return;
            }

            animal.health.AddHediff(HediffMaker.MakeHediff(ContagionDefOf.Contagion_PendingExam, animal));

            // Tending needs the animal lying down (WorkGiver_Tend.GoodLayingStatusForTend), so the
            // exam happens when it next rests rather than immediately. Tell the player so the delay
            // isn't mistaken for the order being lost; the flag also auto-clears after a day.
            Messages.Message(
                "Contagion_ExamineAnimalFlagged".Translate(animal.LabelShortCap),
                animal,
                MessageTypeDefOf.NeutralEvent,
                historical: false);
        });
    }
}
