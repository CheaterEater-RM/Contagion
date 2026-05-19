using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion.Patches;

internal static class Patch_IncidentWorker_Disease_Helper
{
    public static bool TryGetProfile(IncidentWorker_Disease worker, out ResolvedTransmissionProfile resolvedProfile)
    {
        resolvedProfile = null;

        HediffDef diseaseDef = worker?.def?.diseaseIncident;
        return diseaseDef != null && DiseaseProfileCache.TryGetResolvedProfile(diseaseDef, out resolvedProfile);
    }

    public static bool TryGetStorytellerProfile(IncidentWorker_Disease worker, out ResolvedTransmissionProfile resolvedProfile)
    {
        if (!TryGetProfile(worker, out resolvedProfile))
        {
            return false;
        }

        return GetStorytellerSeeder(resolvedProfile.Profile) != null;
    }

    public static Seeder_Storyteller GetStorytellerSeeder(TransmissionProfile profile)
    {
        if (profile?.seeders == null)
        {
            return null;
        }

        for (int i = 0; i < profile.seeders.Count; i++)
        {
            if (profile.seeders[i] is Seeder_Storyteller storytellerSeeder)
            {
                return storytellerSeeder;
            }
        }

        return null;
    }

    public static Seeder_Environmental GetEnvironmentalSeeder(TransmissionProfile profile)
    {
        if (profile?.seeders == null)
        {
            return null;
        }

        for (int i = 0; i < profile.seeders.Count; i++)
        {
            if (profile.seeders[i] is Seeder_Environmental environmentalSeeder)
            {
                return environmentalSeeder;
            }
        }

        return null;
    }

    public static bool UsesEnvironmentalSeedingOnly(TransmissionProfile profile)
    {
        return GetStorytellerSeeder(profile) == null && GetEnvironmentalSeeder(profile) != null;
    }

    public static List<Pawn> SeedIncubationToPawns(
        ResolvedTransmissionProfile resolvedProfile,
        IEnumerable<Pawn> pawns,
        Seeder_Storyteller seeder,
        Map map,
        out string blockedInfo)
    {
        List<Pawn> seededPawns = new List<Pawn>();
        Dictionary<HediffDef, List<Pawn>> blockedByImmunity = new Dictionary<HediffDef, List<Pawn>>();

        foreach (Pawn pawn in pawns)
        {
            float chance = ContagionTransmissionUtility.BuildSeederChance(
                1f,
                pawn,
                resolvedProfile,
                map,
                1f,
                out HediffDef immunityCause);
            if (chance <= 0f)
            {
                if (immunityCause != null)
                {
                    if (!blockedByImmunity.TryGetValue(immunityCause, out List<Pawn> blockedPawns))
                    {
                        blockedPawns = new List<Pawn>();
                        blockedByImmunity.Add(immunityCause, blockedPawns);
                    }

                    blockedPawns.Add(pawn);
                }

                continue;
            }

            if (!Rand.Chance(Mathf.Clamp01(chance)))
            {
                continue;
            }

            if (ContagionDiseaseUtility.TrySeedIncubation(pawn, resolvedProfile.DiseaseDef, resolvedProfile.PartsToAffect, out HediffDef seedImmunityCause))
            {
                seededPawns.Add(pawn);
            }
            else if (seedImmunityCause != null)
            {
                if (!blockedByImmunity.TryGetValue(seedImmunityCause, out List<Pawn> blockedPawns))
                {
                    blockedPawns = new List<Pawn>();
                    blockedByImmunity.Add(seedImmunityCause, blockedPawns);
                }

                blockedPawns.Add(pawn);
            }
        }

        if (seededPawns.Count > 0)
        {
            map?.GetComponent<Contagion_MapTransmissionComponent>()?.NotifySeederFired(resolvedProfile, seeder);
        }

        blockedInfo = string.Empty;
        foreach (KeyValuePair<HediffDef, List<Pawn>> blockedGroup in blockedByImmunity)
        {
            if (blockedGroup.Key == resolvedProfile.DiseaseDef)
            {
                continue;
            }

            if (blockedInfo.Length != 0)
            {
                blockedInfo += "\n\n";
            }

            blockedInfo = blockedInfo
                + "LetterDisease_Blocked".Translate(blockedGroup.Key.LabelCap, resolvedProfile.DiseaseDef.label).Resolve()
                + ":\n"
                + blockedGroup.Value.Select(victim => victim.LabelNoCountColored.Resolve()).ToLineList("  - ");
        }

        return seededPawns;
    }

    public static TaggedString BuildExposureLetterText(List<Pawn> seededPawns, HediffDef diseaseDef, string blockedInfo)
    {
        TaggedString baseText;
        if (seededPawns.Count == 1)
        {
            baseText = "Contagion_LetterExposureSingle".Translate(seededPawns[0].LabelShortCap, diseaseDef.label);
        }
        else if (seededPawns.Count > 1)
        {
            StringBuilder stringBuilder = new StringBuilder();
            for (int i = 0; i < seededPawns.Count; i++)
            {
                if (stringBuilder.Length != 0)
                {
                    stringBuilder.AppendLine();
                }

                stringBuilder.AppendTagged("  - " + seededPawns[i].LabelNoCountColored.Resolve());
            }

            baseText = "Contagion_LetterExposureMultiple".Translate(seededPawns.Count.ToString(), diseaseDef.label).Resolve() + ":\n\n" + stringBuilder;
        }
        else
        {
            baseText = "Contagion_LetterExposureBlocked".Translate(diseaseDef.label);
        }

        if (blockedInfo.NullOrEmpty())
        {
            return baseText;
        }

        if (baseText.NullOrEmpty())
        {
            return blockedInfo;
        }

        return baseText + "\n\n" + blockedInfo;
    }
}

