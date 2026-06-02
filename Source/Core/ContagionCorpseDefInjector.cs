using System.Collections.Generic;
using Verse;

namespace Contagion;

public static class ContagionCorpseDefInjector
{
    public static void EnsureCorpseComps()
    {
        foreach (ThingDef thingDef in DefDatabase<ThingDef>.AllDefsListForReading)
        {
            if (thingDef.thingClass != typeof(Corpse))
            {
                continue;
            }

            thingDef.comps ??= new List<CompProperties>();
            if (HasInfectedCorpseComp(thingDef.comps))
            {
                continue;
            }

            thingDef.comps.Add(new CompProperties_InfectedCorpse());
        }
    }

    private static bool HasInfectedCorpseComp(List<CompProperties> comps)
    {
        for (int i = 0; i < comps.Count; i++)
        {
            CompProperties comp = comps[i];
            if (comp is CompProperties_InfectedCorpse || comp?.compClass == typeof(Comp_InfectedCorpse))
            {
                return true;
            }
        }

        return false;
    }
}
