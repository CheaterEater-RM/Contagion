using RimWorld;
using Verse;

namespace Contagion;

[DefOf]
public static class ContagionDefOf
{
    public static HediffDef Contagion_Incubation;

    public static HediffDef Contagion_TemporaryImmunity;

    public static HediffDef Contagion_TraitSeedCooldown;

    public static HediffDef Contagion_CorpseFleas;

    public static HediffDef Contagion_AnimalSick;

    public static SpecialThingFilterDef AllowInfectedCorpses;

    public static SpecialThingFilterDef AllowUninfectedCorpses;

    static ContagionDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(ContagionDefOf));
    }
}
