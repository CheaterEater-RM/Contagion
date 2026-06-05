using Verse;

namespace Contagion;

internal static class ContagionTransmissionStaggerUtility
{
    public static bool IsDueThisTick(Thing thing, Map map, int intervalTicks, int bucket)
    {
        if (thing == null || map == null || intervalTicks <= 1)
        {
            return true;
        }

        return GetBucket(thing, map, intervalTicks) == bucket;
    }

    private static int GetBucket(Thing thing, Map map, int intervalTicks)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + map.uniqueID;
            hash = hash * 31 + thing.thingIDNumber;
            return (hash & int.MaxValue) % intervalTicks;
        }
    }
}
