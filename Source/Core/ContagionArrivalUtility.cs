using System.Collections.Generic;
using Verse;

namespace Contagion;

public static class ContagionArrivalUtility
{
    public static int SeedArrivals(IEnumerable<Pawn> pawns)
    {
        return ContagionSeedingCoordinator.HandleArrivals(pawns);
    }
}
