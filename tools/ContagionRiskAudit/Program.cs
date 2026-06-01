using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Contagion;

internal static class Program
{
    private const int Skill = 10;

    private static readonly string Root = FindRepoRoot();

    private static int Main()
    {
        XDocument profiles = XDocument.Load(Path.Combine(Root, "1.6", "Patches", "Contagion_Profiles.xml"));
        XDocument recipes = XDocument.Load(Path.Combine(Root, "1.6", "Patches", "Contagion_RecipeExtensions.xml"));

        XElement plague = Profile(profiles, "Plague");
        XElement flu = Profile(profiles, "Flu");
        XElement animalFlu = Profile(profiles, "Animal_Flu");
        XElement gut = Profile(profiles, "GutWorms");
        XElement muscle = Profile(profiles, "MuscleParasites");

        XElement fluAirborne = Vector(flu, "Contagion.Vector_Airborne");
        XElement animalFluAirborne = Vector(animalFlu, "Contagion.Vector_Airborne");
        XElement plagueFlea = Vector(plague, "Contagion.Vector_CorpseFlea");
        XElement plagueFluid = Vector(plague, "Contagion.Vector_CorpseFluid");
        XElement plagueFood = Vector(plague, "Contagion.Vector_Foodborne");
        XElement plagueCooking = Vector(plague, "Contagion.Vector_CookingExposure");
        XElement gutFluid = Vector(gut, "Contagion.Vector_CorpseFluid");
        XElement gutFood = Vector(gut, "Contagion.Vector_Foodborne");
        XElement gutCooking = Vector(gut, "Contagion.Vector_CookingExposure");
        XElement muscleFluid = Vector(muscle, "Contagion.Vector_CorpseFluid");
        XElement muscleFood = Vector(muscle, "Contagion.Vector_Foodborne");
        XElement muscleCooking = Vector(muscle, "Contagion.Vector_CookingExposure");

        float butcherFactor = ContagionRiskMath.ButcheryExposureFactor(Skill, Skill, Skill, animalCorpse: true);
        float cookExposureFactor = ContagionRiskMath.CookingExposureFactor(Skill, 2f, 0.5f);
        float cookingSurvival = ContagionRiskMath.CookingSurvivalFactor(Skill, 1.5f, 0.25f, 0.18f);
        float ordinaryMealFactor = RecipeFactor(recipes, "CookMealSimple") * cookingSurvival;
        float survivalMealFactor = RecipeFactor(recipes, "CookMealSurvival") * cookingSurvival;
        float pemmicanFactor = RecipeFactor(recipes, "Make_Pemmican") * cookingSurvival;

        float plagueFreshFlea = Field(plagueFlea, "butcherBaseChance") * Curve(plagueFlea.Element("corpseAgePotencyCurve"), 0f) * butcherFactor;
        float plagueFreshFluid = Field(plagueFluid, "butcherChance") * Curve(plagueFluid.Element("corpseAgePotencyCurve"), 0f) * butcherFactor;

        List<Check> checks = new()
        {
            Check.Close("skill10 butchery factor", butcherFactor, 0.5875f, 0.0001f),
            Check.Close("skill10 cooked meal survival factor", ordinaryMealFactor, 0.091325f, 0.0005f),
            Check.Close("plague fresh butchery combined", ContagionRiskMath.Combined(plagueFreshFlea, plagueFreshFluid), 0.091929f, 0.002f),
            Check.Close("plague peak flea butchery", Field(plagueFlea, "butcherBaseChance") * Curve(plagueFlea.Element("corpseAgePotencyCurve"), 0.25f) * butcherFactor, 0.88125f, 0.005f),
            Check.Close("plague raw meat ingestion", Field(plagueFood, "baseChancePerMeal"), 0.25f, 0.001f),
            Check.Close("plague ordinary cooked meal ingestion", Field(plagueFood, "baseChancePerMeal") * ordinaryMealFactor, 0.022831f, 0.001f),
            Check.Close("gut worms raw meat ingestion", Field(gutFood, "baseChancePerMeal"), 0.80f, 0.001f),
            Check.Close("gut worms strong-stomach raw meat ingestion", ContagionRiskMath.ApplyIngestionResistance(Field(gutFood, "baseChancePerMeal"), 0.1f), 0.08f, 0.001f),
            Check.Close("gut worms bionic stomach raw meat ingestion", ContagionRiskMath.ApplyIngestionResistance(Field(gutFood, "baseChancePerMeal"), 0f), 0f, 0.001f),
            Check.Close("gut worms sterilizing stomach raw meat ingestion", ContagionRiskMath.ApplyIngestionResistance(Field(gutFood, "baseChancePerMeal"), 0f), 0f, 0.001f),
            Check.Close("gut worms ordinary cooked meal ingestion", Field(gutFood, "baseChancePerMeal") * ordinaryMealFactor, 0.073060f, 0.002f),
            Check.Close("gut worms strong-stomach ordinary cooked meal ingestion", ContagionRiskMath.ApplyIngestionResistance(Field(gutFood, "baseChancePerMeal") * ordinaryMealFactor, 0.1f), 0.007306f, 0.001f),
            Check.Close("gut worms bionic stomach ordinary cooked meal ingestion", ContagionRiskMath.ApplyIngestionResistance(Field(gutFood, "baseChancePerMeal") * ordinaryMealFactor, 0f), 0f, 0.001f),
            Check.Close("gut worms full cow ordinary meals", 1f - Pow(1f - Field(gutFood, "baseChancePerMeal") * ordinaryMealFactor, 33), 0.918209f, 0.01f),
            Check.Close("muscle parasites raw meat ingestion", Field(muscleFood, "baseChancePerMeal"), 0.85f, 0.001f),
            Check.Close("muscle parasites strong-stomach raw meat ingestion", ContagionRiskMath.ApplyIngestionResistance(Field(muscleFood, "baseChancePerMeal"), 0.1f), 0.085f, 0.001f),
            Check.Close("muscle parasites bionic stomach raw meat ingestion", ContagionRiskMath.ApplyIngestionResistance(Field(muscleFood, "baseChancePerMeal"), 0f), 0f, 0.001f),
            Check.Close("muscle parasites ordinary cooked meal ingestion", Field(muscleFood, "baseChancePerMeal") * ordinaryMealFactor, 0.077626f, 0.002f),
            // Eating a raw infected corpse now transmits via the foodborne vector's baseChancePerMeal
            // run through the ingester's stomach resistance (NotifyCorpseIngested →
            // ApplyTaintedFoodInfectionFactor), identical to eating raw contaminated meat — not a flat
            // 100%. Assert the post-resistance value (strong stomach, factor 0.1) so this exercises the
            // transformation rather than re-stating the raw baseChancePerMeal already checked above.
            Check.Close("raw infected corpse ingestion (strong stomach) applies resistance", ContagionRiskMath.ApplyIngestionResistance(Field(plagueFood, "baseChancePerMeal"), 0.1f), 0.025f, 0.001f),
            Check.Close("gut worms butchery exposure remains low", Field(gutFluid, "butcherChance") * Curve(gutFluid.Element("corpseAgePotencyCurve"), 2f) * butcherFactor, 0.001234f, 0.0005f),
            Check.Close("muscle parasites butchery exposure remains low", Field(muscleFluid, "butcherChance") * Curve(muscleFluid.Element("corpseAgePotencyCurve"), 2f) * butcherFactor, 0.001175f, 0.0005f),
            Check.Close("plague cooking exposure", Field(plagueCooking, "baseChancePerRecipe") * cookExposureFactor, 0.005f, 0.0005f),
            Check.Close("gut worms cooking exposure", Field(gutCooking, "baseChancePerRecipe") * cookExposureFactor, 0.00375f, 0.0005f),
            Check.Close("muscle parasites cooking exposure", Field(muscleCooking, "baseChancePerRecipe") * cookExposureFactor, 0.005f, 0.0005f),
            Check.Close("flu direct plume range", Field(fluAirborne, "maxRange"), 10f, 0.001f),
            Check.Close("flu room-air strength", Field(fluAirborne, "roomAirBaseChanceFactor"), 0.25f, 0.001f),
            Check.Close("flu room-air range", Field(fluAirborne, "roomAirMaxRange"), 10f, 0.001f),
            Check.Close("flu room-air max cells", Field(fluAirborne, "roomAirMaxCells"), 100f, 0.001f),
            Check.Close("animal flu direct plume range", Field(animalFluAirborne, "maxRange"), 10f, 0.001f),
            Check.Close("animal flu room-air strength", Field(animalFluAirborne, "roomAirBaseChanceFactor"), 0.25f, 0.001f),
            Check.Close("animal flu room-air range", Field(animalFluAirborne, "roomAirMaxRange"), 10f, 0.001f),
            Check.Close("animal flu room-air max cells", Field(animalFluAirborne, "roomAirMaxCells"), 100f, 0.001f),
            Check.Bool("survival meal stays safer than ordinary", survivalMealFactor < ordinaryMealFactor, true),
            Check.Bool("pemmican stays risky", pemmicanFactor > ordinaryMealFactor, true),
        };

        // Guard against regressing to the old flat 100% corpse-ingestion chance: the code must
        // derive the base chance from the foodborne vector's baseChancePerMeal, not a hardcoded 1f.
        string corpseUtility = File.ReadAllText(Path.Combine(Root, "Source", "Core", "ContagionCorpseUtility.cs"));
        checks.Add(Check.Bool("corpse ingestion uses foodborne baseChancePerMeal", corpseUtility.Contains("foodborneVector.baseChancePerMeal"), true));
        checks.Add(Check.Bool("corpse ingestion no longer hardcodes 1f base", !corpseUtility.Contains("BuildSeederChance(\r\n            1f,") && !corpseUtility.Contains("BuildSeederChance(\n            1f,"), true));
        // Eating a raw corpse must also roll direct flea + fluid contact (butcher-level), not just
        // the foodborne meal — raw ingestion is the highest-contact corpse interaction.
        checks.Add(Check.Bool("corpse ingestion rolls fluid contact", corpseUtility.Contains("TryApplyFluidExposure"), true));
        checks.Add(Check.Bool("corpse ingestion rolls flea contact", corpseUtility.Contains("TryApplyFleaExposure"), true));
        string transmissionUtility = File.ReadAllText(Path.Combine(Root, "Source", "Core", "ContagionTransmissionUtility.cs"));
        string corpseExposureProcessor = File.ReadAllText(Path.Combine(Root, "Source", "Core", "Transmission", "ContagionCorpseExposureProcessor.cs"));
        string pawnTransmissionProcessor = File.ReadAllText(Path.Combine(Root, "Source", "Core", "Transmission", "ContagionPawnTransmissionProcessor.cs"));
        checks.Add(Check.Bool("shared path-walk helper exists", transmissionUtility.Contains("CollectReachablePathDistances"), true));
        checks.Add(Check.Bool("corpse fleas use path-walk distances", corpseExposureProcessor.Contains("CollectReachablePathDistances"), true));
        checks.Add(Check.Bool("live proximity uses path-walk distances", pawnTransmissionProcessor.Contains("CollectReachablePathDistances"), true));

        int failures = 0;
        foreach (Check check in checks)
        {
            if (!check.Print())
            {
                failures++;
            }
        }

        if (failures > 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"{failures} infection risk audit check(s) failed.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("All infection risk audit checks passed.");
        return 0;
    }

    private static XElement Profile(XDocument document, string disease)
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

    private static XElement Vector(XElement profile, string className)
    {
        XElement vector = profile.Element("vectors")?
            .Elements("li")
            .FirstOrDefault(li => li.Attribute("Class")?.Value == className);
        return vector ?? throw new InvalidOperationException($"Missing vector {className}.");
    }

    private static float Field(XElement element, string name)
    {
        XElement child = element.Element(name);
        if (child == null)
        {
            throw new InvalidOperationException($"Missing field {name}.");
        }

        return float.Parse(child.Value, CultureInfo.InvariantCulture);
    }

    private static float Curve(XElement curve, float x)
    {
        List<(float x, float y)> points = curve.Element("points")
            .Elements("li")
            .Select(li =>
            {
                string[] parts = li.Value.Trim().Trim('(', ')').Split(',');
                return (
                    float.Parse(parts[0], CultureInfo.InvariantCulture),
                    float.Parse(parts[1], CultureInfo.InvariantCulture));
            })
            .OrderBy(point => point.Item1)
            .ToList();

        if (x <= points[0].x)
        {
            return points[0].y;
        }

        if (x >= points[points.Count - 1].x)
        {
            return points[points.Count - 1].y;
        }

        for (int i = 1; i < points.Count; i++)
        {
            if (x > points[i].x)
            {
                continue;
            }

            (float x0, float y0) = points[i - 1];
            (float x1, float y1) = points[i];
            float t = (x - x0) / (x1 - x0);
            return y0 + (y1 - y0) * t;
        }

        throw new InvalidOperationException("Curve interpolation failed.");
    }

    private static float RecipeFactor(XDocument document, string recipeDef)
    {
        string needle = $"defName=\"{recipeDef}\"";
        foreach (XElement operation in document.Root.Elements("Operation"))
        {
            string xpath = operation.Element("xpath")?.Value ?? string.Empty;
            if (!xpath.Contains(needle) || !xpath.Contains("CookingContaminationExtension"))
            {
                continue;
            }

            XElement li = operation.Element("value")?.Element("li");
            if (li != null)
            {
                return Field(li, "reductionFactor");
            }
        }

        throw new InvalidOperationException($"Missing cooking extension for {recipeDef}.");
    }

    private static float Pow(float value, int exponent)
    {
        return (float)Math.Pow(value, exponent);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo directory = new(AppDomain.CurrentDomain.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Source", "Contagion.csproj"))
                && File.Exists(Path.Combine(directory.FullName, "1.6", "Patches", "Contagion_Profiles.xml")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find Contagion repo root from audit output directory.");
    }

    private sealed class Check
    {
        private readonly string _name;
        private readonly float _actual;
        private readonly float _expected;
        private readonly float _tolerance;
        private readonly bool? _actualBool;
        private readonly bool? _expectedBool;

        private Check(string name, float actual, float expected, float tolerance)
        {
            _name = name;
            _actual = actual;
            _expected = expected;
            _tolerance = tolerance;
        }

        private Check(string name, bool actual, bool expected)
        {
            _name = name;
            _actualBool = actual;
            _expectedBool = expected;
        }

        public static Check Close(string name, float actual, float expected, float tolerance)
        {
            return new Check(name, actual, expected, tolerance);
        }

        public static Check Bool(string name, bool actual, bool expected)
        {
            return new Check(name, actual, expected);
        }

        public bool Print()
        {
            bool ok;
            string details;
            if (_actualBool.HasValue)
            {
                ok = _actualBool.Value == _expectedBool.Value;
                details = $"actual={_actualBool.Value} expected={_expectedBool.Value} tol=exact";
            }
            else
            {
                ok = Math.Abs(_actual - _expected) <= _tolerance;
                details = $"actual={_actual:F6} expected={_expected:F6} tol={_tolerance:F6}";
            }

            Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {_name}: {details}");
            return ok;
        }
    }
}
