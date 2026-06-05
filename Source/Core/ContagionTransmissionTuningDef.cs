using System;
using Verse;

namespace Contagion;

// Singleton Def carrying global transmission cadence tuning. Define exactly one instance in XML with
// defName "Contagion_TransmissionTuning". Kept as a Def (not a hardcoded const) so the cadence can be
// tuned without recompiling and overridden by modpacks via PatchOperationReplace.
public sealed class ContagionTransmissionTuningDef : Def
{
    // Ticks between live pawn-to-pawn transmission passes (and the corpse-exposure pass, which scales
    // its per-pass chance by this interval). 60000 ticks = 1 day, so 500 = 120 passes/day. Raising it
    // slows pawn-to-pawn spread proportionally AND cuts CPU (fewer passes/day); it was raised from the
    // original 250 to 500 to take the edge off dense-group "wildfire" spread (see tools/ContagionSpreadSim).
    public int transmissionCheckIntervalTicks = 500;

    public static ContagionTransmissionTuningDef Active =>
        DefDatabase<ContagionTransmissionTuningDef>.GetNamed("Contagion_TransmissionTuning", errorOnFail: false);

    // Resolved cadence with a floor so a bad/absent value can never stall or zero-divide the pass gate.
    public static int CheckIntervalTicks => Math.Max(1, Active?.transmissionCheckIntervalTicks ?? 500);
}
