using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

internal sealed class ContagionVomitFomiteTracker : IExposable
{
    private const int TicksPerHour = 2500;

    private const float MinFomitePotency = 0.05f;

    private List<Filth> _contaminatedVomitFilth = new List<Filth>();

    private List<HediffDef> _contaminatedVomitDiseases = new List<HediffDef>();

    private List<int> _contaminatedVomitTicks = new List<int>();

    public void ExposeData()
    {
        Scribe_Collections.Look(ref _contaminatedVomitFilth, "contaminatedVomitFilth", LookMode.Reference);
        Scribe_Collections.Look(ref _contaminatedVomitDiseases, "contaminatedVomitDiseases", LookMode.Def);
        Scribe_Collections.Look(ref _contaminatedVomitTicks, "contaminatedVomitTicks", LookMode.Value);

        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            _contaminatedVomitFilth ??= new List<Filth>();
            _contaminatedVomitDiseases ??= new List<HediffDef>();
            _contaminatedVomitTicks ??= new List<int>();
        }
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

            int index = _contaminatedVomitFilth.IndexOf(filth);
            if (index >= 0)
            {
                _contaminatedVomitDiseases[index] = resolvedProfile.DiseaseDef;
                _contaminatedVomitTicks[index] = Find.TickManager.TicksGame;
            }
            else
            {
                _contaminatedVomitFilth.Add(filth);
                _contaminatedVomitDiseases.Add(resolvedProfile.DiseaseDef);
                _contaminatedVomitTicks.Add(Find.TickManager.TicksGame);
            }

            ContagionDiagnostics.Record(ContagionDiagnosticCounter.VomitFilthContaminated);
            ContagionDiagnostics.Trace($"Vomit contaminated: {resolvedProfile.DiseaseDef.defName} from {sourcePawn.LabelShortCap}.");
            return;
        }
    }

    public void Cleanup(Map map)
    {
        for (int i = _contaminatedVomitFilth.Count - 1; i >= 0; i--)
        {
            Filth filth = _contaminatedVomitFilth[i];
            HediffDef diseaseDef = i < _contaminatedVomitDiseases.Count ? _contaminatedVomitDiseases[i] : null;
            int contaminationTick = i < _contaminatedVomitTicks.Count ? _contaminatedVomitTicks[i] : 0;
            bool remove = filth == null || filth.Destroyed || !filth.Spawned || filth.Map != map || diseaseDef == null;

            if (!remove
                && (!DiseaseProfileCache.TryGetResolvedProfile(diseaseDef, out ResolvedTransmissionProfile resolvedProfile)
                    || !ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Fomite fomiteVector)
                    || GetFomitePotencyFactor(contaminationTick, fomiteVector.potencyDecayPerHour) <= MinFomitePotency))
            {
                remove = true;
            }

            if (!remove)
            {
                continue;
            }

            _contaminatedVomitFilth.RemoveAt(i);

            if (i < _contaminatedVomitDiseases.Count)
            {
                _contaminatedVomitDiseases.RemoveAt(i);
            }

            if (i < _contaminatedVomitTicks.Count)
            {
                _contaminatedVomitTicks.RemoveAt(i);
            }
        }
    }

    public void RunFomiteExposurePass(IReadOnlyList<Pawn> spawnedPawns, Map map)
    {
        if (_contaminatedVomitFilth.Count == 0)
        {
            return;
        }

        float transmissionMultiplier = Contagion_Mod.Settings?.EffectiveTransmissionMultiplier ?? 1f;
        Dictionary<HediffDef, float> suppressionByDisease = new Dictionary<HediffDef, float>();
        for (int pawnIndex = 0; pawnIndex < spawnedPawns.Count; pawnIndex++)
        {
            Pawn pawn = spawnedPawns[pawnIndex];
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map != map)
            {
                continue;
            }

            for (int contaminationIndex = 0; contaminationIndex < _contaminatedVomitFilth.Count; contaminationIndex++)
            {
                Filth filth = _contaminatedVomitFilth[contaminationIndex];
                if (filth == null || filth.Position != pawn.Position)
                {
                    continue;
                }

                if (!DiseaseProfileCache.TryGetResolvedProfile(_contaminatedVomitDiseases[contaminationIndex], out ResolvedTransmissionProfile resolvedProfile)
                    || !ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Fomite fomiteVector))
                {
                    continue;
                }

                float potencyFactor = GetFomitePotencyFactor(_contaminatedVomitTicks[contaminationIndex], fomiteVector.potencyDecayPerHour);
                if (potencyFactor <= MinFomitePotency)
                {
                    continue;
                }

                float suppression = 1f;
                if (ContagionTransmissionUtility.IsSuppressionTarget(pawn))
                {
                    if (!suppressionByDisease.TryGetValue(resolvedProfile.DiseaseDef, out suppression))
                    {
                        suppression = ContagionTransmissionUtility.GetSpreadSuppressionFactor(map, resolvedProfile);
                        suppressionByDisease[resolvedProfile.DiseaseDef] = suppression;
                    }
                }

                ContagionDiagnostics.Record(ContagionDiagnosticCounter.FomiteAttempted);
                float chance = ContagionTransmissionUtility.BuildSeederChance(
                    fomiteVector.baseChancePerContact * potencyFactor * suppression,
                    pawn,
                    resolvedProfile,
                    map,
                    transmissionMultiplier,
                    out HediffDef _);
                if (!Rand.Chance(Mathf.Clamp01(chance)))
                {
                    continue;
                }

                if (ContagionDiseaseUtility.TrySeedIncubation(
                    pawn,
                    resolvedProfile.DiseaseDef,
                    resolvedProfile.PartsToAffect,
                    ContagionDiagnosticOrigin.Spread,
                    out HediffDef _))
                {
                    ContagionDiagnostics.Record(ContagionDiagnosticCounter.FomiteSeeded);
                    ContagionDiagnostics.Trace($"Fomite transmission: {resolvedProfile.DiseaseDef.defName} on {pawn.LabelShortCap} from vomit filth.");
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
