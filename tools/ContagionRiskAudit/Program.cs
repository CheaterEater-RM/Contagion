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
        XElement gutEating = Vector(gut, "Contagion.Vector_FecalOralEating");
        XElement muscleEating = Vector(muscle, "Contagion.Vector_FecalOralEating");

        // Cow-anchored fecal-oral grazing: a full-potency node (peak active infectivity 1.0) made by a
        // cow (BodySize 2.4) grazed point-blank (distance 0 -> factor 1) on a clean map (seeder factor 1)
        // should read ~60%. Larger grazers are capped at the cow by MaxBodySizePotencyFactor (2.5, a C#
        // const mirrored here and guarded by a source check below).
        const float CowBodySize = 2.4f;
        const float MaxBodySizePotencyFactor = 2.5f;

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
            Check.Close("biome disease commonality below baseline", ContagionRiskMath.BiomeDiseaseCommonalityFactor(50f), 0.5f, 0.0001f),
            Check.Close("biome disease commonality baseline", ContagionRiskMath.BiomeDiseaseCommonalityFactor(100f), 1f, 0.0001f),
            Check.Close("biome disease commonality above baseline", ContagionRiskMath.BiomeDiseaseCommonalityFactor(160f), 1.6f, 0.0001f),
            Check.Close("biome disease commonality clamps negatives", ContagionRiskMath.BiomeDiseaseCommonalityFactor(-10f), 0f, 0.0001f),
            Check.Close("seeder bonus leaves zero at zero", ContagionRiskMath.SeederBonusChance(0f), 0f, 0.0001f),
            Check.Close("seeder bonus leaves one at one", ContagionRiskMath.SeederBonusChance(1f), 1f, 0.0001f),
            Check.Close("seeder bonus cuberoots tiny chance", ContagionRiskMath.SeederBonusChance(0.001f), 0.1f, 0.0001f),
            Check.Close("seeder bonus cuberoots low chance", ContagionRiskMath.SeederBonusChance(0.027f), 0.3f, 0.0001f),
            Check.Close("active-case capacity floors small groups", ContagionRiskMath.ActiveCaseCapacity(1, 0f), 1f, 0.0001f),
            Check.Close("active-case capacity mid-size group", ContagionRiskMath.ActiveCaseCapacity(10, 0f), 4f, 0.0001f),
            Check.Close("active-case capacity approaches fifty percent", ContagionRiskMath.ActiveCaseCapacity(20, 0f), 10f, 0.0001f),
            // Suppression curves retuned against tools/ContagionSpreadSim so the default Medium mode
            // holds the active-case ceiling near the cap (240 passes/day compound a small leak).
            // Medium: engage 0.75, floor 0.02 at/above stop load 1.05. Weak: softer (engage 0.90,
            // floor 0.08 at/above 1.40). Strong: unchanged true cap (floor 0 at load >= 1.0).
            Check.Close("medium suppression full below engage load",
                ContagionRiskMath.SpreadSuppressionFactor(ContagionSuppressionMode.Medium, 0.75f), 1f, 0.0001f),
            Check.Close("medium suppression engages before ninety percent load",
                ContagionRiskMath.SpreadSuppressionFactor(ContagionSuppressionMode.Medium, 0.90f), 0.51f, 0.001f),
            Check.Close("medium suppression floors near zero at capacity",
                ContagionRiskMath.SpreadSuppressionFactor(ContagionSuppressionMode.Medium, 1.05f), 0.02f, 0.0001f),
            Check.Close("strong suppression starts at fifty percent load",
                ContagionRiskMath.SpreadSuppressionFactor(ContagionSuppressionMode.Strong, 0.50f), 1f, 0.0001f),
            Check.Close("strong suppression is a true cap at capacity",
                ContagionRiskMath.SpreadSuppressionFactor(ContagionSuppressionMode.Strong, 1.00f), 0f, 0.0001f),
            Check.Close("weak suppression stays full below engage load",
                ContagionRiskMath.SpreadSuppressionFactor(ContagionSuppressionMode.Weak, 0.90f), 1f, 0.0001f),
            Check.Close("weak suppression keeps a softer leaky floor",
                ContagionRiskMath.SpreadSuppressionFactor(ContagionSuppressionMode.Weak, 1.40f), 0.08f, 0.0001f),
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
            Check.Close("gut worms full cow point-blank grazing", GrazingPointBlank(gutEating, CowBodySize, MaxBodySizePotencyFactor), 0.60f, 0.005f),
            Check.Close("muscle parasites full cow point-blank grazing", GrazingPointBlank(muscleEating, CowBodySize, MaxBodySizePotencyFactor), 0.60f, 0.005f),
            Check.Close("gut worms large grazer capped at cow", GrazingPointBlank(gutEating, 4.0f, MaxBodySizePotencyFactor), 0.625f, 0.005f),
            // Tight point-blank shape: chance halves per cell (distanceFalloffRate = ln 2) and the node
            // reaches only two tiles. A full cow node reads ~60% on the tile, ~30% one cell out.
            Check.Close("gut worms grazing halves per cell", (float)Math.Exp(-Field(gutEating, "distanceFalloffRate")), 0.5f, 0.01f),
            Check.Close("gut worms full cow grazing one cell out", GrazingPointBlank(gutEating, CowBodySize, MaxBodySizePotencyFactor) * (float)Math.Exp(-Field(gutEating, "distanceFalloffRate")), 0.30f, 0.005f),
            Check.Close("gut worms grazing range capped at two tiles", Field(gutEating, "hotspotRadius"), 2f, 0.001f),
            Check.Close("gut worms node halves per day", Field(gutEating, "hotspotDecayPerDay"), 0.5f, 0.001f),
            Check.Close("gut worms grazing node hard-expiry backstop", Field(gutEating, "hotspotDurationDays"), 5f, 0.001f),
            Check.Close("fecal-oral node decay executes as fraction-per-day",
                ContagionRiskMath.HotspotPotencyAfterDecay(2.4f, 0.5f, 1f), 1.2f, 0.0001f),
            Check.Close("fecal-oral repeat shed decays then adds",
                ContagionRiskMath.AddHotspotPotency(
                    ContagionRiskMath.HotspotPotencyAfterDecay(2.4f, 0.5f, 1f), 0.2f, 4f), 1.4f, 0.0001f),
            Check.Close("fecal-oral stacked node respects potency cap",
                ContagionRiskMath.AddHotspotPotency(4f, 2.4f, 4f), 4f, 0.0001f),
            Check.Bool("temporary rain suppresses but does not clean up viable node",
                ContagionRiskMath.HotspotEffectivePotency(0.2f, true, 0.35f, false, 1f) < 0.08f
                && !ContagionRiskMath.ShouldCleanupHotspot(0.2f, 0.5f, 0f, 0.08f), true),
            Check.Close("hidden eating risk does not control visible overlay scale",
                ContagionRiskMath.VisibleChanceMaximum(
                    ContagionRiskMath.VisibleChanceMaximum(0f, 0.2f, true, 0.00005f),
                    0.9f,
                    false,
                    0.00005f),
                0.2f,
                0.0001f),
            // Natural ×0.5/day decay must drop a full cow node below the cleanup floor (0.08 potency =
            // ~2% point-blank chance at base 0.25) by the hard-cap day, so decay does the real removal
            // and nothing lingers past the backstop.
            Check.Bool("cow grazing node self-expires by the hard cap",
                ContagionRiskMath.ShouldCleanupHotspot(CowBodySize, Field(gutEating, "hotspotDecayPerDay"), Field(gutEating, "hotspotDurationDays"), 0.08f), true),
            Check.Close("muscle parasites grazing halves per cell", (float)Math.Exp(-Field(muscleEating, "distanceFalloffRate")), 0.5f, 0.01f),
            Check.Close("muscle parasites grazing range capped at two tiles", Field(muscleEating, "hotspotRadius"), 2f, 0.001f),
            Check.Close("muscle parasites node halves per day", Field(muscleEating, "hotspotDecayPerDay"), 0.5f, 0.001f),
            Check.Close("muscle parasites grazing node hard-expiry backstop", Field(muscleEating, "hotspotDurationDays"), 5f, 0.001f),
            // Weather off: rain/freezing no longer suppress fecal-oral nodes (factors 1.0).
            Check.Close("gut worms grazing ignores rain", Field(gutEating, "rainPotencyFactor"), 1f, 0.001f),
            Check.Close("gut worms grazing ignores freezing", Field(gutEating, "freezingPotencyFactor"), 1f, 0.001f),
            Check.Close("muscle parasites grazing ignores rain", Field(muscleEating, "rainPotencyFactor"), 1f, 0.001f),
            Check.Close("muscle parasites grazing ignores freezing", Field(muscleEating, "freezingPotencyFactor"), 1f, 0.001f),
            // Eating route ramps fast: active disease sheds near-full from the start (override at 0.8),
            // incubating carriers shed moderately (override at 0.3) so even fresh nodes can transmit.
            Check.Close("gut worms eating active infectivity ramps high", Curve(Child(gutEating, "activeInfectivityCurveOverride"), 0f), 0.8f, 0.001f),
            Check.Close("gut worms eating incubation infectivity is moderate", Curve(Child(gutEating, "incubationInfectivityCurveOverride"), 0f), 0.3f, 0.001f),
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
        string infectedCorpseComp = File.ReadAllText(Path.Combine(Root, "Source", "Comps", "Comp_InfectedCorpse.cs"));
        string corpseInspectableComp = File.ReadAllText(Path.Combine(Root, "Source", "Comps", "Comp_CorpseInspectable.cs"));
        string inspectCorpseProvider = File.ReadAllText(Path.Combine(Root, "Source", "Jobs", "FloatMenuOptionProvider_InspectCorpse.cs"));
        checks.Add(Check.Bool("corpse ingestion uses foodborne baseChancePerMeal", corpseUtility.Contains("foodborneVector.baseChancePerMeal"), true));
        checks.Add(Check.Bool("corpse ingestion no longer hardcodes 1f base", !corpseUtility.Contains("BuildSeederChance(\r\n            1f,") && !corpseUtility.Contains("BuildSeederChance(\n            1f,"), true));
        // Eating a raw corpse must also roll direct flea + fluid contact (butcher-level), not just
        // the foodborne meal — raw ingestion is the highest-contact corpse interaction.
        checks.Add(Check.Bool("corpse ingestion rolls fluid contact", corpseUtility.Contains("TryApplyFluidExposure"), true));
        checks.Add(Check.Bool("corpse ingestion rolls flea contact", corpseUtility.Contains("TryApplyFleaExposure"), true));
        checks.Add(Check.Bool("corpse inspection is final after one attempt",
            corpseUtility.Contains("if (comp == null || comp.HasBeenInspected || comp.DiseaseIdentified)"), true));
        checks.Add(Check.Bool("failed corpse inspection clears visible suspicion",
            corpseUtility.Contains("Corpse inspection false negative")
            && !corpseUtility.Contains("MarkInspectedFailed")
            && !infectedCorpseComp.Contains("MarkInspectedFailed"), true));
        // Corpse inspection runs on the vanilla interaction job (JobDriver_InteractThing) so a save
        // made mid-inspection survives Contagion's removal (no custom job/driver class in the save).
        // The Contagion-specific diagnosis runs in Comp_CorpseInspectable.OnInteracted, and the
        // float-menu issues the vanilla job.
        checks.Add(Check.Bool("corpse inspection runs in the interaction comp callback",
            corpseInspectableComp.Contains("OnInteracted")
            && corpseInspectableComp.Contains("ContagionCorpseUtility.TryInspectCorpse"), true));
        checks.Add(Check.Bool("corpse inspection uses the vanilla InteractThing job (removal-safe)",
            inspectCorpseProvider.Contains("JobDefOf.InteractThing"), true));
        string transmissionUtility = File.ReadAllText(Path.Combine(Root, "Source", "Core", "ContagionTransmissionUtility.cs"));
        string seedingCoordinator = File.ReadAllText(Path.Combine(Root, "Source", "Core", "Seeding", "ContagionSeedingCoordinator.cs"));
        string mapTransmissionComponent = File.ReadAllText(Path.Combine(Root, "Source", "Core", "Contagion_MapTransmissionComponent.cs"));
        string staggerUtility = File.ReadAllText(Path.Combine(Root, "Source", "Core", "Transmission", "ContagionTransmissionStaggerUtility.cs"));
        string corpseExposureProcessor = File.ReadAllText(Path.Combine(Root, "Source", "Core", "Transmission", "ContagionCorpseExposureProcessor.cs"));
        string pawnTransmissionProcessor = File.ReadAllText(Path.Combine(Root, "Source", "Core", "Transmission", "ContagionPawnTransmissionProcessor.cs"));
        string fomiteTrackerForStagger = File.ReadAllText(Path.Combine(Root, "Source", "Core", "Transmission", "ContagionVomitFomiteTracker.cs"));
        string fecalOralTrackerForStagger = File.ReadAllText(Path.Combine(Root, "Source", "Core", "Transmission", "ContagionFecalOralTracker.cs"));
        checks.Add(Check.Bool("shared path-walk helper exists", transmissionUtility.Contains("CollectReachablePathDistances"), true));
        checks.Add(Check.Bool("corpse fleas use path-walk distances", corpseExposureProcessor.Contains("CollectReachablePathDistances"), true));
        checks.Add(Check.Bool("live proximity uses path-walk distances", pawnTransmissionProcessor.Contains("CollectReachablePathDistances"), true));
        checks.Add(Check.Bool("live transmission no longer uses full-interval modulo gate",
            !mapTransmissionComponent.Contains("bool runTransmission = ticksGame % TransmissionCheckInterval == 0")
            && !mapTransmissionComponent.Contains("if (!runTransmission)"), true));
        checks.Add(Check.Bool("transmission staggering uses map and thing stable buckets",
            staggerUtility.Contains("map.uniqueID")
            && staggerUtility.Contains("thing.thingIDNumber")
            && staggerUtility.Contains("% intervalTicks"), true));
        checks.Add(Check.Bool("pawn-to-pawn spread is source-bucket staggered",
            mapTransmissionComponent.Contains("BuildDuePawnList(spawnedPawns, bucket)")
            && mapTransmissionComponent.Contains("_pawnTransmissionProcessor.RunPawnTransmissionPass(spawnedPawns, _transmissionDuePawns)")
            && pawnTransmissionProcessor.Contains("RunPawnTransmissionPass(IReadOnlyList<Pawn> spawnedPawns, IReadOnlyList<Pawn> sourcePawns)")
            && pawnTransmissionProcessor.Contains("List<TransmissionSource> sources = GatherTransmissionSources(sourcePawns)"), true));
        checks.Add(Check.Bool("fomite spread is target-bucket staggered",
            mapTransmissionComponent.Contains("_vomitFomiteTracker.RunFomiteExposurePass(_transmissionDuePawns, map)")
            && fomiteTrackerForStagger.Contains("RunFomiteExposurePass(IReadOnlyList<Pawn> spawnedPawns, Map map)"), true));
        checks.Add(Check.Bool("fecal-oral living spread is target-bucket staggered",
            mapTransmissionComponent.Contains("_fecalOralTracker.RunFecalOralLivingExposurePass(_transmissionDuePawns, map)")
            && fecalOralTrackerForStagger.Contains("RunFecalOralLivingExposurePass(IReadOnlyList<Pawn> spawnedPawns, Map map)"), true));
        checks.Add(Check.Bool("corpse exposure is source-bucket staggered with full delta",
            corpseExposureProcessor.Contains("RunCorpseExposurePass(IReadOnlyList<Pawn> spawnedPawns, IReadOnlyList<Pawn> duePawns, int deltaTicks, int bucket)")
            && corpseExposureProcessor.Contains("ContagionTransmissionStaggerUtility.IsDueThisTick(corpse, _map, deltaTicks, bucket)")
            && corpseExposureProcessor.Contains("ProcessCarriedCorpses(spawnedPawns, duePawns, deltaTicks)")
            && mapTransmissionComponent.Contains("_corpseExposureProcessor.RunCorpseExposurePass(spawnedPawns, _transmissionDuePawns, TransmissionCheckInterval, bucket)"), true));
        checks.Add(Check.Bool("lord-group suppression resolves through pawn lord",
            transmissionUtility.Contains("TryResolveSuppressionScope")
            && transmissionUtility.Contains("pawn.GetLord()")
            && transmissionUtility.Contains("lord.ownedPawns"), true));
        checks.Add(Check.Bool("lord-group suppression caches by lord load id",
            transmissionUtility.Contains("_activeLordGroupCaseCountCache")
            && transmissionUtility.Contains("_affectableLordGroupPopulationCache")
            && transmissionUtility.Contains("lord.loadID"), true));
        int lordScopeIndex = transmissionUtility.IndexOf("Lord lord = pawn.GetLord();", StringComparison.Ordinal);
        int wildScopeIndex = transmissionUtility.IndexOf("GetCaseTrack(pawn) == ContagionCaseTrack.WildAnimal", StringComparison.Ordinal);
        checks.Add(Check.Bool("lord group scope is chosen before wild-animal scope",
            lordScopeIndex >= 0 && wildScopeIndex > lordScopeIndex, true));
        checks.Add(Check.Bool("arrival carrier capacity uses arriving group pawns",
            seedingCoordinator.Contains("GetRemainingActiveCaseCapacity(component, resolvedProfile, arrivalSeeder, groupPawns)")
            && seedingCoordinator.Contains("GetRemainingActiveCaseCapacityForGroup(groupPawns, resolvedProfile)"), true));

        // ---- Apparel protection (docs/Apparel_Protection_Design.md) -------------------------------
        // Seal integrity bands (design §7) keyed to the vanilla ratty (<50%) / tattered (<20%) states.
        checks.Add(Check.Close("seal integrity normal HP", ContagionRiskMath.SealIntegrity(0.60f), 1.00f, 0.0001f));
        checks.Add(Check.Close("seal integrity ratty", ContagionRiskMath.SealIntegrity(0.35f), 0.85f, 0.0001f));
        checks.Add(Check.Close("seal integrity tattered", ContagionRiskMath.SealIntegrity(0.10f), 0.55f, 0.0001f));

        // Channel protection formula (design §3.2): full seal => full coverage; unsealed => floor; naked => 0.
        checks.Add(Check.Close("channel full seal is full", ContagionRiskMath.ChannelProtection(1f, 1f, 0.20f), 1.00f, 0.0001f));
        checks.Add(Check.Close("channel unsealed rides floor", ContagionRiskMath.ChannelProtection(1f, 0f, 0.20f), 0.20f, 0.0001f));
        checks.Add(Check.Close("channel naked is zero", ContagionRiskMath.ChannelProtection(0f, 1f, 0.50f), 0.00f, 0.0001f));

        // Authored per-vector floors (unsealedEffectiveness) match the design: flea barely helps unsealed,
        // fomite/cooking moderate.
        XElement plagueFleaProtection = Child(plagueFlea, "apparelProtection");
        XElement plagueCookingProtection = Child(plagueCooking, "apparelProtection");
        XElement fluFomiteProtection = Child(Vector(flu, "Contagion.Vector_Fomite"), "apparelProtection");
        XElement plagueFoodCookSource = Child(plagueFood, "cookSourceProtection");
        checks.Add(Check.Close("corpse flea unsealed effectiveness", Field(plagueFleaProtection, "unsealedEffectiveness"), 0.20f, 0.0001f));
        checks.Add(Check.Close("fomite unsealed effectiveness", Field(fluFomiteProtection, "unsealedEffectiveness"), 0.60f, 0.0001f));
        checks.Add(Check.Close("cooking exposure unsealed effectiveness", Field(plagueCookingProtection, "unsealedEffectiveness"), 0.60f, 0.0001f));

        // Typhoid Mary (design §5): a sealed-suit cook bakes ~no contamination; a bare cook the full amount.
        float cookUnsealedEff = Field(plagueFoodCookSource, "unsealedEffectiveness");
        var sealedCook = new[] { (0.5f, 1f, 1f), (0.5f, 1f, 1f) };
        var bareCook = new[] { (0.5f, 0f, 0f), (0.5f, 0f, 0f) };
        checks.Add(Check.Close("sealed cook contaminates ~nothing", 1f - ContagionRiskMath.WeightedProtection(sealedCook, cookUnsealedEff), 0.00f, 0.001f));
        checks.Add(Check.Close("bare cook full contamination", 1f - ContagionRiskMath.WeightedProtection(bareCook, cookUnsealedEff), 1.00f, 0.001f));

        // Wiring: each protected vector site multiplies in the apparel factor.
        string fomiteTracker = File.ReadAllText(Path.Combine(Root, "Source", "Core", "Transmission", "ContagionVomitFomiteTracker.cs"));
        string corpseExposureUtility = File.ReadAllText(Path.Combine(Root, "Source", "Core", "ContagionCorpseExposureUtility.cs"));
        string environmentalProcessor = File.ReadAllText(Path.Combine(Root, "Source", "Core", "Transmission", "ContagionEnvironmentalExposureProcessor.cs"));
        string fecalOralTracker = File.ReadAllText(Path.Combine(Root, "Source", "Core", "Transmission", "ContagionFecalOralTracker.cs"));
        string developerDiagnosticsUtility = File.ReadAllText(Path.Combine(Root, "Source", "Core", "ContagionDeveloperDiagnosticsUtility.cs"));
        string hotspotStore = File.ReadAllText(Path.Combine(Root, "Source", "Core", "Transmission", "ContagionHotspotStore.cs"));
        string developerOverlay = File.ReadAllText(Path.Combine(Root, "Source", "Core", "ContagionDeveloperOverlayDrawer.cs"));
        string selectionOverlayPatch = File.ReadAllText(Path.Combine(Root, "Source", "Patches", "Patch_Pawn_DrawExtraSelectionOverlays.cs"));
        string cookingPatch = File.ReadAllText(Path.Combine(Root, "Source", "Patches", "Patch_GenRecipe_MakeRecipeProducts.cs"));
        string contaminatedFood = File.ReadAllText(Path.Combine(Root, "Source", "Comps", "Comp_ContaminatedFood.cs"));
        string apparelUtility = File.ReadAllText(Path.Combine(Root, "Source", "Core", "ContagionApparelProtectionUtility.cs"));
        checks.Add(Check.Bool("fomite applies contact protection", fomiteTracker.Contains("GetContactProtectionFactor"), true));
        checks.Add(Check.Bool("corpse flea/fluid apply contact protection", corpseExposureUtility.Contains("GetContactProtectionFactor"), true));
        checks.Add(Check.Bool("environmental applies contact protection", environmentalProcessor.Contains("GetContactProtectionFactor"), true));
        checks.Add(Check.Bool("environmental normalizes biome disease commonality", environmentalProcessor.Contains("BiomeDiseaseCommonalityFactor"), true));
        checks.Add(Check.Bool("fecal-oral living applies contact protection", fecalOralTracker.Contains("GetContactProtectionFactor"), true));
        checks.Add(Check.Bool("fecal-oral eating caps body-size potency at the cow", fecalOralTracker.Contains("MaxBodySizePotencyFactor = 2.5f"), true));
        checks.Add(Check.Bool("fecal-oral eating floors body-size potency so small animals shed", fecalOralTracker.Contains("MinBodySizePotencyFactor = 0.4f"), true));
        checks.Add(Check.Bool("fecal-oral eating caps accumulated node potency", fecalOralTracker.Contains("MaxNodePotency = 4f"), true));
        checks.Add(Check.Bool("fecal-oral cleanup uses weather-independent tested math", fecalOralTracker.Contains("ShouldCleanupHotspot"), true));
        checks.Add(Check.Bool("fecal-oral stacking uses tested additive math", hotspotStore.Contains("AddHotspotPotency"), true));
        checks.Add(Check.Bool("eating overlay uses tested visible-cell scale", developerOverlay.Contains("VisibleChanceMaximum"), true));
        checks.Add(Check.Bool("eating overlay is single-selection-only",
            developerOverlay.Contains("SingleSelectedThing != ingester")
            && selectionOverlayPatch.Contains("SingleSelectedThing == __instance"), true));
        checks.Add(Check.Bool("cooking exposure applies contact protection", cookingPatch.Contains("GetContactProtectionFactor"), true));
        checks.Add(Check.Bool("cook->food applies source protection", contaminatedFood.Contains("GetCookSourceProtectionFactor"), true));
        checks.Add(Check.Bool("seal integrity reads worn HitPoints", apparelUtility.Contains("HitPoints"), true));
        checks.Add(Check.Bool("apparel utility uses SealIntegrity", apparelUtility.Contains("SealIntegrity"), true));
        checks.Add(Check.Bool("pawn-to-pawn breakdown applies seeder bonus",
            developerDiagnosticsUtility.Contains("SeederBonusApplied = targetFactor > 0f")
            && developerDiagnosticsUtility.Contains("ShouldApplySeederBonus"), true));
        int nominalBreakdownIndex = developerDiagnosticsUtility.IndexOf("private static bool TryBuildNominalBreakdown", StringComparison.Ordinal);
        checks.Add(Check.Bool("nominal overlay breakdowns skip seeder bonus",
            nominalBreakdownIndex >= 0
            && !developerDiagnosticsUtility.Substring(nominalBreakdownIndex).Contains("ShouldApplySeederBonus"), true));

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

    private static XElement Child(XElement element, string name)
    {
        XElement child = element?.Element(name);
        return child ?? throw new InvalidOperationException($"Missing element {name}.");
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

    // Point-blank (distance 0, clean-map) fecal-oral grazing chance for a shedder of the given body
    // size at peak infectivity: baseChancePerIngestion × grazingFactor × clamp(BodySize^exp, .., max).
    // Mirrors ContagionFecalOralTracker.TryGetNodeGrazingChance with distanceFactor = 1 and seeder = 1.
    private static float GrazingPointBlank(XElement eatingVector, float bodySize, float maxBodySizePotencyFactor)
    {
        float potencyFactor = Math.Min(
            (float)Math.Pow(bodySize, Field(eatingVector, "bodySizePotencyExponent")),
            maxBodySizePotencyFactor);
        return Field(eatingVector, "baseChancePerIngestion") * Field(eatingVector, "grazingFactor") * potencyFactor;
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
