using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

// Unified apparel-protection model (see docs/Apparel_Protection_Design.md).
//
// Every worn item resolves, per ThingDef, to four channels {Airway, Skin, Hands, Feet} with a
// COVERAGE (automatic, from bodyPartGroups/coverageAbs) and a SEAL (authored ApparelContagionProtection,
// else a stat + tech-level fallback). Per pawn we aggregate coverage and (integrity-adjusted) seal,
// detect the sealed-system capstone, and expose:
//   - GetRespiratoryMaskFactor  : two-sided airway factor (sealed helmet/capstone => immune; masks/lungs filter)
//   - GetContactProtectionFactor: target-side multiplier for contact/surface/cooking vectors
//   - GetCookSourceProtectionFactor: source-side multiplier for cook -> food contamination
//
// Genes are excluded from the physical barrier; whitelisted gene airway immunity is layered on top via
// RespiratoryProtectionCache (e.g. breathless), unchanged from the original respiratory model.
public static class ContagionApparelProtectionUtility
{
    private const int ChannelCount = 4;

    // Per-pawn aggregate cache TTL (ticks). Protection only shifts when apparel/HP change, so a short
    // staleness window is harmless and keeps the per-body-part walk off the hot transmission path.
    private const int CacheTtlTicks = 250;

    private static bool _initialized;

    private static StatDef _vacuumResistanceStat;   // [MayRequireOdyssey] -> may stay null

    private static BodyPartGroupDef[] _headGroups;
    private static BodyPartGroupDef[] _handGroups;
    private static BodyPartGroupDef[] _footGroups;

    // Total coverageAbs of the human body that falls in each channel (constant; denominator for fractions).
    private static readonly float[] _channelTotalAbs = new float[ChannelCount];

    private static readonly Dictionary<ThingDef, ApparelProtectionInfo> _infoCache =
        new Dictionary<ThingDef, ApparelProtectionInfo>();

    private static readonly Dictionary<Pawn, CachedProtection> _pawnCache =
        new Dictionary<Pawn, CachedProtection>();

    private static readonly List<Pawn> _pawnCacheRemovalBuffer = new List<Pawn>();

    // Per-ThingDef resolved protection. Coverage fractions are channel-normalised (0-1).
    private sealed class ApparelProtectionInfo
    {
        public readonly float[] Coverage = new float[ChannelCount];
        public readonly float[] Seal = new float[ChannelCount];
        public bool AirwaySealed;
        public bool CoversExtremities;
        public bool DurableSeal;
        public bool ProvidesSealedAtmosphere;
        public bool Authored;
        public float VacuumResistance;
    }

    // Per-pawn aggregate, integrity-adjusted, including assumed extremity coverage and capstone state.
    public sealed class PawnProtection
    {
        public readonly float[] Coverage = new float[ChannelCount];
        public readonly float[] Seal = new float[ChannelCount];
        public bool AirwaySealed;
        public float AirwayFilterSeal;
        public bool FullySealed;
    }

    private struct CachedProtection
    {
        public int Tick;
        public PawnProtection Protection;
    }

    // ---- public API ---------------------------------------------------------------------------

    // Two-sided respiratory reduction. Signature preserved from the retired mask-only utility.
    public static float GetRespiratoryMaskFactor(Pawn source, Pawn target, RespiratoryVector vector)
    {
        if (vector == null)
        {
            return 1f;
        }

        return GetSideFactor(source, vector, vector.maskSourceEffectiveness)
            * GetSideFactor(target, vector, vector.maskTargetEffectiveness);
    }

    // Target-side multiplier (1 = no protection, 0 = immune) for a contact/surface/cooking vector.
    public static float GetContactProtectionFactor(Pawn target, ContactProtectionProfile profile)
    {
        if (target == null || profile == null || !profile.HasAnyWeight)
        {
            return 1f;
        }

        return 1f - GetProfileProtection(target, profile);
    }

    // Source-side multiplier for cook -> food contamination (Typhoid Mary). Same math; a sealed-suit
    // cook returns ~0, baking ~no contamination into meals.
    public static float GetCookSourceProtectionFactor(Pawn cook, ContactProtectionProfile profile)
    {
        if (cook == null || profile == null || !profile.HasAnyWeight)
        {
            return 1f;
        }

        return 1f - GetProfileProtection(cook, profile);
    }

    public static bool IsAirwaySealed(Pawn pawn)
    {
        return pawn != null && GetPawnProtection(pawn).AirwaySealed;
    }

    public static bool IsFullySealed(Pawn pawn)
    {
        return pawn != null && GetPawnProtection(pawn).FullySealed;
    }

    // ---- UX summaries (design §8) -------------------------------------------------------------

    public enum ProtectionLevel
    {
        None,
        Minimal,
        Poor,
        Ok,
        Good,
        Excellent,
        Immune
    }

