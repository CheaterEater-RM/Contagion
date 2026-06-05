using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Contagion;

internal static class ContagionCorpseFilterSaveUtility
{
    private const string AllowInfectedCorpsesDefName = "AllowInfectedCorpses";
    private const string AllowUninfectedCorpsesDefName = "AllowUninfectedCorpses";

    public static bool IsContagionCorpseFilter(SpecialThingFilterDef def)
    {
        return def?.defName is AllowInfectedCorpsesDefName or AllowUninfectedCorpsesDefName;
    }

    public static List<string> GetContagionCorpseFilterDefNames(List<SpecialThingFilterDef> filters)
    {
        if (filters == null || filters.Count == 0)
        {
            return null;
        }

        List<string> defNames = null;
        for (int i = 0; i < filters.Count; i++)
        {
            SpecialThingFilterDef filter = filters[i];
            if (!IsContagionCorpseFilter(filter))
            {
                continue;
            }

            defNames ??= new List<string>();
            if (!defNames.Contains(filter.defName))
            {
                defNames.Add(filter.defName);
            }
        }

        return defNames;
    }

    public static bool TryResolveContagionCorpseFilter(string defName, out SpecialThingFilterDef def)
    {
        def = defName switch
        {
            AllowInfectedCorpsesDefName => ContagionDefOf.AllowInfectedCorpses,
            AllowUninfectedCorpsesDefName => ContagionDefOf.AllowUninfectedCorpses,
            _ => null
        };

        return def != null && IsContagionCorpseFilter(def);
    }
}
