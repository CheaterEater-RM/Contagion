using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

// Disease + vector data loaded straight from 1.6/Patches/Contagion_Profiles.xml, plus the vanilla
// immunity/severity race (from the disease's vanilla HediffDef, captured in docs/diseases). The live
// pawn-to-pawn simulator models Airborne/Proximity/Social; the environmental simulator models the
// source-less Vector_Environmental + Seeder_Environmental window path.

internal enum VectorKind
{
    Airborne,
    Proximity,
    Social,
}

internal sealed class LiveVector
{
    public VectorKind Kind;
    public float BaseChance;          // baseChancePerCheck (airborne/proximity) or baseChancePerInteraction (social)
    public float MaxRange;
    public float DistanceFalloffRate;
    public float OutdoorFactor = 1f;  // penalty applied when the contact happens outdoors / unroofed
    public float ObstructedFactor = 1f;
    public float RoomAirBaseChanceFactor;
    public float RoomAirMaxRange;
    public int RoomAirMaxCells;
    public float CleanlinessImpact;
    public float MaskSourceEffectiveness;
    public float MaskTargetEffectiveness;
}

internal enum EnvironmentalTargetKind
{
    Human,
    ColonyAnimal,
    WildAnimal,
}

internal enum EnvironmentalBudgetMode
{
    Profile,
    None,
    Fixed,
}

// When the pawn is OUTDOORS over the day. Off-hours are spent sheltered (roofed; ground-contact
// surface treated as a clean built/stone floor, shelter model uses --cells-from-edge depth).
internal enum EnvironmentalSchedule
{
    Always, // outdoors every hour (honours --indoor/--outdoor as before)
    Day,    // outdoors during work hours, sheltered at night (mountain sleeper)
    Night,  // outdoors at night, sheltered during the day (night-shift worker)
}

internal sealed class FloatRangeModel
{
    public float Min;
    public float Max;

    public float RandomInRange(Random rng) => Min + (Max - Min) * (float)rng.NextDouble();
}

internal sealed class EnvironmentalVectorModel
{
    public float BaseChancePerCheck;
    public float HumanExposureFactor = 1f;
    public float MinTemperature = 15f;
    public float PeakTemperature = 30f;
    public int WaterProximityRadius = 10;
    public float WaterProximityWeight = 0.02f;
    public float IndoorReductionPerCellFromEdge = 0.1f;
    public float CoolRoomThreshold = 18f;
    public GroundContactModel GroundContact; // non-null for soil-tracked parasites
    public Curve TimeOfDayActivityCurve;     // non-null for time-weighted vectors (mosquito/tsetse)
}

internal enum EnvironmentalSurface
{
    Dirt,
    Stone,
    Ice,
    Breeding,
}

internal sealed class GroundContactModel
{
    public float DirtFactor = 1f;
    public float StoneFactor = 0.25f;
    public float IceFactor = 0.05f;
    public float BreedingFactor = 2.5f;
    public float RoofedMultiplier = 0.3f;
}

internal sealed class EnvironmentalSeederModel
{
    public float BaseChanceMultiplier = 1f;
    public float WindowDays = 14f;
    public float ContagionWindowDays = -1f;
    public FloatRangeModel ColonyHumanBudgetFraction = new() { Min = 0.25f, Max = 0.75f };
    public FloatRangeModel ColonyAnimalBudgetFraction = new() { Min = 0.25f, Max = 0.75f };
    public float WildAnimalBudgetSqrtFactor = 1f;

    public float EffectiveWindowDays => ContagionWindowDays > 0f ? ContagionWindowDays : WindowDays;
}

internal sealed class DiseaseModel
{
    public string Name;
    public float IncubationDays;
    public float ImmunityPerDaySick;
    public float SeverityPerDayNotImmune;
    public float LethalSeverity = 1f;
    public float SpreadSuppressionScale = 1f;
    public float MaxActiveCaseChanceOffset;
    public Curve IncubationCurve;
    public Curve ActiveCurve;
    public List<LiveVector> Vectors = new();
    public EnvironmentalVectorModel EnvironmentalVector;
    public EnvironmentalSeederModel EnvironmentalSeeder;