    public readonly struct ProtectionCategorySummary
    {
        public ProtectionCategorySummary(ProtectionLevel level, float protection, string contributor = null)
        {
            Level = level;
            Protection = Mathf.Clamp01(protection);
            Contributor = contributor;
        }

        public readonly ProtectionLevel Level;
        public readonly float Protection;
        public readonly string Contributor;

        public bool HasProtection => Level != ProtectionLevel.None;
    }

    public readonly struct ProtectionSummary
    {
        public ProtectionSummary(
            ProtectionCategorySummary airborne,
            ProtectionCategorySummary contact,
            ProtectionCategorySummary surface,
            bool fullySealed,
            bool airwaySealed)
        {
            FullySealed = fullySealed;
            AirwaySealed = airwaySealed;
            AirborneCategory = airborne;
            ContactCategory = contact;
            SurfaceCategory = surface;
        }

        public readonly bool FullySealed;
        public readonly bool AirwaySealed;
        public readonly ProtectionCategorySummary AirborneCategory;
        public readonly ProtectionCategorySummary ContactCategory;
        public readonly ProtectionCategorySummary SurfaceCategory;

        public ProtectionLevel Airborne => AirborneCategory.Level;

        public ProtectionLevel Contact => ContactCategory.Level;

        public ProtectionLevel Surface => SurfaceCategory.Level;

        public float AirborneProtection => AirborneCategory.Protection;

        public float ContactProtection => ContactCategory.Protection;

        public float SurfaceProtection => SurfaceCategory.Protection;

        public string AirborneContributor => AirborneCategory.Contributor;

        public string ContactContributor => ContactCategory.Contributor;

        public string SurfaceContributor => SurfaceCategory.Contributor;
    }

    // Representative UX category profiles. All player-facing summaries use these category summaries;
    // actual transmission still uses each vector's authored ContactProtectionProfile.
    // Skin/hands-heavy for contact, feet/skin for surface.
    private static readonly ContactProtectionProfile SummaryContactProfile = new ContactProtectionProfile
    {
        unsealedEffectiveness = 0.4f,
        skinWeight = 0.55f,
        handsWeight = 0.30f,
        airwayWeight = 0.15f
    };

    private static readonly ContactProtectionProfile SummarySurfaceProfile = new ContactProtectionProfile
    {
        unsealedEffectiveness = 0.4f,
        skinWeight = 0.45f,
        feetWeight = 0.40f,
        airwayWeight = 0.15f
    };

    public static ProtectionSummary GetProtectionSummary(Pawn pawn)
    {
        if (pawn == null)
        {
            return new ProtectionSummary(
                NoCategorySummary(),
                NoCategorySummary(),
                NoCategorySummary(),
                false,
                false);
        }

        PawnProtection prot = GetPawnProtection(pawn);

        return new ProtectionSummary(
            BuildAirborneCategorySummary(
                prot.FullySealed || prot.AirwaySealed ? 1f : prot.AirwayFilterSeal,
                DominantAirborneContributor(pawn)),
            BuildCategorySummary(
                ComputeProtection(prot, SummaryContactProfile),
                DominantContactContributor(pawn, SummaryContactProfile)),
            BuildCategorySummary(
                ComputeProtection(prot, SummarySurfaceProfile),
                DominantContactContributor(pawn, SummarySurfaceProfile)),
            prot.FullySealed,
            prot.AirwaySealed);
    }

    private static ProtectionCategorySummary NoCategorySummary()
    {
        return new ProtectionCategorySummary(ProtectionLevel.None, 0f);
    }

    private static ProtectionCategorySummary BuildCategorySummary(float protection, string contributor = null)
    {
        return new ProtectionCategorySummary(Bucket(protection), protection, contributor);
    }

    private static ProtectionCategorySummary BuildAirborneCategorySummary(float protection, string contributor = null)
    {
        protection = Mathf.Clamp01(protection);
        return new ProtectionCategorySummary(
            protection >= 0.999f ? ProtectionLevel.Immune : Bucket(protection),
            protection,
            contributor);
    }

    private static ProtectionLevel Bucket(float protection)
    {
        protection = Mathf.Clamp01(protection);
        if (protection >= 0.999f)
        {
            return ProtectionLevel.Immune;
        }

        if (protection >= 0.8f)
        {
            return ProtectionLevel.Excellent;
        }

        if (protection >= 0.6f)
        {
            return ProtectionLevel.Good;
        }

        if (protection >= 0.4f)
        {
            return ProtectionLevel.Ok;
        }

        if (protection >= 0.2f)
        {
            return ProtectionLevel.Poor;
        }

        return protection > 0f ? ProtectionLevel.Minimal : ProtectionLevel.None;
    }

    public static string FormatProtectionSummaryLine(ProtectionSummary summary)
    {
        return "Contagion_DiseaseProtectionCompact".Translate(
            "Contagion_DiseaseProtectionAirborne".Translate(),
            LevelLabel(summary.Airborne),
            "Contagion_DiseaseProtectionContact".Translate(),
            LevelLabel(summary.Contact),
            "Contagion_DiseaseProtectionSurface".Translate(),
            LevelLabel(summary.Surface));
    }