[HarmonyPatch(typeof(IncidentWorker_Disease), nameof(IncidentWorker_Disease.ApplyToPawns))]
internal static class Patch_IncidentWorker_Disease_ApplyToPawns
{
    // This must cancel the vanilla path because Contagion replaces immediate disease application
    // with incubation for profiled storyteller-seeded diseases.
    public static bool Prefix(IncidentWorker_Disease __instance, IEnumerable<Pawn> pawns, ref string blockedInfo, ref List<Pawn> __result)
    {
        if (!Patch_IncidentWorker_Disease_Helper.TryGetProfile(__instance, out ResolvedTransmissionProfile resolvedProfile))
        {
            return true;
        }

        if (Patch_IncidentWorker_Disease_Helper.UsesEnvironmentalSeedingOnly(resolvedProfile.Profile))
        {
            blockedInfo = string.Empty;
            __result = new List<Pawn>();
            return false;
        }

        if (Patch_IncidentWorker_Disease_Helper.GetStorytellerSeeder(resolvedProfile.Profile) == null)
        {
            return true;
        }

        List<Pawn> pawnList = pawns?.ToList();
        if (pawnList == null || pawnList.Count == 0 || pawnList.Any(pawn => pawn == null || !pawn.Spawned))
        {
            return true;
        }

        Seeder_Storyteller storytellerSeeder = Patch_IncidentWorker_Disease_Helper.GetStorytellerSeeder(resolvedProfile.Profile);
        Map map = pawnList[0].Map;
        if (map?.GetComponent<Contagion_MapTransmissionComponent>()?.CanRunSeeder(resolvedProfile, storytellerSeeder) == false)
        {
            blockedInfo = string.Empty;
            __result = new List<Pawn>();
            return false;
        }

        ContagionDiagnostics.Record(ContagionDiagnosticCounter.StorytellerAttempted, pawnList.Count);
        __result = Patch_IncidentWorker_Disease_Helper.SeedIncubationToPawns(resolvedProfile, pawnList, storytellerSeeder, map, out blockedInfo);
        if (__result.Count > 0)
        {
            ContagionDiagnostics.Record(ContagionDiagnosticCounter.StorytellerSeeded, __result.Count);
            ContagionDiagnostics.Trace($"Storyteller seeded {resolvedProfile.DiseaseDef.defName} on {__result.Count} pawn(s).");
        }

        return false;
    }
}

[HarmonyPatch(typeof(IncidentWorker_Disease), "TryExecuteWorker")]
internal static class Patch_IncidentWorker_Disease_TryExecuteWorker
{
    // This must cancel the vanilla path because the original letter flow assumes the real disease
    // hediff already exists, while Contagion seeds incubation first.
    public static bool Prefix(IncidentWorker_Disease __instance, IncidentParms parms, ref bool __result)
    {
        if (parms.target is not Map || !Patch_IncidentWorker_Disease_Helper.TryGetStorytellerProfile(__instance, out ResolvedTransmissionProfile resolvedProfile))
        {
            return true;
        }

        MethodInfo actualVictimsMethod = AccessTools.Method(__instance.GetType(), "ActualVictims");
        if (actualVictimsMethod == null)
        {
            Log.Error($"[Contagion] Could not resolve ActualVictims() for storyteller disease worker {__instance.GetType().FullName}.");
            return true;
        }

        List<Pawn> actualVictims;
        try
        {
            actualVictims = ((IEnumerable<Pawn>)actualVictimsMethod.Invoke(__instance, new object[] { parms }))?.ToList();
        }
        catch (TargetInvocationException ex)
        {
            Log.Error($"[Contagion] ActualVictims() threw while resolving storyteller disease targets for {__instance.GetType().FullName}: {ex}");
            return true;
        }
        catch (System.Exception ex)
        {
            Log.Error($"[Contagion] Failed to invoke ActualVictims() for storyteller disease worker {__instance.GetType().FullName}: {ex}");
            return true;
        }

        if (actualVictims == null || actualVictims.Count == 0)
        {
            __result = false;
            return false;
        }

        Map map = (Map)parms.target;
        Seeder_Storyteller storytellerSeeder = Patch_IncidentWorker_Disease_Helper.GetStorytellerSeeder(resolvedProfile.Profile);
        if (map.GetComponent<Contagion_MapTransmissionComponent>()?.CanRunSeeder(resolvedProfile, storytellerSeeder) == false)
        {
            __result = false;
            return false;
        }

        int seedCount = storytellerSeeder?.seedCountRange.RandomInRange ?? 1;
        seedCount = Mathf.Clamp(seedCount, 1, actualVictims.Count);

        actualVictims.Shuffle();
        if (actualVictims.Count > seedCount)
        {
            actualVictims.RemoveRange(seedCount, actualVictims.Count - seedCount);
        }

        List<Pawn> seededPawns = Patch_IncidentWorker_Disease_Helper.SeedIncubationToPawns(
            resolvedProfile,
            actualVictims,
            storytellerSeeder,
            map,
            out string blockedInfo);
        if (seededPawns.Count == 0 && blockedInfo.NullOrEmpty())
        {
            __result = false;
            return false;
        }

        if (seededPawns.Count == 0 && !blockedInfo.NullOrEmpty())
        {
            TaggedString label = "Contagion_LetterLabelExposure".Translate(__instance.def.diseaseIncident.label);
            TaggedString text = Patch_IncidentWorker_Disease_Helper.BuildExposureLetterText(seededPawns, __instance.def.diseaseIncident, blockedInfo);
            Find.LetterStack.ReceiveLetter(label, text, __instance.def.letterDef);
        }

        __result = true;
        return false;
    }
}
