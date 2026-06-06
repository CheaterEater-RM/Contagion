using RimWorld;
using Verse;

namespace Contagion;

[DefOf]
public static class ContagionDefOf
{
    public static HediffDef Contagion_Incubation;

    public static HediffDef Contagion_TemporaryImmunity;

    public static HediffDef Contagion_TraitSeedCooldown;

    public static HediffDef Contagion_AnimalDiagnosisCooldown;

    public static HediffDef Contagion_CorpseFleas;

    public static HediffDef Contagion_AnimalSick;

    public static HediffDef Contagion_PendingExam;

    public static SpecialThingFilterDef Contagion_AllowInfectedCorpses;

    public static SpecialThingFilterDef Contagion_AllowUninfectedCorpses;

    static ContagionDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(ContagionDefOf));
    }
}
