using RimWorld;
using Verse;

namespace Contagion;

[DefOf]
public static class ContagionDefOf
{
    public static HediffDef Contagion_Incubation;

    public static HediffDef Contagion_TemporaryImmunity;

    public static HediffDef Contagion_TraitSeedCooldown;

    static ContagionDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(ContagionDefOf));
    }
}