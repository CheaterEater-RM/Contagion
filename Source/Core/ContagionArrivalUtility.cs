using System.Collections.Generic;
using Verse;

namespace Contagion;

public enum ContagionArrivalGroupKind
{
    Neutral,
    WandererJoin,
    QuestGuest,
    QuestJoiner,
    HostileRaid,
    TribalRaid,
    FarmAnimals
}

public static class ContagionArrivalUtility
{
    public static int SeedArrivals(IEnumerable<Pawn> pawns)
    {
        return SeedArrivalGroup(pawns, ContagionArrivalGroupKind.Neutral);
    }

    public static int SeedArrivalGroup(IEnumerable<Pawn> pawns, ContagionArrivalGroupKind groupKind)
    {
        return ContagionSeedingCoordinator.HandleArrivalGroup(pawns, groupKind);
    }
}