    public static DiseaseModel Load(XDocument profiles, string disease, float incubationDays, float immunityPerDaySick, float severityPerDayNotImmune)
    {
        XElement profile = Xml.Profile(profiles, disease);
        DiseaseModel model = new()
        {
            Name = disease,
            IncubationDays = incubationDays,
            ImmunityPerDaySick = immunityPerDaySick,
            SeverityPerDayNotImmune = severityPerDayNotImmune,
            SpreadSuppressionScale = Xml.FieldOr(profile, "spreadSuppressionScale", 1f),
            MaxActiveCaseChanceOffset = Xml.FieldOr(profile, "maxActiveCaseChanceOffset", 0f),
            IncubationCurve = Curve.From(profile.Element("incubationInfectivityCurve")),
            ActiveCurve = Curve.From(profile.Element("activeInfectivityCurve")) ?? Curve.DefaultActive(),
        };

        model.LoadVectors(profile);
        model.LoadEnvironmentalSeeder(profile);

        return model;
    }

    public static DiseaseModel LoadEnvironmental(XDocument profiles, string disease)
    {
        XElement profile = Xml.Profile(profiles, disease);
        DiseaseModel model = new()
        {
            Name = disease,
            IncubationDays = Xml.FieldOr(profile, "incubationDays", 1f),
            SpreadSuppressionScale = Xml.FieldOr(profile, "spreadSuppressionScale", 1f),
            MaxActiveCaseChanceOffset = Xml.FieldOr(profile, "maxActiveCaseChanceOffset", 0f),
            IncubationCurve = Curve.From(profile.Element("incubationInfectivityCurve")),
            ActiveCurve = Curve.From(profile.Element("activeInfectivityCurve")) ?? Curve.DefaultActive(),
        };

        model.LoadVectors(profile);
        model.LoadEnvironmentalSeeder(profile);

        if (model.EnvironmentalVector == null || model.EnvironmentalSeeder == null)
        {
            throw new InvalidOperationException($"{disease} does not define both Vector_Environmental and Seeder_Environmental.");
        }

        return model;
    }