    public static string BuildProtectionSummaryTooltip(Pawn pawn)
    {
        ProtectionSummary summary = GetProtectionSummary(pawn);
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("Contagion_DiseaseProtectionHeader".Translate());
        AppendSummaryTooltipLine(
            sb,
            "Contagion_DiseaseProtectionAirborne".Translate(),
            summary.Airborne,
            summary.AirborneProtection,
            summary.AirborneContributor,
            "Contagion_DiseaseProtectionAirborneTooltip".Translate());
        AppendSummaryTooltipLine(
            sb,
            "Contagion_DiseaseProtectionContact".Translate(),
            summary.Contact,
            summary.ContactProtection,
            summary.ContactContributor,
            "Contagion_DiseaseProtectionContactTooltip".Translate());
        AppendSummaryTooltipLine(
            sb,
            "Contagion_DiseaseProtectionSurface".Translate(),
            summary.Surface,
            summary.SurfaceProtection,
            summary.SurfaceContributor,
            "Contagion_DiseaseProtectionSurfaceTooltip".Translate());
        if (summary.FullySealed)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append("Contagion_DiseaseProtectionFullySealedTooltip".Translate());
        }

        return sb.ToString().TrimEndNewlines();
    }

    private static void AppendSummaryTooltipLine(
        StringBuilder sb,
        string label,
        ProtectionLevel level,
        float protection,
        string contributor,
        string explanation)
    {
        sb.AppendLine();
        string value = "Contagion_DiseaseProtectionTooltipLine".Translate(
            label,
            LevelLabel(level),
            FormatProtectionPercent(protection));
        if (!contributor.NullOrEmpty())
        {
            value += " " + "Contagion_DiseaseProtectionContributor".Translate(contributor);
        }

        sb.AppendLine(value);
        sb.Append(explanation);
    }

    public static string LevelLabel(ProtectionLevel level)
    {
        switch (level)
        {
            case ProtectionLevel.Immune:
                return "Contagion_ProtectionImmune".Translate();
            case ProtectionLevel.Excellent:
                return "Contagion_ProtectionExcellent".Translate();
            case ProtectionLevel.Good:
                return "Contagion_ProtectionGood".Translate();
            case ProtectionLevel.Ok:
                return "Contagion_ProtectionOk".Translate();
            case ProtectionLevel.Poor:
                return "Contagion_ProtectionPoor".Translate();
            case ProtectionLevel.Minimal:
                return "Contagion_ProtectionMinimal".Translate();
            default:
                return "Contagion_ProtectionNone".Translate();
        }
    }

    // Per-item display data for the apparel info card (design §8, Tier 3). Resolved seals only;
    // ThingDef display assumes intact gear; Apparel display includes current hit points.
    public readonly struct ItemProtectionDisplay
    {
        public ItemProtectionDisplay(
            float airwaySeal,
            float airwayFilter,
            bool airwaySealed,
            float skinSeal,
            float handsSeal,
            float feetSeal,
            bool durableSeal,
            ProtectionCategorySummary airborne,
            ProtectionCategorySummary contact,
            ProtectionCategorySummary surface,
            bool damaged)
        {
            AirwaySeal = airwaySeal;
            AirwayFilter = airwayFilter;
            AirwaySealed = airwaySealed;
            SkinSeal = skinSeal;
            HandsSeal = handsSeal;
            FeetSeal = feetSeal;
            DurableSeal = durableSeal;
            AirborneCategory = airborne;
            ContactCategory = contact;
            SurfaceCategory = surface;
            ContactProtection = contact.Protection;
            SurfaceProtection = surface.Protection;
            Damaged = damaged;
        }

        public readonly float AirwaySeal;
        public readonly float AirwayFilter;
        public readonly bool AirwaySealed;
        public readonly float SkinSeal;
        public readonly float HandsSeal;
        public readonly float FeetSeal;
        public readonly bool DurableSeal;
        public readonly ProtectionCategorySummary AirborneCategory;
        public readonly ProtectionCategorySummary ContactCategory;
        public readonly ProtectionCategorySummary SurfaceCategory;
        public readonly float ContactProtection;
        public readonly float SurfaceProtection;
        public readonly bool Damaged;

        public ProtectionLevel AirborneLevel => AirborneCategory.Level;

        public ProtectionLevel ContactLevel => ContactCategory.Level;

        public ProtectionLevel SurfaceLevel => SurfaceCategory.Level;

        public bool HasAnyProtection => AirborneLevel != ProtectionLevel.None
            || ContactLevel != ProtectionLevel.None
            || SurfaceLevel != ProtectionLevel.None;
    }

    public static ItemProtectionDisplay GetItemDisplay(ThingDef def)
    {
        return GetItemDisplay(def, null);
    }

    public static ItemProtectionDisplay GetItemDisplay(Apparel apparel)
    {
        return GetItemDisplay(apparel?.def, apparel);
    }

    public static string BuildItemProtectionTooltip(Apparel apparel)
    {
        ItemProtectionDisplay display = GetItemDisplay(apparel);
        if (!display.HasAnyProtection)
        {
            return null;
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("Contagion_DiseaseProtectionHeader".Translate());
        AppendItemTooltipLine(
            sb,
            "Contagion_DiseaseProtectionAirborne".Translate(),
            display.AirborneCategory,
            display.AirwaySealed ? "Contagion_ItemAirborneSealedNote".Translate() : "Contagion_ItemAirborneFilterNote".Translate(),
            false);
        AppendItemTooltipLine(
            sb,
            "Contagion_DiseaseProtectionContact".Translate(),
            display.ContactCategory,
            "Contagion_ItemContactNote".Translate(),
            display.Damaged);
        AppendItemTooltipLine(
            sb,
            "Contagion_DiseaseProtectionSurface".Translate(),
            display.SurfaceCategory,
            "Contagion_ItemSurfaceNote".Translate(),
            display.Damaged);
        return sb.ToString().TrimEndNewlines();
    }

    private static void AppendItemTooltipLine(
        StringBuilder sb,
        string label,
        ProtectionCategorySummary summary,
        string note,
        bool damaged)
    {
        sb.AppendLine();
        sb.AppendLine("Contagion_DiseaseProtectionTooltipLine".Translate(
            label,
            LevelLabel(summary.Level),
            FormatProtectionPercent(summary.Protection)));
        sb.Append(note);
        if (damaged && summary.HasProtection)
        {
            sb.Append(" ");
            sb.Append("Contagion_ItemDamagedNote".Translate());
        }
    }

    private static ItemProtectionDisplay GetItemDisplay(ThingDef def, Apparel apparel)
    {
        ApparelProtectionInfo info = GetInfo(def);
        if (info == null)
        {
            return default;
        }

        float integrity = GetItemIntegrity(apparel, info);
        bool damaged = apparel != null && !info.DurableSeal && integrity < 1f;
        float airwaySeal = info.Seal[(int)ContagionChannel.Airway] * integrity;
        float airwayFilter = info.Authored
            ? airwaySeal
            : Mathf.Clamp01(GetThingDefToxicResistance(def) + airwaySeal);
        float airborneProtection = info.Authored ? airwaySeal : Mathf.Max(airwaySeal, airwayFilter);

        if (info.AirwaySealed && (info.DurableSeal || integrity >= 1f))
        {
            airborneProtection = 1f;
        }

        return new ItemProtectionDisplay(
            airwaySeal,
            airwayFilter,
            info.AirwaySealed && (info.DurableSeal || integrity >= 1f),
            info.Seal[(int)ContagionChannel.Skin] * integrity,
            info.Seal[(int)ContagionChannel.Hands] * integrity,
            info.Seal[(int)ContagionChannel.Feet] * integrity,
            info.DurableSeal,
            BuildAirborneCategorySummary(airborneProtection),
            BuildCategorySummary(ComputeItemProtection(info, SummaryContactProfile, integrity)),
            BuildCategorySummary(ComputeItemProtection(info, SummarySurfaceProfile, integrity)),
            damaged);
    }

    public static string FormatProtectionPercent(float protection)
    {
        return Mathf.Clamp01(protection).ToStringPercent("F0");
    }

    // ---- core computation ---------------------------------------------------------------------

    private static string DominantAirborneContributor(Pawn pawn)
    {
        List<Apparel> worn = pawn?.apparel?.WornApparel;
        if (worn == null || worn.Count == 0)
        {
            return null;
        }

        Apparel best = null;
        float bestProtection = 0f;
        for (int i = 0; i < worn.Count; i++)
        {
            Apparel apparel = worn[i];
            ApparelProtectionInfo info = GetInfo(apparel?.def);
            if (info == null)
            {
                continue;
            }

            float integrity = GetItemIntegrity(apparel, info);
            bool sealedAirway = info.AirwaySealed && (info.DurableSeal || integrity >= 1f);
            float protection = sealedAirway
                ? 1f
                : Mathf.Clamp01((info.Authored ? 0f : GetThingDefToxicResistance(apparel.def))
                    + info.Seal[(int)ContagionChannel.Airway] * integrity);
            if (protection > bestProtection)
            {
                bestProtection = protection;
                best = apparel;
            }
        }

        return bestProtection > 0.05f ? best?.LabelCap : null;
    }

    private static string DominantContactContributor(Pawn pawn, ContactProtectionProfile profile)
    {
        List<Apparel> worn = pawn?.apparel?.WornApparel;
        if (worn == null || worn.Count == 0)
        {
            return null;
        }

        Apparel best = null;
        float bestProtection = 0f;
        for (int i = 0; i < worn.Count; i++)
        {
            Apparel apparel = worn[i];
            ApparelProtectionInfo info = GetInfo(apparel?.def);
            if (info == null)
            {
                continue;
            }

            float protection = ComputeItemProtection(info, profile, GetItemIntegrity(apparel, info));
            if (protection > bestProtection)
            {
                bestProtection = protection;
                best = apparel;
            }
        }

        return bestProtection > 0.05f ? best?.LabelCap : null;
    }

    private static float GetProfileProtection(Pawn pawn, ContactProtectionProfile profile)
    {
        return ComputeProtection(GetPawnProtection(pawn), profile);
    }

    private static float ComputeProtection(PawnProtection prot, ContactProtectionProfile profile)
    {
        if (prot.FullySealed)
        {
            return 1f;
        }

        var channels = new (float weight, float coverage, float seal)[ChannelCount];
        for (int c = 0; c < ChannelCount; c++)
        {
            channels[c] = (profile.WeightFor((ContagionChannel)c), prot.Coverage[c], prot.Seal[c]);
        }

        return ContagionRiskMath.WeightedProtection(channels, profile.unsealedEffectiveness);
    }

    private static float ComputeItemProtection(ApparelProtectionInfo info, ContactProtectionProfile profile, float integrity)
    {
        if (info == null || profile == null)
        {
            return 0f;
        }

        if (info.ProvidesSealedAtmosphere && integrity >= 1f)
        {
            return 1f;
        }

        return ContagionRiskMath.WeightedProtection(ItemChannels(info, profile, integrity), profile.unsealedEffectiveness);
    }

    private static (float weight, float coverage, float seal)[] ItemChannels(
        ApparelProtectionInfo info,
        ContactProtectionProfile profile,
        float integrity)
    {
        var channels = new (float weight, float coverage, float seal)[ChannelCount];
        float extremitySeal = info.CoversExtremities
            ? info.Seal[(int)ContagionChannel.Skin] * integrity
            : 0f;
        for (int c = 0; c < ChannelCount; c++)
        {
            float coverage = info.Coverage[c];
            float seal = info.Seal[c] * integrity;
            if (info.CoversExtremities
                && (c == (int)ContagionChannel.Hands || c == (int)ContagionChannel.Feet))
            {
                coverage = Mathf.Max(coverage, 1f);
                seal = Mathf.Max(seal, extremitySeal);
            }

            channels[c] = (profile.WeightFor((ContagionChannel)c), coverage, seal);
        }

        return channels;
    }

    private static float GetSideFactor(Pawn pawn, RespiratoryVector vector, float barrierEffectiveness)
    {
        if (pawn == null)
        {
            return 1f;
        }

        float barrierFactor = 1f;
        if (barrierEffectiveness > 0f)
        {
            PawnProtection prot = GetPawnProtection(pawn);
            // A hermetic helmet confers immunity only for genuinely airway-borne vectors. For contact/flea
            // vectors reusing the respiratory schema (e.g. plague proximity, airwayImmunityFactor 0) the
            // seal instead acts as a physical filter, so a sealed helmet reduces but never eliminates them.
            barrierFactor = prot.AirwaySealed && vector.airwayImmunityFactor > 0f
                ? 0f
                : 1f - prot.AirwayFilterSeal * barrierEffectiveness;
        }

        // Whitelisted gene airway immunity (e.g. breathless), scaled by how airway-dependent the vector is.
        float immunityFactor = 1f;
        if (vector.airwayImmunityFactor > 0f && RespiratoryProtectionCache.HasAnyGeneProtections)
        {
            immunityFactor = 1f - RespiratoryProtectionCache.GetGeneProtection(pawn) * vector.airwayImmunityFactor;
        }

        return Mathf.Clamp01(barrierFactor * immunityFactor);
    }

    public static PawnProtection GetPawnProtection(Pawn pawn)
    {
        if (pawn == null)
        {
            return new PawnProtection();
        }

        EnsureInitialized();

        int now = Find.TickManager?.TicksGame ?? 0;
        PrunePawnCache(now);
        if (_pawnCache.TryGetValue(pawn, out CachedProtection cached)
            && cached.Protection != null
            && now - cached.Tick <= CacheTtlTicks)
        {
            return cached.Protection;
        }

        PawnProtection prot = ComputePawnProtection(pawn);
        _pawnCache[pawn] = new CachedProtection { Tick = now, Protection = prot };
        return prot;
    }

    private static void PrunePawnCache(int now)
    {
        _pawnCacheRemovalBuffer.Clear();
        foreach (KeyValuePair<Pawn, CachedProtection> entry in _pawnCache)
        {
            Pawn pawn = entry.Key;
            if (pawn == null
                || pawn.Destroyed
                || pawn.Dead
                || entry.Value.Protection == null
                || now - entry.Value.Tick > CacheTtlTicks)
            {
                _pawnCacheRemovalBuffer.Add(pawn);
            }
        }

        for (int i = 0; i < _pawnCacheRemovalBuffer.Count; i++)
        {
            _pawnCache.Remove(_pawnCacheRemovalBuffer[i]);
        }

        _pawnCacheRemovalBuffer.Clear();
    }

    private static PawnProtection ComputePawnProtection(Pawn pawn)
    {
        PawnProtection result = new PawnProtection();

        List<Apparel> worn = pawn?.apparel?.WornApparel;
        BodyDef body = pawn?.RaceProps?.body;
        if (worn == null || worn.Count == 0 || body == null)
        {
            // No apparel: only body-part airway filtering (detoxifier lung etc.) applies.
            result.AirwayFilterSeal = Mathf.Clamp01(GetBodyPartToxicResistance(pawn));
            return result;
        }

        // Walk body parts once, attributing coverage and the best (max) integrity-adjusted seal per channel.
        float[] coveredAbs = new float[ChannelCount];
        float[] sealWeightedAbs = new float[ChannelCount];
        List<BodyPartRecord> parts = body.AllParts;
        for (int i = 0; i < parts.Count; i++)
        {
            BodyPartRecord part = parts[i];
            int channel = ClassifyPart(part);

            float bestSeal = 0f;
            bool covered = false;
            for (int j = 0; j < worn.Count; j++)
            {
                Apparel a = worn[j];
                if (a?.def?.apparel == null || !a.def.apparel.CoversBodyPart(part))
                {
                    continue;
                }

                covered = true;
                ApparelProtectionInfo info = GetInfo(a.def);
                float itemSeal = info.Seal[channel] * GetItemIntegrity(a, info);
                if (itemSeal > bestSeal)
                {
                    bestSeal = itemSeal;
                }
            }

            if (covered)
            {
                coveredAbs[channel] += part.coverageAbs;
                sealWeightedAbs[channel] += part.coverageAbs * bestSeal;
            }
        }

        for (int c = 0; c < ChannelCount; c++)
        {
            result.Coverage[c] = _channelTotalAbs[c] > 0f ? Mathf.Clamp01(coveredAbs[c] / _channelTotalAbs[c]) : 0f;
            result.Seal[c] = coveredAbs[c] > 0f ? Mathf.Clamp01(sealWeightedAbs[c] / coveredAbs[c]) : 0f;
        }

        // Assumed extremity coverage (design §9): a sealed full-body suit includes gauntlets/boots even
        // though vanilla covers no hands/feet. Disease calc only; never touches combat coverage.
        ApplyAssumedExtremityCoverage(worn, result);

        // Airway: filtering inputs (masks + lung implants) and the binary hermetic seal.
        result.AirwayFilterSeal = Mathf.Clamp01(
            GetApparelToxicResistance(pawn)
            + GetBodyPartToxicResistance(pawn)
            + result.Seal[(int)ContagionChannel.Airway]);
        result.AirwaySealed = ComputeAirwaySealed(worn);

        // Sealed-system capstone (design §3.3).
        result.FullySealed = ComputeFullySealed(worn, result);

        return result;
    }

    private static void ApplyAssumedExtremityCoverage(List<Apparel> worn, PawnProtection result)
    {
        float extremitySeal = 0f;
        for (int j = 0; j < worn.Count; j++)
        {
            Apparel a = worn[j];
            if (a?.def == null)
            {
                continue;
            }

            ApparelProtectionInfo info = GetInfo(a.def);
            if (!info.CoversExtremities)
            {
                continue;
            }

            float seal = info.Seal[(int)ContagionChannel.Skin] * GetItemIntegrity(a, info);
            if (seal > extremitySeal)
            {
                extremitySeal = seal;
            }
        }

        if (extremitySeal <= 0f)
        {
            return;
        }

        foreach (ContagionChannel channel in new[] { ContagionChannel.Hands, ContagionChannel.Feet })
        {
            int c = (int)channel;
            if (result.Coverage[c] < 1f)
            {
                result.Coverage[c] = 1f;
            }

            if (result.Seal[c] < extremitySeal)
            {
                result.Seal[c] = extremitySeal;
            }
        }
    }

    private static bool ComputeAirwaySealed(List<Apparel> worn)
    {
        for (int j = 0; j < worn.Count; j++)
        {
            Apparel a = worn[j];
            if (a?.def?.apparel == null)
            {
                continue;
            }

            ApparelProtectionInfo info = GetInfo(a.def);
            // An incidental (non-durable) helmet only stays hermetic while structurally intact (>= ratty);
            // a ratty/tattered combat helmet drops out of "sealed" and falls back to filtering.
            bool intact = info.DurableSeal || GetItemIntegrity(a, info) >= 1f;
            if (!intact)
            {
                continue;
            }

            if (info.AirwaySealed || (!info.Authored && a.def.apparel.immuneToToxGasExposure))
            {
                return true;
            }

            if (!info.Authored
                && _vacuumResistanceStat != null
                && info.VacuumResistance >= 0.5f
                && info.Coverage[(int)ContagionChannel.Airway] > 0f)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ComputeFullySealed(List<Apparel> worn, PawnProtection result)
    {
        for (int j = 0; j < worn.Count; j++)
        {
            ApparelProtectionInfo info = GetInfo(worn[j]?.def);
            if (info != null && info.ProvidesSealedAtmosphere)
            {
                return true;
            }
        }

        // Fully-sealed (DLC-independent): airway sealed AND a near-complete high-quality skin seal.
        bool skinSealed = result.AirwaySealed
            && result.Seal[(int)ContagionChannel.Skin] >= 0.9f
            && result.Coverage[(int)ContagionChannel.Skin] >= 0.9f;
        if (skinSealed)
        {
            return true;
        }

        // Vacuum-sealed (Odyssey): integrity-adjusted aggregate VacuumResistance >= 0.95.
        if (_vacuumResistanceStat == null)
        {
            return false;
        }

        float adjustedVacuum = 0f;
        for (int j = 0; j < worn.Count; j++)
        {
            Apparel a = worn[j];
            ApparelProtectionInfo info = GetInfo(a?.def);
            if (info == null || info.Authored || info.VacuumResistance <= 0f)
            {
                continue;
            }

            // Authored apparel is resolved by Contagion's seal extensions and the DLC-independent path
            // above. The raw VacuumResistance capstone is only a fallback for unauthored modded spacer gear.
            adjustedVacuum += info.VacuumResistance * GetItemIntegrity(a, info);
        }

        return adjustedVacuum >= 0.95f;
    }

    private static float GetItemIntegrity(Apparel apparel, ApparelProtectionInfo info)
    {
        if (info.DurableSeal || apparel == null || !apparel.def.useHitPoints || apparel.MaxHitPoints <= 0)
        {
            return 1f;
        }

        return ContagionRiskMath.SealIntegrity(apparel.HitPoints / (float)apparel.MaxHitPoints);
    }

    // ---- per-ThingDef resolution (cached) -----------------------------------------------------

    private static ApparelProtectionInfo GetInfo(ThingDef def)
    {
        if (def?.apparel == null)
        {
            return null;
        }

        if (_infoCache.TryGetValue(def, out ApparelProtectionInfo cached))
        {
            return cached;
        }

        ApparelProtectionInfo info = BuildInfo(def);
        _infoCache[def] = info;
        return info;
    }

    private static ApparelProtectionInfo BuildInfo(ThingDef def)
    {
        EnsureInitialized();
        ApparelProtectionInfo info = new ApparelProtectionInfo();

        ComputeCoverage(def, info.Coverage);

        if (_vacuumResistanceStat != null)
        {
            info.VacuumResistance = Mathf.Max(0f, def.GetStatValueAbstract(_vacuumResistanceStat));
        }

        ApparelContagionProtection ext = def.GetModExtension<ApparelContagionProtection>();
        if (ext != null)
        {
            // Authored: authoritative (resolution hierarchy rule 1).
            for (int c = 0; c < ChannelCount; c++)
            {
                info.Seal[c] = Mathf.Clamp01(ext.SealFor((ContagionChannel)c));
            }

            info.AirwaySealed = ext.airwaySealed;
            info.CoversExtremities = ext.coversExtremities;
            info.DurableSeal = ext.durableSeal;
            info.ProvidesSealedAtmosphere = ext.providesSealedAtmosphere;
            info.Authored = true;
            return info;
        }

        ApplyFallbackSeal(def, info);
        return info;
    }

    // Stat + tech-level fallback for unauthored (modded) gear (design §3.5). Authored vanilla gear never
    // reaches here. ToxicEnvironmentResistance is a breathing filter, handled at the pawn level, so it is
    // not turned into a channel seal here.
    private static void ApplyFallbackSeal(ThingDef def, ApparelProtectionInfo info)
    {
        TechLevel tech = def.techLevel;
        if (tech <= TechLevel.Medieval && tech != TechLevel.Undefined)
        {
            // Medieval and below never seals.
            return;
        }

        bool immuneToTox = def.apparel.immuneToToxGasExposure;
        float vr = info.VacuumResistance;
        bool spacerPlus = tech >= TechLevel.Spacer;

        bool sealedSignal = immuneToTox || vr > 0f;
        if (!sealedSignal)
        {
            // Industrial (and Undefined) only seal with an explicit signal; with none, coverage only.
            return;
        }

        // Take VacuumResistance at face value as the channel seal for every channel this item covers
        // (spacer is generous; industrial reaches here only via an explicit signal).
        float faceValueSeal = Mathf.Clamp01(vr);
        for (int c = 0; c < ChannelCount; c++)
        {
            if (info.Coverage[c] > 0f)
            {
                info.Seal[c] = faceValueSeal;
            }
        }

        if (immuneToTox || (vr >= 0.5f && info.Coverage[(int)ContagionChannel.Airway] > 0f))
        {
            info.AirwaySealed = true;
        }

        // A high-seal full-body spacer shell is assumed to enclose the extremities too.
        if (spacerPlus && info.Coverage[(int)ContagionChannel.Skin] >= 0.6f && vr >= 0.3f)
        {
            info.CoversExtremities = true;
        }
    }

    private static void ComputeCoverage(ThingDef def, float[] coverage)
    {
        BodyDef human = BodyDefOf.Human;
        if (human == null)
        {
            return;
        }

        float[] coveredAbs = new float[ChannelCount];
        List<BodyPartRecord> parts = human.AllParts;
        for (int i = 0; i < parts.Count; i++)
        {
            BodyPartRecord part = parts[i];
            if (!def.apparel.CoversBodyPart(part))
            {
                continue;
            }

            coveredAbs[ClassifyPart(part)] += part.coverageAbs;
        }

        for (int c = 0; c < ChannelCount; c++)
        {
            coverage[c] = _channelTotalAbs[c] > 0f ? Mathf.Clamp01(coveredAbs[c] / _channelTotalAbs[c]) : 0f;
        }
    }

    // ---- airway/lung helpers (preserved from the original respiratory model) -------------------

    public static float GetApparelToxicResistance(Pawn pawn)
    {
        List<Apparel> worn = pawn?.apparel?.WornApparel;
        if (worn == null || worn.Count == 0)
        {
            return 0f;
        }

        float sum = 0f;
        for (int i = 0; i < worn.Count; i++)
        {
            List<StatModifier> offsets = worn[i]?.def?.equippedStatOffsets;
            if (offsets != null)
            {
                sum += offsets.GetStatOffsetFromList(StatDefOf.ToxicEnvironmentResistance);
            }
        }

        return sum;
    }

    private static float GetThingDefToxicResistance(ThingDef def)
    {
        List<StatModifier> offsets = def?.equippedStatOffsets;
        return offsets?.GetStatOffsetFromList(StatDefOf.ToxicEnvironmentResistance) ?? 0f;
    }

    public static float GetBodyPartToxicResistance(Pawn pawn)
    {
        List<Hediff> hediffs = pawn?.health?.hediffSet?.hediffs;
        if (hediffs == null)
        {
            return 0f;
        }

        float sum = 0f;
        for (int i = 0; i < hediffs.Count; i++)
        {
            Hediff hediff = hediffs[i];
            if (!IsBodyPartHediff(hediff))
            {
                continue;
            }

            List<StatModifier> offsets = hediff.CurStage?.statOffsets;
            if (offsets != null)
            {
                sum += offsets.GetStatOffsetFromList(StatDefOf.ToxicEnvironmentResistance);
            }
        }

        return sum;
    }

    private static bool IsBodyPartHediff(Hediff hediff)
    {
        return hediff?.def != null
            && (hediff is Hediff_AddedPart
                || hediff.def.countsAsAddedPartOrImplant
                || hediff.def.addedPartProps != null);
    }

    // ---- channel classification + init --------------------------------------------------------

    private static int ClassifyPart(BodyPartRecord part)
    {
        if (PartInAny(part, _handGroups))
        {
            return (int)ContagionChannel.Hands;
        }

        if (PartInAny(part, _footGroups))
        {
            return (int)ContagionChannel.Feet;
        }

        if (PartInAny(part, _headGroups))
        {
            return (int)ContagionChannel.Airway;
        }

        return (int)ContagionChannel.Skin;
    }

    private static bool PartInAny(BodyPartRecord part, BodyPartGroupDef[] groups)
    {
        if (part?.groups == null || groups == null)
        {
            return false;
        }

        for (int i = 0; i < groups.Length; i++)
        {
            if (groups[i] != null && part.groups.Contains(groups[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        _vacuumResistanceStat = StatDefOf.VacuumResistance;                       // null without Odyssey

        _headGroups = ResolveGroups("FullHead", "UpperHead", "Eyes", "Teeth", "Mouth", "Neck");
        _handGroups = ResolveGroups("Hands", "LeftHand", "RightHand", "MiddleFingers");
        _footGroups = ResolveGroups("Feet");

        BodyDef human = BodyDefOf.Human;
        if (human != null)
        {
            List<BodyPartRecord> parts = human.AllParts;
            for (int i = 0; i < parts.Count; i++)
            {
                _channelTotalAbs[ClassifyPart(parts[i])] += parts[i].coverageAbs;
            }
        }

        _initialized = true;
    }

    private static BodyPartGroupDef[] ResolveGroups(params string[] names)
    {
        List<BodyPartGroupDef> resolved = new List<BodyPartGroupDef>(names.Length);
        for (int i = 0; i < names.Length; i++)
        {
            BodyPartGroupDef def = DefDatabase<BodyPartGroupDef>.GetNamedSilentFail(names[i]);
            if (def != null)
            {
                resolved.Add(def);
            }
        }

        return resolved.ToArray();
    }
}
