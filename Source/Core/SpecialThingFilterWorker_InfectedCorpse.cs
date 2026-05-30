using RimWorld;
using Verse;

namespace Contagion;

public sealed class SpecialThingFilterWorker_InfectedCorpse : SpecialThingFilterWorker
{
    public override bool Matches(Thing t)
    {
        return ContagionCorpseUtility.IsInfectedCorpse(t);
    }

    public override bool CanEverMatch(ThingDef def)
    {
        return def?.ingestible != null && (def.ingestible.foodType & FoodTypeFlags.Corpse) != 0;
    }
}

public sealed class SpecialThingFilterWorker_UninfectedCorpse : SpecialThingFilterWorker
{
    public override bool Matches(Thing t)
    {
        return ContagionCorpseUtility.IsUninfectedCorpse(t);
    }

    public override bool CanEverMatch(ThingDef def)
    {
        return def?.ingestible != null && (def.ingestible.foodType & FoodTypeFlags.Corpse) != 0;
    }
}
