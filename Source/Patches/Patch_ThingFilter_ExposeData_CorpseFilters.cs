using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Verse;

namespace Contagion.Patches;

[HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.ExposeData))]
internal static class Patch_ThingFilter_ExposeData_CorpseFilters
{
    private const string ContagionDisallowedCorpseFiltersNode = "contagionDisallowedCorpseFilters";

    private static readonly AccessTools.FieldRef<ThingFilter, List<SpecialThingFilterDef>> DisallowedSpecialFiltersRef =
        AccessTools.FieldRefAccess<ThingFilter, List<SpecialThingFilterDef>>("disallowedSpecialFilters");

    private static readonly Dictionary<ThingFilter, List<SpecialThingFilterDef>> SavedDisallowedSpecialFilters = new();

    public static void Prefix(ThingFilter __instance)
    {
        if (Scribe.mode != LoadSaveMode.Saving)
        {
            return;
        }

        List<SpecialThingFilterDef> disallowedSpecialFilters = DisallowedSpecialFiltersRef(__instance);
        if (disallowedSpecialFilters == null || disallowedSpecialFilters.Count == 0)
        {
            return;
        }

        disallowedSpecialFilters.RemoveAll(filterDef => filterDef == null);

        List<SpecialThingFilterDef> strippedFilters = disallowedSpecialFilters
            .Where(filterDef => !ContagionCorpseFilterSaveUtility.IsContagionCorpseFilter(filterDef))
            .ToList();

        if (strippedFilters.Count == disallowedSpecialFilters.Count)
        {
            return;
        }

        SavedDisallowedSpecialFilters[__instance] = disallowedSpecialFilters;
        DisallowedSpecialFiltersRef(__instance) = strippedFilters;
    }

    public static void Postfix(ThingFilter __instance)
    {
        List<SpecialThingFilterDef> disallowedSpecialFilters = DisallowedSpecialFiltersRef(__instance)
            ?? new List<SpecialThingFilterDef>();
        bool removedNulls = disallowedSpecialFilters.RemoveAll(filterDef => filterDef == null) > 0;
        List<string> disallowedCorpseFilterDefNames = null;

        if (Scribe.mode == LoadSaveMode.Saving)
        {
            disallowedCorpseFilterDefNames = SavedDisallowedSpecialFilters.TryGetValue(
                __instance,
                out List<SpecialThingFilterDef> savedFilters)
                ? ContagionCorpseFilterSaveUtility.GetContagionCorpseFilterDefNames(savedFilters)
                : ContagionCorpseFilterSaveUtility.GetContagionCorpseFilterDefNames(disallowedSpecialFilters);
        }

        Scribe_Collections.Look(
            ref disallowedCorpseFilterDefNames,
            ContagionDisallowedCorpseFiltersNode,
            LookMode.Value);

        if (Scribe.mode == LoadSaveMode.LoadingVars && disallowedCorpseFilterDefNames != null)
        {
            for (int i = 0; i < disallowedCorpseFilterDefNames.Count; i++)
            {
                if (!ContagionCorpseFilterSaveUtility.TryResolveContagionCorpseFilter(
                        disallowedCorpseFilterDefNames[i],
                        out SpecialThingFilterDef filterDef)
                    || disallowedSpecialFilters.Contains(filterDef))
                {
                    continue;
                }

                disallowedSpecialFilters.Add(filterDef);
            }

            DisallowedSpecialFiltersRef(__instance) = disallowedSpecialFilters;
        }
        else if (removedNulls)
        {
            DisallowedSpecialFiltersRef(__instance) = disallowedSpecialFilters;
        }

        if (Scribe.mode == LoadSaveMode.Saving
            && SavedDisallowedSpecialFilters.TryGetValue(__instance, out List<SpecialThingFilterDef> originalFilters))
        {
            DisallowedSpecialFiltersRef(__instance) = originalFilters;
            SavedDisallowedSpecialFilters.Remove(__instance);
        }
    }
}
