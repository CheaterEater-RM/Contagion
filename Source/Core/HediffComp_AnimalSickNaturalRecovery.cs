using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

public sealed class HediffCompProperties_AnimalSickNaturalRecovery : HediffCompProperties
{
    public float firstDayChance = 0.20f;

    public float chanceIncreasePerDay = 0.10f;

    public int maxDays = 5;

    public HediffCompProperties_AnimalSickNaturalRecovery()
    {
        compClass = typeof(HediffComp_AnimalSickNaturalRecovery);
    }
}

public sealed class HediffComp_AnimalSickNaturalRecovery : HediffComp
{
    private const int TicksPerDay = 60000;

    private int _nextRecoveryRollTick = -1;

    private int _daysElapsed;

    private HediffCompProperties_AnimalSickNaturalRecovery Props =>
        (HediffCompProperties_AnimalSickNaturalRecovery)props;

    public override void CompPostMake()
    {
        base.CompPostMake();
        ScheduleNextRoll();
    }

    public override void CompPostTickInterval(ref float severityAdjustment, int delta)
    {
        Pawn pawn = Pawn;
        if (pawn == null || pawn.Dead || pawn.RaceProps?.Animal != true)
        {
            return;
        }

        int currentTick = Find.TickManager.TicksGame;
        if (_nextRecoveryRollTick < 0)
        {
            ScheduleNextRoll();
            return;
        }

        if (currentTick < _nextRecoveryRollTick)
        {
            return;
        }

        _daysElapsed++;
        int maxDays = Mathf.Max(1, Props.maxDays);
        if (_daysElapsed >= maxDays)
        {
            ClearSickSignal(pawn);
            return;
        }

        float chance = Mathf.Clamp01(Props.firstDayChance + Props.chanceIncreasePerDay * (_daysElapsed - 1));
        if (Rand.Chance(chance))
        {
            ClearSickSignal(pawn);
            return;
        }

        ScheduleNextRoll();
    }

    public override void CompExposeData()
    {
        base.CompExposeData();
        Scribe_Values.Look(ref _nextRecoveryRollTick, "nextRecoveryRollTick", -1);
        Scribe_Values.Look(ref _daysElapsed, "daysElapsed");
    }

    private void ScheduleNextRoll()
    {
        _nextRecoveryRollTick = Find.TickManager.TicksGame + TicksPerDay;
    }

    private void ClearSickSignal(Pawn pawn)
    {
        if (parent != null && pawn.health?.hediffSet?.hediffs.Contains(parent) == true)
        {
            pawn.health.RemoveHediff(parent);
        }
    }
}
