using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Contagion;

public sealed class Hediff_ContagionIncubation : Hediff
{
    private List<BodyPartDef> _partsToAffect;

    private int _activationTick = -1;

    public HediffDef TargetDiseaseDef;

    public override bool Visible => false;

    public override bool ShouldRemove
    {
        get
        {
            if (TargetDiseaseDef == null || _activationTick < 0)
            {
                return true;
            }

            return base.ShouldRemove;
        }
    }

    public int ActivationTick => _activationTick;

    public List<BodyPartDef> PartsToAffect => _partsToAffect;

    public bool ReadyToActivate => _activationTick >= 0 && Find.TickManager.TicksGame >= _activationTick;

    public void Configure(HediffDef targetDiseaseDef, List<BodyPartDef> partsToAffect, int activationTick)
    {
        TargetDiseaseDef = targetDiseaseDef;
        _activationTick = activationTick;
        _partsToAffect = partsToAffect.NullOrEmpty() ? null : new List<BodyPartDef>(partsToAffect);
        Severity = Mathf.Max(Severity, 1f);
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Defs.Look(ref TargetDiseaseDef, "targetDiseaseDef");
        Scribe_Collections.Look(ref _partsToAffect, "partsToAffect", LookMode.Def);
        Scribe_Values.Look(ref _activationTick, "activationTick", -1);
    }

    public override void TickInterval(int delta)
    {
        base.TickInterval(delta);

        if (!ReadyToActivate)
        {
            return;
        }

        ContagionDiseaseUtility.TryActivateIncubatedDisease(this);
    }
}