    private void LoadVectors(XElement profile)
    {
        XElement vectors = profile.Element("vectors");
        if (vectors != null)
        {
            foreach (XElement li in vectors.Elements("li"))
            {
                string cls = li.Attribute("Class")?.Value ?? string.Empty;
                LiveVector v = cls switch
                {
                    "Contagion.Vector_Airborne" => new LiveVector
                    {
                        Kind = VectorKind.Airborne,
                        BaseChance = Xml.Field(li, "baseChancePerCheck"),
                        MaxRange = Xml.Field(li, "maxRange"),
                        DistanceFalloffRate = Xml.Field(li, "distanceFalloffRate"),
                        OutdoorFactor = Xml.FieldOr(li, "outdoorFactor", 1f),
                        ObstructedFactor = Xml.FieldOr(li, "obstructedFactor", 1f),
                        RoomAirBaseChanceFactor = Xml.FieldOr(li, "roomAirBaseChanceFactor", 0f),
                        RoomAirMaxRange = Xml.FieldOr(li, "roomAirMaxRange", 0f),
                        RoomAirMaxCells = (int)Xml.FieldOr(li, "roomAirMaxCells", 0f),
                        MaskSourceEffectiveness = Xml.FieldOr(li, "maskSourceEffectiveness", 0f),
                        MaskTargetEffectiveness = Xml.FieldOr(li, "maskTargetEffectiveness", 0f),
                    },
                    "Contagion.Vector_Proximity" => new LiveVector
                    {
                        Kind = VectorKind.Proximity,
                        BaseChance = Xml.Field(li, "baseChancePerCheck"),
                        MaxRange = Xml.Field(li, "maxRange"),
                        DistanceFalloffRate = Xml.Field(li, "distanceFalloffRate"),
                        OutdoorFactor = Xml.FieldOr(li, "outdoorFactor", 1f),
                        CleanlinessImpact = Xml.FieldOr(li, "cleanlinessImpact", 0f),
                        MaskSourceEffectiveness = Xml.FieldOr(li, "maskSourceEffectiveness", 0f),
                        MaskTargetEffectiveness = Xml.FieldOr(li, "maskTargetEffectiveness", 0f),
                    },
                    "Contagion.Vector_Social" => new LiveVector
                    {
                        Kind = VectorKind.Social,
                        BaseChance = Xml.Field(li, "baseChancePerInteraction"),
                        OutdoorFactor = Xml.FieldOr(li, "outdoorFactor", 1f),
                        MaskSourceEffectiveness = Xml.FieldOr(li, "maskSourceEffectiveness", 0f),
                        MaskTargetEffectiveness = Xml.FieldOr(li, "maskTargetEffectiveness", 0f),
                    },
                    _ => null,
                };

                if (v != null)
                {
                    Vectors.Add(v);
                }
                else if (cls == "Contagion.Vector_Environmental")
                {
                    EnvironmentalVector = new EnvironmentalVectorModel
                    {
                        BaseChancePerCheck = Xml.FieldOr(li, "baseChancePerCheck", 0.02f),
                        HumanExposureFactor = Xml.FieldOr(li, "humanExposureFactor", 1f),
                        MinTemperature = Xml.FieldOr(li, "minTemperature", 15f),
                        PeakTemperature = Xml.FieldOr(li, "peakTemperature", 30f),
                        WaterProximityRadius = (int)Xml.FieldOr(li, "waterProximityRadius", 10f),
                        WaterProximityWeight = Xml.FieldOr(li, "waterProximityWeight", 0.02f),
                        IndoorReductionPerCellFromEdge = Xml.FieldOr(li, "indoorReductionPerCellFromEdge", 0.1f),
                        CoolRoomThreshold = Xml.FieldOr(li, "coolRoomThreshold", 18f),
                        GroundContact = LoadGroundContact(li.Element("groundContact")),
                        TimeOfDayActivityCurve = Curve.From(li.Element("timeOfDayActivityCurve")),
                    };
                }
            }
        }
    }

    private static GroundContactModel LoadGroundContact(XElement element)
    {
        if (element == null)
        {
            return null;
        }

        return new GroundContactModel
        {
            DirtFactor = Xml.FieldOr(element, "dirtFactor", 1f),
            StoneFactor = Xml.FieldOr(element, "stoneFactor", 0.25f),
            IceFactor = Xml.FieldOr(element, "iceFactor", 0.05f),
            BreedingFactor = Xml.FieldOr(element, "breedingFactor", 2.5f),
            RoofedMultiplier = Xml.FieldOr(element, "roofedMultiplier", 0.3f),
        };
    }

    private void LoadEnvironmentalSeeder(XElement profile)
    {
        XElement seeders = profile.Element("seeders");
        if (seeders == null)
        {
            return;
        }

        foreach (XElement li in seeders.Elements("li"))
        {
            string cls = li.Attribute("Class")?.Value ?? string.Empty;
            if (cls != "Contagion.Seeder_Environmental")
            {
                continue;
            }

            EnvironmentalSeeder = new EnvironmentalSeederModel
            {
                BaseChanceMultiplier = Xml.FieldOr(li, "baseChanceMultiplier", 1f),
                WindowDays = Xml.FieldOr(li, "windowDays", 14f),
                ContagionWindowDays = Xml.FieldOr(li, "contagionWindowDays", -1f),
                ColonyHumanBudgetFraction = Xml.RangeOr(li, "colonyHumanBudgetFraction", 0.25f, 0.75f),
                ColonyAnimalBudgetFraction = Xml.RangeOr(li, "colonyAnimalBudgetFraction", 0.25f, 0.75f),
                WildAnimalBudgetSqrtFactor = Xml.FieldOr(li, "wildAnimalBudgetSqrtFactor", 1f),
            };
            return;
        }
    }

