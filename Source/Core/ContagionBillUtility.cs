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

        if (ContagionDefOf.AllowInfectedCorpses != null)
        {
            bill.ingredientFilter.SetAllow(ContagionDefOf.AllowInfectedCorpses, false);
        }
    }

    public static int DisallowInfectedCorpsesOnButcherBills()
    {
        int changed = 0;
        foreach (IBillGiver billGiver in BillUtility.GlobalBillGivers())
        {
            BillStack billStack = billGiver?.BillStack;
            if (billStack == null)
            {
                continue;
            }

            foreach (Bill bill in billStack.Bills)
            {
                if (bill?.recipe?.defName != ButcherCorpseRecipeDefName || bill.ingredientFilter == null)
                {
                    continue;
                }

                bool wasAllowed = ContagionDefOf.AllowInfectedCorpses != null
                    && bill.ingredientFilter.Allows(ContagionDefOf.AllowInfectedCorpses);
                ApplyButcherBillDefaults(bill);
                if (wasAllowed)
                {
                    changed++;
                }
            }
        }

        return changed;
    }
}
