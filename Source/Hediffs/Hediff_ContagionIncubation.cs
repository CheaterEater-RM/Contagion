using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Contagion;

public sealed class Hediff_ContagionIncubation : Hediff
{
    private const int LazyActivationCheckInterval = 250;

    private List<BodyPartDef> _partsToAffect;

    private int _activationTick = -1;

    private int _durationTicks = 1;

    private int _nextLazyCheckTick = -1;

    public HediffDef TargetDiseaseDef;

    private ContagionSeedSource _seedSource;

    public ContagionSeedSource SeedSource => _seedSource;

    public override bool Visible => false;

    public override string LabelBase => TargetDiseaseDef == null
        ? base.LabelBase
        : "Contagion_IncubationLabel".Translate(TargetDiseaseDef.label).Resolve();

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

    public void Configure(HediffDef targetDiseaseDef, List<BodyPartDef> partsToAffect, int activationTick, ContagionSeedSource seedSource = ContagionSeedSource.Unknown)
    {
        TargetDiseaseDef = targetDiseaseDef;
        _seedSource = seedSource;
        _activationTick = activationTick;
        _durationTicks = Mathf.Max(1, activationTick - Find.TickManager.TicksGame);
        _nextLazyCheckTick = Mathf.Min(activationTick, Find.TickManager.TicksGame + LazyActivationCheckInterval);
        _partsToAffect = partsToAffect.NullOrEmpty() ? null : new List<BodyPartDef>(partsToAffect);
        UpdateProgressSeverity();
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Defs.Look(ref TargetDiseaseDef, "targetDiseaseDef");
        Scribe_Collections.Look(ref _partsToAffect, "partsToAffect", LookMode.Def);
        Scribe_Values.Look(ref _activationTick, "activationTick", -1);
        Scribe_Values.Look(ref _durationTicks, "durationTicks", 1);
        Scribe_Values.Look(ref _nextLazyCheckTick, "nextLazyCheckTick", -1);
        Scribe_Values.Look(ref _seedSource, "seedSource", ContagionSeedSource.Unknown);
    }

    public override void PostTickInterval(int delta)
    {
        if (!ShouldRunLazyCheck())
        {
            return;
        }

        UpdateProgressSeverity();

        if (!ReadyToActivate)
        {
            return;
        }

        ContagionDiseaseUtility.TryActivateIncubatedDisease(this);
    }

    private bool ShouldRunLazyCheck()
    {
        if (_activationTick < 0)
        {
            return false;
        }

        int currentTick = Find.TickManager.TicksGame;
        if (_nextLazyCheckTick < 0)
        {
            _nextLazyCheckTick = Mathf.Min(_activationTick, currentTick + LazyActivationCheckInterval);
        }

        if (currentTick < _nextLazyCheckTick)
        {
            return false;
        }

        // Hediffs may tick in intervals, so activation can land between calls; the next interval
        // catches up immediately once currentTick has passed the activation tick.
        _nextLazyCheckTick = Mathf.Min(_activationTick, currentTick + LazyActivationCheckInterval);
        return true;
    }

    private void UpdateProgressSeverity()
    {
        if (_activationTick < 0)
        {
            return;
        }

        int ticksRemaining = Mathf.Max(0, _activationTick - Find.TickManager.TicksGame);
        float progress = 1f - ((float)ticksRemaining / Mathf.Max(1, _durationTicks));
        Severity = Mathf.Clamp(progress, 0.01f, 1f);
    }
}
