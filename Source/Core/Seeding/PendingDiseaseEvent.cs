using RimWorld;
using Verse;

namespace Contagion;

public sealed class PendingDiseaseEvent : IExposable
{
    public HediffDef diseaseDef;

    public int firedTick;

    public int expiryTick;

    public int infectionBudget;

    public int infectionsApplied;

    public int colonyHumanInfectionBudget;

    public int colonyHumanInfectionsApplied;

    public int colonyAnimalInfectionBudget;

    public int colonyAnimalInfectionsApplied;

    public int wildAnimalInfectionBudget;

    public int wildAnimalInfectionsApplied;

    public bool IsEnvironmentalWindow => infectionBudget > 0
        || colonyHumanInfectionBudget > 0
        || colonyAnimalInfectionBudget > 0
        || wildAnimalInfectionBudget > 0;

    public bool HasTrackBudgets => colonyHumanInfectionBudget > 0
        || colonyAnimalInfectionBudget > 0
        || wildAnimalInfectionBudget > 0;

    public bool HasRemainingBudget => !IsEnvironmentalWindow
        || (HasTrackBudgets
            ? HasAnyRemainingTrackBudget
            : infectionsApplied < infectionBudget);

    private bool HasAnyRemainingTrackBudget =>
        colonyHumanInfectionsApplied < colonyHumanInfectionBudget
        || colonyAnimalInfectionsApplied < colonyAnimalInfectionBudget
        || wildAnimalInfectionsApplied < wildAnimalInfectionBudget;

    public bool HasRemainingBudgetFor(ContagionCaseTrack track)
    {
        if (!IsEnvironmentalWindow)
        {
            return true;
        }

        if (!HasTrackBudgets)
        {
            return infectionsApplied < infectionBudget;
        }

        return track switch
        {
            ContagionCaseTrack.Human => colonyHumanInfectionsApplied < colonyHumanInfectionBudget,
            ContagionCaseTrack.Animal => colonyAnimalInfectionsApplied < colonyAnimalInfectionBudget,
            ContagionCaseTrack.WildAnimal => wildAnimalInfectionsApplied < wildAnimalInfectionBudget,
            _ => false
        };
    }

    public void NotifyInfectionApplied(ContagionCaseTrack track)
    {
        if (!HasTrackBudgets)
        {
            infectionsApplied++;
            return;
        }

        switch (track)
        {
            case ContagionCaseTrack.Human:
                colonyHumanInfectionsApplied++;
                break;
            case ContagionCaseTrack.Animal:
                colonyAnimalInfectionsApplied++;
                break;
            case ContagionCaseTrack.WildAnimal:
                wildAnimalInfectionsApplied++;
                break;
        }
    }

    public bool IsExpired(int currentTick)
    {
        return currentTick >= expiryTick;
    }

    public void ExposeData()
    {
        Scribe_Defs.Look(ref diseaseDef, "diseaseDef");
        Scribe_Values.Look(ref firedTick, "firedTick");
        Scribe_Values.Look(ref expiryTick, "expiryTick");
        Scribe_Values.Look(ref infectionBudget, "infectionBudget");
        Scribe_Values.Look(ref infectionsApplied, "infectionsApplied");
        Scribe_Values.Look(ref colonyHumanInfectionBudget, "colonyHumanInfectionBudget");
        Scribe_Values.Look(ref colonyHumanInfectionsApplied, "colonyHumanInfectionsApplied");
        Scribe_Values.Look(ref colonyAnimalInfectionBudget, "colonyAnimalInfectionBudget");
        Scribe_Values.Look(ref colonyAnimalInfectionsApplied, "colonyAnimalInfectionsApplied");
        Scribe_Values.Look(ref wildAnimalInfectionBudget, "wildAnimalInfectionBudget");
        Scribe_Values.Look(ref wildAnimalInfectionsApplied, "wildAnimalInfectionsApplied");
    }
}
