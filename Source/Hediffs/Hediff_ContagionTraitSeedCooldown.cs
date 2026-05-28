using Verse;

namespace Contagion;

// Pawn-wide cooldown applied after a trait-driven disease seed (vanilla's randomDiseaseMtbDays
// path, e.g. Sickly). While active, the ApplyToPawns prefix skips further trait-driven seeds on
// this pawn — preventing back-to-back random illnesses on the same colonist.
public sealed class Hediff_ContagionTraitSeedCooldown : Hediff
{
    private int _expiryTick = -1;

    public override bool Visible => false;

    public override bool ShouldRemove
    {
        get
        {
            if (_expiryTick < 0 || Find.TickManager.TicksGame >= _expiryTick)
            {
                return true;
            }

            return base.ShouldRemove;
        }
    }

    public int ExpiryTick => _expiryTick;

    public void Configure(int expiryTick)
    {
        _expiryTick = expiryTick;
        Severity = 1f;
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref _expiryTick, "expiryTick", -1);
    }
}
