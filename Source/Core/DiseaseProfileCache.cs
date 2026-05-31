using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Contagion;

public sealed class ResolvedTransmissionProfile
{
    public ResolvedTransmissionProfile(
        HediffDef diseaseDef,
        TransmissionProfile profile,
        IncidentDef linkedIncidentDef,
        List<BodyPartDef> partsToAffect,
        bool usesFallbackParts)
    {
        DiseaseDef = diseaseDef;
        Profile = profile;
        LinkedIncidentDef = linkedIncidentDef;
        PartsToAffect = partsToAffect;
        UsesFallbackParts = usesFallbackParts;
    }

    public HediffDef DiseaseDef { get; }

    public TransmissionProfile Profile { get; }

    public IncidentDef LinkedIncidentDef { get; }

    public List<BodyPartDef> PartsToAffect { get; }

    public bool UsesFallbackParts { get; }

    public bool HasResolvedParts => !PartsToAffect.NullOrEmpty();

    // Returns the hediff that should be applied to this specific pawn.
    // Non-humanlike targets of a profile that declares animalVariantDef receive the variant
    // (e.g. Animal_Plague) rather than the primary (e.g. Plague) so each species gets
    // its own tuned tend cycle and stats while sharing all transmission logic.
    public HediffDef ResolveHediffForPawn(Pawn pawn)
    {
        HediffDef variant = Profile.animalVariantDef;
        return variant != null && pawn != null && !pawn.RaceProps.Humanlike ? variant : DiseaseDef;
    }
}

public static class DiseaseProfileCache
{
    private static Dictionary<HediffDef, ResolvedTransmissionProfile> _profilesByDisease;

    private static bool _initialized;

    public static IEnumerable<ResolvedTransmissionProfile> AllProfiles
    {
        get
        {
            EnsureInitialized();
            return _profilesByDisease.Values.Distinct();
        }
    }

    public static void Reset()
    {
        _profilesByDisease = null;
        _initialized = false;
    }

    public static TransmissionProfile GetProfile(HediffDef diseaseDef)
    {
        return TryGetResolvedProfile(diseaseDef, out ResolvedTransmissionProfile resolvedProfile)
            ? resolvedProfile.Profile
            : null;
    }

    public static bool TryGetResolvedProfile(HediffDef diseaseDef, out ResolvedTransmissionProfile resolvedProfile)
    {
        EnsureInitialized();

        if (diseaseDef == null)
        {
            resolvedProfile = null;
            return false;
        }

        return _profilesByDisease.TryGetValue(diseaseDef, out resolvedProfile);
    }

    private static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        BuildCache();
        _initialized = true;
    }

    private static void BuildCache()
    {
        _profilesByDisease = new Dictionary<HediffDef, ResolvedTransmissionProfile>();

        foreach (HediffDef diseaseDef in DefDatabase<HediffDef>.AllDefsListForReading)
        {
            TransmissionProfile profile = diseaseDef.GetModExtension<TransmissionProfile>();
            if (profile == null)
            {
                continue;
            }

            IncidentDef linkedIncidentDef = FindLinkedIncident(diseaseDef);
            bool usesFallbackParts;
            List<BodyPartDef> resolvedParts = ResolvePartsToAffect(profile, linkedIncidentDef, out usesFallbackParts);

            if (profile.UsesPartTargeting && resolvedParts.NullOrEmpty())
            {
                Log.Warning($"[Contagion] TransmissionProfile on {diseaseDef.defName} requires part metadata, but none could be resolved.");
            }

            if (Prefs.DevMode)
            {
                WarnIfReservedFieldsAreSet(diseaseDef, profile);
            }

            _profilesByDisease[diseaseDef] = new ResolvedTransmissionProfile(
                diseaseDef,
                profile,
                linkedIncidentDef,
                resolvedParts,
                usesFallbackParts);
        }

        // Register animal variant hediffs so animals carrying the variant are recognised as
        // carriers of the primary profile. After this, TryGetResolvedProfile(Animal_Plague)
        // returns the same ResolvedTransmissionProfile as TryGetResolvedProfile(Plague).
        var variantMappings = new List<(HediffDef variant, ResolvedTransmissionProfile profile)>();
        foreach (KeyValuePair<HediffDef, ResolvedTransmissionProfile> kvp in _profilesByDisease)
        {
            if (kvp.Value.Profile.animalVariantDef != null)
            {
                variantMappings.Add((kvp.Value.Profile.animalVariantDef, kvp.Value));
            }
        }
        foreach ((HediffDef variant, ResolvedTransmissionProfile profile) in variantMappings)
        {
            if (!_profilesByDisease.ContainsKey(variant))
            {
                _profilesByDisease[variant] = profile;
            }
        }

        if (Prefs.DevMode)
        {
            Log.Message($"[Contagion] Cached {_profilesByDisease.Count} transmission profiles.");
        }
    }

    private static IncidentDef FindLinkedIncident(HediffDef diseaseDef)
    {
        return DefDatabase<IncidentDef>.AllDefsListForReading.FirstOrDefault(incidentDef => incidentDef.diseaseIncident == diseaseDef);
    }

    private static List<BodyPartDef> ResolvePartsToAffect(
        TransmissionProfile profile,
        IncidentDef linkedIncidentDef,
        out bool usesFallbackParts)
    {
        usesFallbackParts = false;

        if (!profile.targetBodyParts.NullOrEmpty())
        {
            return new List<BodyPartDef>(profile.targetBodyParts);
        }

        if (linkedIncidentDef?.diseasePartsToAffect.NullOrEmpty() == false)
        {
            usesFallbackParts = true;
            return new List<BodyPartDef>(linkedIncidentDef.diseasePartsToAffect);
        }

        return null;
    }

    private static void WarnIfReservedFieldsAreSet(HediffDef diseaseDef, TransmissionProfile profile)
    {
        if (profile.carrierChance != 0f)
        {
            Log.Warning($"[Contagion] TransmissionProfile on {diseaseDef.defName} sets reserved field carrierChance; it is not implemented yet.");
        }

        if (profile.carrierHediffDef != null)
        {
            Log.Warning($"[Contagion] TransmissionProfile on {diseaseDef.defName} sets reserved field carrierHediffDef; it is not implemented yet.");
        }

        if (profile.spreadsDuringCaravan)
        {
            Log.Warning($"[Contagion] TransmissionProfile on {diseaseDef.defName} sets reserved field spreadsDuringCaravan; it is not implemented yet.");
        }

        if (profile.corpseInfectivityDecayPerDay != 0.5f)
        {
            Log.Warning($"[Contagion] TransmissionProfile on {diseaseDef.defName} sets reserved field corpseInfectivityDecayPerDay; it is not implemented yet.");
        }
    }
}
