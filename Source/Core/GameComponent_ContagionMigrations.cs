using RimWorld;
using Verse;

namespace Contagion;

public sealed class GameComponent_ContagionMigrations : GameComponent
{
    private bool _infectedCorpseBillMigrationDone;

    public GameComponent_ContagionMigrations(Game game)
    {
    }

    public override void StartedNewGame()
    {
        base.StartedNewGame();
        _infectedCorpseBillMigrationDone = true;
    }

    public override void LoadedGame()
    {
        base.LoadedGame();

        if (_infectedCorpseBillMigrationDone)
        {
            return;
        }

        int changed = ContagionBillUtility.DisallowInfectedCorpsesOnButcherBills();
        _infectedCorpseBillMigrationDone = true;
        ContagionDiagnostics.Trace($"Infected corpse bill migration updated {changed} butcher bills.");
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref _infectedCorpseBillMigrationDone, "infectedCorpseBillMigrationDone");
    }
}
