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

    public bool IsEnvironmentalWindow => infectionBudget > 0;

    public bool HasRemainingBudget => !IsEnvironmentalWindow || infectionsApplied < infectionBudget;

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
    }
}
