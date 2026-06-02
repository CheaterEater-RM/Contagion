using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

public static class ContagionSeedingExecutionUtility
{
    private const float MildDiseaseSeverity = 0.15f;

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
            if (ContagionTransmissionUtility.IsAtActiveCaseCapacity(map, resolvedProfile, pawn))
            {
                continue;
            }

            float chance = ContagionTransmissionUtility.BuildSeederChance(
                1f,
                pawn,
                resolvedProfile,
                map,
                1f,
                out HediffDef immunityCause);
            if (chance <= 0f)
            {
                ContagionDiagnostics.RecordApplicationResult(ContagionDiagnosticOrigin.Incidence, seeded: false, immunityCause);
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
                ContagionDiagnostics.LogRoll(ContagionDebugVectorKind.Environmental, null, pawn, resolvedProfile.DiseaseDef, Mathf.Clamp01(chance), passed: false);
                continue;
            }

            if (ContagionDiseaseUtility.TrySeedIncubation(
                pawn,
                resolvedProfile.ResolveHediffForPawn(pawn),
                resolvedProfile.PartsToAffect,
                ContagionDiagnosticOrigin.Incidence,
                ContagionSeedSource.Storyteller,
                out HediffDef seedImmunityCause))
            {
                seededPawns.Add(pawn);
                ContagionDiagnostics.LogRoll(ContagionDebugVectorKind.Environmental, null, pawn, resolvedProfile.DiseaseDef, Mathf.Clamp01(chance), passed: true);
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

    public static bool IsEligiblePawn(Pawn pawn, ResolvedTransmissionProfile resolvedProfile, Map map, out HediffDef immunityCause)
    {
        immunityCause = null;
        if (pawn == null || pawn.Dead || !pawn.Spawned || map == null || pawn.Map != map)
        {
            return false;
        }

        return !ContagionTransmissionUtility.IsAtActiveCaseCapacity(map, resolvedProfile, pawn)
            && ContagionTransmissionUtility.BuildSeederChance(1f, pawn, resolvedProfile, map, 1f, out immunityCause) > 0f;
    }

    public static bool TrySeedExactPawn(Pawn pawn, ResolvedTransmissionProfile resolvedProfile, out HediffDef immunityCause)
    {
        return TrySeedExactPawn(pawn, resolvedProfile, ContagionDiagnosticOrigin.Incidence, ContagionSeedSource.Acausal, out immunityCause);
    }

    public static bool TrySeedExactPawn(
        Pawn pawn,
        ResolvedTransmissionProfile resolvedProfile,
        ContagionDiagnosticOrigin origin,
        out HediffDef immunityCause)
    {
        return TrySeedExactPawn(pawn, resolvedProfile, origin, ContagionSeedSource.Acausal, out immunityCause);
    }

    public static bool TrySeedExactPawn(
        Pawn pawn,
        ResolvedTransmissionProfile resolvedProfile,
        ContagionDiagnosticOrigin origin,
        ContagionSeedSource seedSource,
        out HediffDef immunityCause)
    {
        immunityCause = null;
        if (pawn == null || resolvedProfile?.DiseaseDef == null)
        {
            return false;
        }

        return ContagionDiseaseUtility.TrySeedIncubation(
            pawn,
            resolvedProfile.ResolveHediffForPawn(pawn),
            resolvedProfile.PartsToAffect,
            origin,
            seedSource,
            out immunityCause);
    }

    public static bool TrySeedArrivalCarrier(Pawn pawn, ResolvedTransmissionProfile resolvedProfile, float mildVisibleChance, out bool visibleDisease)
    {
        visibleDisease = false;
        if (pawn?.RaceProps?.Animal == true && resolvedProfile?.Profile?.showsSickSignal == true)
        {
            return TrySeedExactPawn(pawn, resolvedProfile, ContagionDiagnosticOrigin.Incidence, ContagionSeedSource.Arrival, out HediffDef _);
        }

        if (Rand.Chance(Mathf.Clamp01(mildVisibleChance))
            && TrySeedMildVisibleDisease(pawn, resolvedProfile, ContagionDiagnosticOrigin.Incidence, ContagionSeedSource.Arrival, out HediffDef _))
        {
            visibleDisease = true;
            return true;
        }

        return TrySeedExactPawn(pawn, resolvedProfile, ContagionDiagnosticOrigin.Incidence, ContagionSeedSource.Arrival, out HediffDef _);
    }

    public static bool TrySeedMildVisibleDisease(Pawn pawn, ResolvedTransmissionProfile resolvedProfile, out HediffDef immunityCause)
    {
        return TrySeedMildVisibleDisease(pawn, resolvedProfile, ContagionDiagnosticOrigin.Incidence, ContagionSeedSource.Arrival, out immunityCause);
    }

    public static bool TrySeedMildVisibleDisease(
        Pawn pawn,
        ResolvedTransmissionProfile resolvedProfile,
        ContagionDiagnosticOrigin origin,
        out HediffDef immunityCause)
    {
        return TrySeedMildVisibleDisease(pawn, resolvedProfile, origin, ContagionSeedSource.Arrival, out immunityCause);
    }

    public static bool TrySeedMildVisibleDisease(
        Pawn pawn,
        ResolvedTransmissionProfile resolvedProfile,
        ContagionDiagnosticOrigin origin,
        ContagionSeedSource seedSource,
        out HediffDef immunityCause)
    {
        immunityCause = null;
        if (pawn == null || resolvedProfile?.DiseaseDef == null)
        {
            return false;
        }

        HediffDef targetDef = resolvedProfile.ResolveHediffForPawn(pawn);
        if (!ContagionDiseaseUtility.CanContractDiseaseNow(pawn, targetDef, resolvedProfile.PartsToAffect, out immunityCause))
        {
            return false;
        }

        List<Hediff> addedHediffs = new List<Hediff>();
        bool applied = HediffGiverUtility.TryApply(
            pawn,
            targetDef,
            resolvedProfile.PartsToAffect,
            outAddedHediffs: addedHediffs);
        if (!applied)
        {
            return false;
        }

        for (int i = 0; i < addedHediffs.Count; i++)
        {
            Hediff hediff = addedHediffs[i];
            if (hediff?.def == targetDef)
            {
                hediff.Severity = Mathf.Min(hediff.def.maxSeverity, MildDiseaseSeverity);
            }
        }

        TaleRecorder.RecordTale(TaleDefOf.IllnessRevealed, pawn, targetDef);
        ContagionDiseaseNotifier.NotifyDiseaseActivated(pawn, addedHediffs.Count > 0 ? addedHediffs[0] : null, targetDef, seedSource);
        ContagionDiagnostics.RecordApplicationResult(origin, seeded: true, immunityCause: null);
        return true;
    }

    public static bool TrySeedRandomEligiblePawn(IReadOnlyList<Pawn> pawns, ResolvedTransmissionProfile resolvedProfile, Map map, out Pawn seededPawn)
    {
        return TrySeedWeightedEligiblePawn(pawns, resolvedProfile, map, null, ContagionDiagnosticOrigin.Incidence, out seededPawn);
    }

    public static bool TrySeedWeightedEligiblePawn(
        IReadOnlyList<Pawn> pawns,
        ResolvedTransmissionProfile resolvedProfile,
        Map map,
        Func<Pawn, float> weightSelector,
        out Pawn seededPawn)
    {
        return TrySeedWeightedEligiblePawn(pawns, resolvedProfile, map, weightSelector, ContagionDiagnosticOrigin.Incidence, out seededPawn);
    }

    public static bool TrySeedWeightedEligiblePawn(
        IReadOnlyList<Pawn> pawns,
        ResolvedTransmissionProfile resolvedProfile,
        Map map,
        Func<Pawn, float> weightSelector,
        ContagionDiagnosticOrigin origin,
        out Pawn seededPawn)
    {
        seededPawn = null;
        if (pawns == null || resolvedProfile?.DiseaseDef == null || map == null)
        {
            return false;
        }

        List<Pawn> candidates = new List<Pawn>();
        List<float> weights = weightSelector == null ? null : new List<float>();

        for (int i = 0; i < pawns.Count; i++)
        {
            Pawn pawn = pawns[i];
            if (!IsEligiblePawn(pawn, resolvedProfile, map, out HediffDef _))
            {
                continue;
            }

            candidates.Add(pawn);
            if (weights != null)
            {
                weights.Add(Mathf.Max(0f, weightSelector(pawn)));
            }
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        Pawn selectedPawn = weights == null
            ? candidates.RandomElement()
            : SelectWeightedPawn(candidates, weights);
        if (selectedPawn == null)
        {
            return false;
        }

        if (!TrySeedExactPawn(selectedPawn, resolvedProfile, origin, out HediffDef _))
        {
            return false;
        }

        seededPawn = selectedPawn;
        return true;
    }

    private static Pawn SelectWeightedPawn(List<Pawn> candidates, List<float> weights)
    {
        float totalWeight = 0f;
        for (int i = 0; i < weights.Count; i++)
        {
            totalWeight += Mathf.Max(0f, weights[i]);
        }

        if (totalWeight <= 0f)
        {
            return candidates.RandomElement();
        }

        float roll = Rand.Value * totalWeight;
        float runningWeight = 0f;
        for (int i = 0; i < candidates.Count; i++)
        {
            runningWeight += Mathf.Max(0f, weights[i]);
            if (roll <= runningWeight)
            {
                return candidates[i];
            }
        }

        return candidates[candidates.Count - 1];
    }
}
