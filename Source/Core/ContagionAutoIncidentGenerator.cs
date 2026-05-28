using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Contagion;

// Option C: for a profiled disease that opts in via TransmissionProfile.selfSchedules and has no
// hand-authored disease IncidentDef, generate one at startup so the vanilla storyteller schedules it
// in Storyteller mode (Mode 1). The generated incident mirrors a real vanilla disease incident of the
// matching species (so category, worker, and target tags stay correct across versions/DLC) and registers
// its own biome presence through diseaseBiomeRecords, so no BiomeDef is patched. Everything downstream is
// the existing intercept -> pending event -> incubation pipeline; this only supplies the incident the
// modder did not write.
public static class ContagionAutoIncidentGenerator
{
    private const string GeneratedDefNamePrefix = "Contagion_AutoDisease_";

    // Runs from the [StaticConstructorOnStartup] entry point, after defs are loaded and ResolveReferences
    // has run, and before any gameplay disease roll. Iterates HediffDefs directly rather than through
    // DiseaseProfileCache so the cache is not built before the generated incidents exist.
    public static void GenerateAndValidate()
    {
        ModContentPack contentPack = LoadedModManager.GetMod<Contagion_Mod>()?.Content;
        int generated = 0;

        foreach (HediffDef diseaseDef in DefDatabase<HediffDef>.AllDefsListForReading)
        {
            TransmissionProfile profile = diseaseDef.GetModExtension<TransmissionProfile>();
            if (profile == null)
            {
                continue;
            }

            bool hasStorytellerSeeder = HasStorytellerSeeder(profile);
            bool hasLinkedIncident = HasLinkedIncident(diseaseDef);

            if (profile.selfSchedules)
            {
                if (!hasStorytellerSeeder)
                {
                    Log.Warning($"[Contagion] {diseaseDef.defName} sets selfSchedules=true but has no Seeder_Storyteller, "
                        + "so a storyteller pick would apply the disease directly instead of routing through incubation. "
                        + "Add a Seeder_Storyteller. No incident was generated.");
                    continue;
                }

                if (hasLinkedIncident)
                {
                    if (Prefs.DevMode)
                    {
                        Log.Message($"[Contagion] {diseaseDef.defName} sets selfSchedules=true but already has a disease "
                            + "IncidentDef; skipping auto-generation.");
                    }

                    continue;
                }

                if (TryGenerateIncident(diseaseDef, profile, contentPack))
                {
                    generated++;
                }
            }
            else if (hasStorytellerSeeder && !hasLinkedIncident)
            {
                Log.Warning($"[Contagion] {diseaseDef.defName} declares a Seeder_Storyteller but has no disease IncidentDef "
                    + "and selfSchedules=false, so the storyteller can never schedule it (it will not seed in Storyteller "
                    + "mode). Set selfSchedules=true, or author an IncidentDef for it.");
            }
        }

        if (generated > 0)
        {
            // The cache resolves each profile's linked incident lazily on first access; reset so the next
            // build sees the incidents we just added.
            DiseaseProfileCache.Reset();

            if (Prefs.DevMode)
            {
                Log.Message($"[Contagion] Auto-generated {generated} self-scheduling disease incident(s).");
            }
        }
    }

    private static bool TryGenerateIncident(HediffDef diseaseDef, TransmissionProfile profile, ModContentPack contentPack)
    {
        IncidentDef template = FindTemplateIncident(profile.affectsHumans);
        if (template == null)
        {
            Log.Warning($"[Contagion] Could not auto-generate a disease incident for {diseaseDef.defName}: no vanilla "
                + $"{(profile.affectsHumans ? "human" : "animal")} disease incident was found to use as a template.");
            return false;
        }

        IncidentDef gen = new IncidentDef
        {
            defName = GeneratedDefNamePrefix + diseaseDef.defName,
            label = diseaseDef.label,
            workerClass = template.workerClass,
            category = template.category,
            targetTags = template.targetTags != null ? new List<IncidentTargetTagDef>(template.targetTags) : null,
            diseaseVictimFractionRange = template.diseaseVictimFractionRange,
            diseaseMaxVictims = template.diseaseMaxVictims,
            letterDef = template.letterDef,
            letterText = template.letterText,
            letterLabel = template.letterLabel,
            diseaseIncident = diseaseDef,
            diseasePartsToAffect = profile.targetBodyParts.NullOrEmpty()
                ? null
                : new List<BodyPartDef>(profile.targetBodyParts),
            modContentPack = contentPack
        };

        gen.diseaseBiomeRecords = BuildBiomeRecords(gen, profile.selfScheduleCommonality);
        if (gen.diseaseBiomeRecords.Count == 0)
        {
            Log.Warning($"[Contagion] Auto-generated incident for {diseaseDef.defName} has no biome records "
                + "(no biome with diseaseMtbDays > 0); it will not be storyteller-scheduled.");
        }

        DefGenerator.AddImpliedDef(gen);
        return true;
    }

    private static List<BiomeDiseaseRecord> BuildBiomeRecords(IncidentDef incident, float commonality)
    {
        List<BiomeDiseaseRecord> records = new List<BiomeDiseaseRecord>();
        foreach (BiomeDef biome in DefDatabase<BiomeDef>.AllDefsListForReading)
        {
            // Biomes that do not roll storyteller diseases (diseaseMtbDays <= 0) cover the special/no-disease
            // biomes (e.g. space), so skipping them avoids placing the disease somewhere nonsensical.
            if (biome.diseaseMtbDays <= 0f)
            {
                continue;
            }

            records.Add(new BiomeDiseaseRecord
            {
                biome = biome,
                diseaseInc = incident,
                commonality = commonality
            });
        }

        return records;
    }

    private static IncidentDef FindTemplateIncident(bool human)
    {
        foreach (IncidentDef incident in DefDatabase<IncidentDef>.AllDefsListForReading)
        {
            if (incident.diseaseIncident == null || incident.workerClass == null)
            {
                continue;
            }

            if (incident.defName.StartsWith(GeneratedDefNamePrefix))
            {
                continue;
            }

            if (human)
            {
                if (typeof(IncidentWorker_DiseaseHuman).IsAssignableFrom(incident.workerClass))
                {
                    return incident;
                }
            }
            else if (typeof(IncidentWorker_DiseaseAnimal).IsAssignableFrom(incident.workerClass))
            {
                return incident;
            }
        }

        return null;
    }

    private static bool HasStorytellerSeeder(TransmissionProfile profile)
    {
        if (profile.seeders == null)
        {
            return false;
        }

        for (int i = 0; i < profile.seeders.Count; i++)
        {
            if (profile.seeders[i] is Seeder_Storyteller)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasLinkedIncident(HediffDef diseaseDef)
    {
        foreach (IncidentDef incident in DefDatabase<IncidentDef>.AllDefsListForReading)
        {
            if (incident.diseaseIncident == diseaseDef && !incident.defName.StartsWith(GeneratedDefNamePrefix))
            {
                return true;
            }
        }

        return false;
    }
}
