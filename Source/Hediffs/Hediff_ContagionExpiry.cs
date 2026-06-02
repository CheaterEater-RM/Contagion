using Verse;

namespace Contagion;

public sealed class Hediff_ContagionExpiry : Hediff
{
    private int _expiryTick = -1;

    public HediffDef AssociatedDef;

    public override bool Visible => false;

    public override bool ShouldRemove
    {
        get
        {
            if (_expiryTick < 0 || Find.TickManager.TicksGame >= _expiryTick)
            {
                return true;
            }

            return def == ContagionDefOf.Contagion_TemporaryImmunity && AssociatedDef == null
                ? true
                : base.ShouldRemove;
        }
    }

    public int ExpiryTick => _expiryTick;

    public void Configure(int expiryTick, HediffDef associatedDef = null)
    {
        _expiryTick = expiryTick;
        AssociatedDef = associatedDef;
        Severity = 1f;
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Defs.Look(ref AssociatedDef, "associatedDef");
        Scribe_Values.Look(ref _expiryTick, "expiryTick", -1);
    }
}
