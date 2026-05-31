using HarmonyLib;
using RimWorld;
using Verse;

namespace Contagion;

// Gut worms and muscle parasites have no lethal severity and no immunity race: without
// treatment they neither kill an animal nor clear on their own — the animal is a permanent
// reservoir. This comp resolves that by passively accumulating totalTendQuality over time,
// so the disease eventually reaches disappearsAtTotalTendQuality=3 (300%) without vet input.
//
// Rates are split by faction so wild animals (no vet access) clear faster than domestic ones,
// keeping player tending meaningfully faster than doing nothing.
//   Wild:     0.20/day → ~15 days to clear
//   Domestic: 0.12/day → ~25 days to clear
//   Vet tend: ~1.0 per tend at 48h window → ~3 tends over 4–6 days
//
// Do NOT add to Animal_Plague: plague has HediffCompProperties_Immunizable — the immunity
// race is the recovery mechanism and natural lethality is intentional.
public sealed class HediffCompProperties_AnimalNaturalRecovery : HediffCompProperties
{
    // Total tend quality accumulated per game day for wild (non-player-faction) animals.
    // At the 3.0 threshold: 0.20/day → ~15 days to clear.
    public float wildPerDay = 0.20f;

    // Total tend quality accumulated per game day for domestic (player-faction) animals.
    // At the 3.0 threshold: 0.12/day → ~25 days to clear. Lower than wild so player
    // tending (which adds ~1.0 quality per tend) is a meaningful speedup.
    public float domesticPerDay = 0.12f;

    public HediffCompProperties_AnimalNaturalRecovery()
    {
        compClass = typeof(HediffComp_AnimalNaturalRecovery);
    }
}

public sealed class HediffComp_AnimalNaturalRecovery : HediffComp
{
    private const float TicksPerDay = 60000f;

    private static readonly AccessTools.FieldRef<HediffComp_TendDuration, float> TotalTendQualityRef =
        AccessTools.FieldRefAccess<HediffComp_TendDuration, float>("totalTendQuality");

    private HediffCompProperties_AnimalNaturalRecovery Props =>
        (HediffCompProperties_AnimalNaturalRecovery)props;

    public override void CompPostPostTickInterval(ref float severityAdjustment, int delta)
    {
        Pawn pawn = Pawn;
        if (pawn == null || !pawn.Spawned || pawn.RaceProps?.Animal != true)
        {
            return;
        }

        HediffComp_TendDuration tendComp = parent.TryGetComp<HediffComp_TendDuration>();
        if (tendComp == null)
        {
            return;
        }

        float perDay = pawn.Faction == Faction.OfPlayer ? Props.domesticPerDay : Props.wildPerDay;
        if (perDay <= 0f)
        {
            return;
        }

        TotalTendQualityRef(tendComp) += perDay * delta / TicksPerDay;
    }
}
