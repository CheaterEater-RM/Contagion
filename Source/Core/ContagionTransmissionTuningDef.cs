using System;
using Verse;

namespace Contagion;

// Singleton Def carrying global transmission cadence tuning. Define exactly one instance in XML with
// defName "Contagion_TransmissionTuning". Kept as a Def (not a hardcoded const) so the cadence can be
// tuned without recompiling and overridden by modpacks via PatchOperationReplace.
public sealed class ContagionTransmissionTuningDef : Def
{
    // Ticks between live transmission checks for a given source/target bucket. The work is staggered
    // across the interval, but each bucket still gets one per-pass chance per window. 60000 ticks = 1
    // day, so 500 = 120 checks/day per bucket. Raising it slows spread proportionally and cuts CPU.
    public int transmissionCheckIntervalTicks = 500;

    public static ContagionTransmissionTuningDef Active =>
        DefDatabase<ContagionTransmissionTuningDef>.GetNamed("Contagion_TransmissionTuning", errorOnFail: false);

    // Resolved cadence with a floor so a bad/absent value can never stall or zero-divide the pass gate.
    public static int CheckIntervalTicks => Math.Max(1, Active?.transmissionCheckIntervalTicks ?? 500);
}