    public bool HasVector(VectorKind kind) => Vectors.Any(v => v.Kind == kind);
}

// Linear-interpolated SimpleCurve, matching RimWorld's clamp-to-endpoints behavior. Mirrors the
// curve evaluation used in tools/ContagionRiskAudit so the two harnesses agree.
internal sealed class Curve
{
    private readonly List<(float x, float y)> _points;

    private Curve(List<(float x, float y)> points) => _points = points;

    public static Curve From(XElement curveElement)
    {
        XElement points = curveElement?.Element("points");
        if (points == null)
        {
            return null;
        }

        List<(float, float)> parsed = points.Elements("li")
            .Select(li =>
            {
                string[] parts = li.Value.Trim().Trim('(', ')').Split(',');
                return (
                    float.Parse(parts[0], CultureInfo.InvariantCulture),
                    float.Parse(parts[1], CultureInfo.InvariantCulture));
            })
            .OrderBy(p => p.Item1)
            .ToList();
        return parsed.Count == 0 ? null : new Curve(parsed);
    }

    // The mod's default active-infectivity bell (ContagionTransmissionUtility.DefaultActiveInfectivityCurve)
    // used when a profile omits activeInfectivityCurve. Both shipped live diseases author their own,
    // so this is only a safety net.
    public static Curve DefaultActive() => new(new List<(float, float)>
    {
        (0.00f, 0.3f), (0.15f, 0.7f), (0.35f, 1.0f), (0.65f, 1.0f), (0.85f, 0.3f), (1.00f, 0.0f),
    });

    public float Evaluate(float x)
    {
        if (x <= _points[0].x)
        {
            return _points[0].y;
        }

        if (x >= _points[_points.Count - 1].x)
        {
            return _points[_points.Count - 1].y;
        }

        for (int i = 1; i < _points.Count; i++)
        {
            if (x > _points[i].x)
            {
                continue;
            }

            (float x0, float y0) = _points[i - 1];
            (float x1, float y1) = _points[i];
            float t = (x - x0) / (x1 - x0);
            return y0 + (y1 - y0) * t;
        }

        return _points[_points.Count - 1].y;
    }
}

internal static class Xml
{
    public static XElement Profile(XDocument document, string disease)
    {
        string needle = $"defName=\"{disease}\"";
        foreach (XElement operation in document.Root.Elements("Operation"))
        {
            string xpath = operation.Element("xpath")?.Value ?? string.Empty;
            XElement li = operation.Element("value")?.Element("li");
            if (xpath.Contains(needle) && li?.Attribute("Class")?.Value == "Contagion.TransmissionProfile")
            {
                return li;
            }
        }

        throw new InvalidOperationException($"Could not find TransmissionProfile for {disease}.");
    }

    public static float Field(XElement element, string name)
    {
        XElement child = element.Element(name);
        if (child == null)
        {
            throw new InvalidOperationException($"Missing field {name}.");
        }

        return float.Parse(child.Value, CultureInfo.InvariantCulture);
    }

    public static float FieldOr(XElement element, string name, float fallback)
    {
        XElement child = element.Element(name);
        return child == null ? fallback : float.Parse(child.Value, CultureInfo.InvariantCulture);
    }

    public static FloatRangeModel RangeOr(XElement element, string name, float minFallback, float maxFallback)
    {
        XElement child = element.Element(name);
        if (child == null)
        {
            return new FloatRangeModel { Min = minFallback, Max = maxFallback };
        }

        string[] parts = child.Value.Split('~');
        if (parts.Length == 1)
        {
            float value = float.Parse(parts[0], CultureInfo.InvariantCulture);
            return new FloatRangeModel { Min = value, Max = value };
        }

        return new FloatRangeModel
        {
            Min = float.Parse(parts[0], CultureInfo.InvariantCulture),
            Max = float.Parse(parts[1], CultureInfo.InvariantCulture),
        };
    }
}
