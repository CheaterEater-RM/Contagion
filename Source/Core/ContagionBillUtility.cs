using RimWorld;
using Verse;

namespace Contagion;

public static class ContagionBillUtility
{
    private const string ButcherCorpseRecipeDefName = "ButcherCorpseFlesh";

    public static void ApplyButcherBillDefaults(Bill bill)
    {
        if (bill?.recipe?.defName != ButcherCorpseRecipeDefName || bill.ingredientFilter == null)
        {
            return;
        }

        if (ContagionDefOf.Contagion_AllowInfectedCorpses != null)
        {
            bill.ingredientFilter.SetAllow(ContagionDefOf.Contagion_AllowInfectedCorpses, false);
        }
    }
}
