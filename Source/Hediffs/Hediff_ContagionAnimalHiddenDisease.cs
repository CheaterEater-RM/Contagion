using Verse;

namespace Contagion;

public sealed class Hediff_ContagionAnimalHiddenDisease : HediffWithComps
{
    private bool _diagnosed;

    public bool Diagnosed => _diagnosed;

    public override bool Visible => _diagnosed && base.Visible;

    public void MarkDiagnosed()
    {
        _diagnosed = true;
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref _diagnosed, "diagnosed");
    }
}
