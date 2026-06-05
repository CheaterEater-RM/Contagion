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

    // True when the inner pawn died showing Contagion_AnimalSick but with no confirmed
    // contagious disease. The corpse is treated as potentially infected for filtering and
    // rendering, but post-mortem inspection will clear it as a false positive.
    private bool _suspectedInfected;

    public bool IsInfected => _infectedDiseaseDef != null;

    // Suspected-infected: sick signal was present at death, but no disease has been confirmed.
    // Cleared to false when post-mortem inspection reports no disease.
    public bool IsSuspectedInfected => _suspectedInfected && _infectedDiseaseDef == null;

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
        _suspectedInfected = false;
        _hasBeenInspected = true;
        (parent as Corpse)?.InnerPawn?.Drawer?.renderer?.SetAllGraphicsDirty();
    }

    // Marks the corpse as suspected infected: the animal was showing the sick signal at
    // death but carried no confirmed contagious disease. Triggers the infected-corpse
    // appearance and filters. Clears when post-mortem inspection reports no disease.
    internal void SetSuspectedInfection()
    {
        _suspectedInfected = true;
        (parent as Corpse)?.InnerPawn?.Drawer?.renderer?.SetAllGraphicsDirty();
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

        // Once a corpse has been inspected, the comp state is authoritative — never re-derive from
        // the inner pawn. Re-derivation runs on every spawn, and hauling despawns/respawns the
        // corpse, so without this guard a cleared corpse would be re-flagged suspected-infected from
        // a lingering inner-pawn sick signal or hidden disease (TryInspectCorpse also strips the
        // sick signal, but this keeps the inspection result sticky against any residual state).
        if (respawningAfterLoad || IsInfected || HasBeenInspected || parent is not Corpse corpse)
        {
            return;
        }

        if (ContagionCorpseUtility.TryGetCorpseContagiousDiseaseFromInnerPawn(corpse.InnerPawn, out HediffDef diseaseDef))
        {
            SetInfection(diseaseDef);
            EnsureCorpseTrace(corpse);
            return;
        }

        ContagionCorpseUtility.TryApplyPosthumousPresentation(corpse.InnerPawn, this);
        EnsureCorpseTrace(corpse);
    }

    // Keep the developer trace graph alive across the pawn→corpse transition. Gate on the
    // transmission-facing detector (includes hidden/undiagnosed disease) rather than the display
    // flags, so a dev-killed animal carrying a hidden disease (e.g. gut worms) still traces.
    // First re-anchor the dead pawn's existing nodes onto the corpse to preserve upstream lineage;
    // if there were none, create a fresh corpse node so the corpse still shows as a traced danger.
    private void EnsureCorpseTrace(Corpse corpse)
    {
        if (corpse?.InnerPawn == null
            || !ContagionCorpseUtility.TryGetCorpseInfectionForTransmission(corpse, out HediffDef traceDisease))
        {
            return;
        }

        int moved = ContagionTrace.ReanchorPawnToCorpse(corpse.InnerPawn, corpse);
        if (moved == 0)
        {
            ContagionTrace.EnsureNode(corpse, traceDisease);
        }
    }

    public override string CompInspectStringExtra()
    {
        if (_infectedDiseaseDef != null)
        {
            return _diseaseIdentified
                ? "Contagion_InfectedCorpseInspect".Translate(_infectedDiseaseDef.LabelCap)
                : "Contagion_InfectedCorpseInspectUnknown".Translate();
        }

        if (_suspectedInfected)
        {
            return "Contagion_InfectedCorpseInspectUnknown".Translate();
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
        Scribe_Values.Look(ref _suspectedInfected, "suspectedInfected");
    }

    public bool TryGetDiseaseForDisplay(out HediffDef diseaseDef)
    {
        // Display is driven purely by comp state. The disease is set on the comp at spawn
        // (visible-before-death) or after a post-mortem inspection — never read straight off
        // the inner pawn, which would leak hidden/undiagnosed diseases with no roll.
        diseaseDef = _infectedDiseaseDef;
        return diseaseDef != null;
    }
}
