using Verse;

namespace Contagion;

public sealed class Hediff_ContagionTemporaryImmunity : Hediff
{
    private int _expiryTick = -1;

    public HediffDef ProtectedDiseaseDef;

    public override bool Visible => false;

    public override bool ShouldRemove
    {
        get
        {
            if (ProtectedDiseaseDef == null || _expiryTick < 0)
            {
                return true;
            }

            if (Find.TickManager.TicksGame >= _expiryTick)
            {
                return true;
            }

            return base.ShouldRemove;
        }
    }

    public int ExpiryTick => _expiryTick;

    public void Configure(HediffDef protectedDiseaseDef, int expiryTick)
    {
        ProtectedDiseaseDef = protectedDiseaseDef;
        _expiryTick = expiryTick;
        Severity = 1f;
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Defs.Look(ref ProtectedDiseaseDef, "protectedDiseaseDef");
        Scribe_Values.Look(ref _expiryTick, "expiryTick", -1);
    }
}