using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

// Disease + vector data loaded straight from 1.6/Patches/Contagion_Profiles.xml, plus the vanilla
// immunity/severity race (from the disease's vanilla HediffDef, captured in docs/diseases). Only the
// LIVE pawn-to-pawn vectors are modeled (Airborne, Proximity, Social); corpse/food/fecal-oral are
// out of scope for this harness.

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
                    model.Vectors.Add(v);
                }
            }
        }

        return model;
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
}
