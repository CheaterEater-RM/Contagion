using Verse;

namespace Contagion;

public sealed class CompProperties_InfectedCorpse : CompProperties
{
    public CompProperties_InfectedCorpse()
    {
        compClass = typeof(Comp_InfectedCorpse);
    }
}

public sealed class Comp_InfectedCorpse : ThingComp
{
    private HediffDef _infectedDiseaseDef;

    private int _infectionTick = -1;

    public bool IsInfected => _infectedDiseaseDef != null;

    public HediffDef InfectedDiseaseDef => _infectedDiseaseDef;

    public void SetInfection(HediffDef diseaseDef)
    {
        if (diseaseDef == null)
        {
            return;
        }

        _infectedDiseaseDef = diseaseDef;
        _infectionTick = Find.TickManager?.TicksGame ?? -1;
        (parent as Corpse)?.InnerPawn?.Drawer?.renderer?.SetAllGraphicsDirty();
    }

    public override void PostSpawnSetup(bool respawningAfterLoad)
    {
        base.PostSpawnSetup(respawningAfterLoad);

        if (respawningAfterLoad || IsInfected || parent is not Corpse corpse)
        {
            return;
        }

        if (ContagionCorpseUtility.TryGetCorpseContagiousDiseaseFromInnerPawn(corpse.InnerPawn, out HediffDef diseaseDef))
        {
            SetInfection(diseaseDef);
        }
    }

    public override string CompInspectStringExtra()
    {
        if (!TryGetDiseaseForDisplay(out HediffDef diseaseDef))
        {
            return null;
        }

        return "Contagion_InfectedCorpseInspect".Translate(diseaseDef.LabelCap);
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Defs.Look(ref _infectedDiseaseDef, "infectedDiseaseDef");
        Scribe_Values.Look(ref _infectionTick, "infectionTick", -1);
    }

    public bool TryGetDiseaseForDisplay(out HediffDef diseaseDef)
    {
        diseaseDef = _infectedDiseaseDef;
        if (diseaseDef != null)
        {
            return true;
        }

        return ContagionCorpseUtility.TryGetCorpseContagiousDiseaseFromInnerPawn((parent as Corpse)?.InnerPawn, out diseaseDef);
    }
}
