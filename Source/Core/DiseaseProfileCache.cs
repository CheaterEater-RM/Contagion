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
            return _profilesByDisease.Values;
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

            _profilesByDisease[diseaseDef] = new ResolvedTransmissionProfile(
                diseaseDef,
                profile,
                linkedIncidentDef,
                resolvedParts,
                usesFallbackParts);
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
}
