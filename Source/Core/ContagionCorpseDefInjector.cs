using System.Linq;
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

            thingDef.comps ??= new System.Collections.Generic.List<CompProperties>();
            if (thingDef.comps.Any(comp => comp is CompProperties_InfectedCorpse || comp.compClass == typeof(Comp_InfectedCorpse)))
            {
                continue;
            }

            thingDef.comps.Add(new CompProperties_InfectedCorpse());
        }
    }
}
