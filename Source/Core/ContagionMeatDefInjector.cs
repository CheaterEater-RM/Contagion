using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Contagion;

// Injects Comp_ContaminatedFood onto all food defs that can carry contamination: raw meat,
// prepared meals, and kibble.
//
// This is done in C# rather than XML because XPath patching cannot reliably reach these defs:
//   - Raw meat defs are generated at runtime by ThingDefGenerator_Meat.ImpliedMeatDefs and don't
//     exist in the XML document at patch time at all.
//   - Meals/kibble exist in XML, but an XPath patch only lands on defs that inherit from the
//     specific vanilla bases (MealBaseIngredientless) or match a hardcoded defName (Kibble), so
//     modded food defined from a different base or as a standalone def slips through.
//
// Detecting by ingestible.foodType (the same flags vanilla cooking/eating logic uses) catches
// vanilla and modded food uniformly. Mirrors ContagionCorpseDefInjector. Runs from the
// [StaticConstructorOnStartup] init, after defs resolve; CompProperties_ContaminatedFood has no
// cross-references, so the missed ResolveReferences pass is harmless.
public static class ContagionMeatDefInjector
{
    // Food kinds that can carry foodborne contamination. Meat is the raw source; meals and kibble
    // inherit it through cooking. Other foodTypes (plants, corpses, tree, etc.) are out of scope.
    private const FoodTypeFlags ContaminableFoodTypes =
        FoodTypeFlags.Meat | FoodTypeFlags.Meal | FoodTypeFlags.Kibble;

    public static void EnsureFoodComps()
    {
        int injected = 0;
        foreach (ThingDef thingDef in DefDatabase<ThingDef>.AllDefsListForReading)
        {
            if (thingDef.ingestible == null
                || (thingDef.ingestible.foodType & ContaminableFoodTypes) == 0)
            {
                continue;
            }

            // The comp lives on a ThingWithComps; skip anything that can't host comps.
            if (!typeof(ThingWithComps).IsAssignableFrom(thingDef.thingClass))
            {
                continue;
            }

            thingDef.comps ??= new List<CompProperties>();
            if (thingDef.comps.Any(comp => comp is CompProperties_ContaminatedFood || comp.compClass == typeof(Comp_ContaminatedFood)))
            {
                continue;
            }

            thingDef.comps.Add(new CompProperties_ContaminatedFood());
            injected++;
        }

        ContagionDiagnostics.Trace($"Food comp injection: added Comp_ContaminatedFood to {injected} food def(s) (meat/meals/kibble).");
    }
}
