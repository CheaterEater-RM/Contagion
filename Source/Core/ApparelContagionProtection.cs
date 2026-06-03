using Verse;

namespace Contagion;

// The four physical protection channels a worn item can contribute to. Airway is special-cased
// for respiratory vectors (sealed-vs-filtering, see ContagionApparelProtectionUtility); the other
// three plus airway are weighted per contact vector via ContactProtectionProfile.
public enum ContagionChannel
{
    Airway,
    Skin,
    Hands,
    Feet
}

// Per-item, author-facing override for how an apparel def seals each channel against disease.
// Authoritative over the stat/tech-level fallback (resolution hierarchy, design §3.5). DLC- and
// CE-independent: a modder ships this on gloves/hazmat and protection appears with no Contagion code.
public sealed class ApparelContagionProtection : DefModExtension
{
    // Per-channel seal quality, 0 (porous fabric) .. 1 (hermetic). Coverage is always automatic.
    public float airwaySeal;

    public float skinSeal;

    public float handsSeal;

    public float feetSeal;

    // Enclosed helmet: the airway is hermetically sealed -> immune to respiratory vectors regardless
    // of the filter math (design §3.4). Combat/space helmets set this.
    public bool airwaySealed;

    // Disease calc only: credit the hands/feet channels at this item's skinSeal when vanilla covers no
    // hands/feet but the suit obviously includes gauntlets/boots (sealed full-body shell, design §9).
    // Never touches ArmorRating / combat coverage.
    public bool coversExtremities;

    // Dedicated PPE (vacsuit, vac helmet, modded hazmat): the seal stays valid to 0 HP, exempt from the
    // ratty/tattered seal-integrity degradation (design §7).
    public bool durableSeal;

    // Sealed by fiat: this single item provides a fully sealed atmosphere (modded hazmat suit), tripping
    // the sealed-system capstone on any DLC (design §3.3 shortcut).
    public bool providesSealedAtmosphere;

    public float SealFor(ContagionChannel channel)
    {
        return channel switch
        {
            ContagionChannel.Airway => airwaySeal,
            ContagionChannel.Skin => skinSeal,
            ContagionChannel.Hands => handsSeal,
            ContagionChannel.Feet => feetSeal,
            _ => 0f
        };
    }
}

// Per-vector authored profile (composed onto a vector, design §4): how clothing blocks this vector.
// weight_{vector,channel} distributes protection across the channels; unsealedEffectiveness is how
// well mere coverage (seal = 0) blocks the vector. The full formula lives in ContagionRiskMath.
public sealed class ContactProtectionProfile
{
    // Value at seal = 0 (design §3.2). NOT a floor on total protection: a naked pawn is still 0.
    public float unsealedEffectiveness = 0.4f;

    public float airwayWeight;

    public float skinWeight;

    public float handsWeight;

    public float feetWeight;

    public float WeightFor(ContagionChannel channel)
    {
        return channel switch
        {
            ContagionChannel.Airway => airwayWeight,
            ContagionChannel.Skin => skinWeight,
            ContagionChannel.Hands => handsWeight,
            ContagionChannel.Feet => feetWeight,
            _ => 0f
        };
    }

    public bool HasAnyWeight => airwayWeight > 0f || skinWeight > 0f || handsWeight > 0f || feetWeight > 0f;
}
