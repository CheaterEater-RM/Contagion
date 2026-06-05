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

            if (!HasComp<Comp_InfectedCorpse>(thingDef.comps))
            {
                thingDef.comps.Add(new CompProperties_InfectedCorpse());
            }

            // Adds the vanilla-interaction inspection surface (Comp_CorpseInspectable). Driven by
            // FloatMenuOptionProvider_InspectCorpse via JobDefOf.InteractThing.
            if (!HasComp<Comp_CorpseInspectable>(thingDef.comps))
            {
                thingDef.comps.Add(new CompProperties_CorpseInspectable());
            }
        }
    }

    private static bool HasComp<TComp>(List<CompProperties> comps) where TComp : ThingComp
    {
        for (int i = 0; i < comps.Count; i++)
        {
            if (comps[i]?.compClass == typeof(TComp))
            {
                return true;
            }
        }

        return false;
    }
}
