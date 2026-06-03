using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

internal sealed class ContagionVomitFomiteTracker : IExposable
{
    private const int TicksPerHour = 2500;

    private const float MinFomitePotency = 0.05f;

    private ContagionFilthStore _contaminatedVomit = new ContagionFilthStore();

    public void ExposeData()
    {
        _contaminatedVomit ??= new ContagionFilthStore();
        _contaminatedVomit.ExposeData("contaminatedVomit");
    }

    public void NotifyVomitFilthCreated(Filth filth, Pawn sourcePawn, Map map)
    {
        if (filth == null || sourcePawn == null || sourcePawn.Map != map)
        {
            return;
        }

        foreach (ResolvedTransmissionProfile resolvedProfile in ContagionDiseaseUtility.GetContagiousProfiles(sourcePawn))
        {
            if (!ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Fomite fomiteVector) || !fomiteVector.contaminatesVomit)
            {
                continue;
            }

            float initialPotency = ContagionTransmissionUtility.GetSourceInfectivity(sourcePawn, resolvedProfile, fomiteVector);
            if (initialPotency <= 0f)
            {
                continue;
            }

            _contaminatedVomit.AddOrUpdate(filth, resolvedProfile.DiseaseDef, sourcePawn.def, initialPotency);

            ContagionDiagnostics.Record(ContagionDiagnosticCounter.VomitFilthContaminated);
            ContagionDiagnostics.Trace($"Vomit contaminated: {resolvedProfile.DiseaseDef.defName} from {sourcePawn.LabelShortCap} at potency {initialPotency:0.###}.");
            return;
        }
    }

    public void Cleanup(Map map)
    {
        _contaminatedVomit.Cleanup(
            map,
            entry => !DiseaseProfileCache.TryGetResolvedProfile(entry.DiseaseDef, out ResolvedTransmissionProfile resolvedProfile)
                || !ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Fomite fomiteVector)
                || GetFomitePotencyFactor(entry.Tick, fomiteVector.potencyDecayPerHour) <= MinFomitePotency);
    }

    public void RunFomiteExposurePass(IReadOnlyList<Pawn> spawnedPawns, Map map)
    {
        if (_contaminatedVomit.Count == 0)
        {
            return;
        }

        float transmissionMultiplier = Contagion_Mod.Settings?.EffectiveTransmissionMultiplier ?? 1f;
        for (int pawnIndex = 0; pawnIndex < spawnedPawns.Count; pawnIndex++)
        {
            Pawn pawn = spawnedPawns[pawnIndex];
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map != map)
            {
                continue;
            }

            for (int contaminationIndex = 0; contaminationIndex < _contaminatedVomit.Count; contaminationIndex++)
            {
                ContagionFilthEntry entry = _contaminatedVomit.Get(contaminationIndex);
                Filth filth = entry.Filth;
                if (filth == null || filth.Position != pawn.Position)
                {
                    continue;
                }

                if (!DiseaseProfileCache.TryGetResolvedProfile(entry.DiseaseDef, out ResolvedTransmissionProfile resolvedProfile)
                    || !ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Fomite fomiteVector))
                {
                    continue;
                }

                float sourceSpeciesFactor = ContagionTransmissionUtility.GetSourceSpeciesFactor(entry.SourceDef, pawn, resolvedProfile, map);
                if (sourceSpeciesFactor <= 0f)
                {
                    continue;
                }

                float potencyFactor = Mathf.Max(0f, entry.Potency)
                    * GetFomitePotencyFactor(entry.Tick, fomiteVector.potencyDecayPerHour);
                if (potencyFactor <= MinFomitePotency)
                {
                    continue;
                }

                float suppression = ContagionTransmissionUtility.GetSpreadSuppressionFactor(map, resolvedProfile, pawn);

                // Worn apparel (gloves / sealed suit) reduces what the pawn picks up from the filth.
                float protectionFactor = ContagionApparelProtectionUtility.GetContactProtectionFactor(pawn, fomiteVector.apparelProtection);

                ContagionDiagnostics.Record(ContagionDiagnosticCounter.FomiteAttempted);
                float chance = ContagionTransmissionUtility.BuildSeederChance(
                    fomiteVector.baseChancePerContact * potencyFactor * sourceSpeciesFactor * suppression * protectionFactor,
                    pawn,
                    resolvedProfile,
                    map,
                    transmissionMultiplier,
                    out HediffDef _);
                float finalChance = Mathf.Clamp01(chance);
                bool passed = Rand.Chance(finalChance);
                bool seeded = false;
                if (passed)
                {
                    seeded = ContagionDiseaseUtility.TrySeedIncubation(
                        pawn,
                        resolvedProfile.DiseaseDef,
                        resolvedProfile.PartsToAffect,
                        null,
                        ContagionDiagnosticOrigin.Spread,
                        ContagionSeedSource.Contact,
                        out HediffDef _);
                    if (seeded)
                    {
                        ContagionDiagnostics.Record(ContagionDiagnosticCounter.FomiteSeeded);
                        ContagionDiagnostics.Trace($"Fomite transmission: {resolvedProfile.DiseaseDef.defName} on {pawn.LabelShortCap} from vomit filth.");
                        ContagionTrace.Transmission(filth, pawn, resolvedProfile.DiseaseDef, ContagionDebugVectorKind.Fomite);
                    }
                }

                ContagionDiagnostics.LogRoll(ContagionDebugVectorKind.Fomite, filth, pawn, resolvedProfile.DiseaseDef, finalChance, seeded);
                if (seeded)
                {
                    break;
                }
            }
        }
    }

    private static float GetFomitePotencyFactor(int contaminationTick, float potencyDecayPerHour)
    {
        float elapsedHours = Mathf.Max(0f, (Find.TickManager.TicksGame - contaminationTick) / (float)TicksPerHour);
        return Mathf.Exp(-Mathf.Max(0f, potencyDecayPerHour) * elapsedHours);
    }

}
