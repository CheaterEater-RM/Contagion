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

    private bool _diseaseIdentified;

    private bool _hasBeenInspected;

    public bool IsInfected => _infectedDiseaseDef != null;

    public HediffDef InfectedDiseaseDef => _infectedDiseaseDef;

    internal bool DiseaseIdentified => _diseaseIdentified;

    internal bool HasBeenInspected => _hasBeenInspected;

    internal void MarkIdentified()
    {
        _diseaseIdentified = true;
        _hasBeenInspected = true;
    }

    internal void MarkInspectedClean()
    {
        _infectedDiseaseDef = null;
        _diseaseIdentified = false;
        _hasBeenInspected = true;
    }

    internal void MarkInspectedFailed()
    {
        _hasBeenInspected = true;
    }

    public void SetInfection(HediffDef diseaseDef, bool identified = true)
    {
        if (diseaseDef == null)
        {
            return;
        }

        _infectedDiseaseDef = diseaseDef;
        _diseaseIdentified = identified;
        _infectionTick = Find.TickManager?.TicksGame ?? -1;
        if (parent is Corpse corpse && DiseaseProfileCache.TryGetResolvedProfile(diseaseDef, out ResolvedTransmissionProfile resolvedProfile))
        {
            ContagionCorpseExposureUtility.EnsureCorpseFleas(corpse, resolvedProfile);
        }

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
            return;
        }

        ContagionCorpseUtility.TryApplyPosthumousPresentation(corpse.InnerPawn, this);
    }

    public override string CompInspectStringExtra()
    {
        if (_infectedDiseaseDef != null)
        {
            return _diseaseIdentified
                ? "Contagion_InfectedCorpseInspect".Translate(_infectedDiseaseDef.LabelCap)
                : "Contagion_InfectedCorpseInspectUnknown".Translate();
        }

        if (ContagionCorpseUtility.TryGetCorpseContagiousDiseaseFromInnerPawn((parent as Corpse)?.InnerPawn, out HediffDef diseaseDef))
        {
            return "Contagion_InfectedCorpseInspect".Translate(diseaseDef.LabelCap);
        }

        return null;
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Defs.Look(ref _infectedDiseaseDef, "infectedDiseaseDef");
        Scribe_Values.Look(ref _infectionTick, "infectionTick", -1);
        Scribe_Values.Look(ref _diseaseIdentified, "diseaseIdentified", defaultValue: true);
        Scribe_Values.Look(ref _hasBeenInspected, "hasBeenInspected");
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
