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

    private List<float> _contaminatedVomitInitialPotencies = new List<float>();

    private List<ThingDef> _contaminatedVomitSourceDefs = new List<ThingDef>();

    public void ExposeData()
    {
        Scribe_Collections.Look(ref _contaminatedVomitFilth, "contaminatedVomitFilth", LookMode.Reference);
        Scribe_Collections.Look(ref _contaminatedVomitDiseases, "contaminatedVomitDiseases", LookMode.Def);
        Scribe_Collections.Look(ref _contaminatedVomitTicks, "contaminatedVomitTicks", LookMode.Value);
        Scribe_Collections.Look(ref _contaminatedVomitInitialPotencies, "contaminatedVomitInitialPotencies", LookMode.Value);
        Scribe_Collections.Look(ref _contaminatedVomitSourceDefs, "contaminatedVomitSourceDefs", LookMode.Def);

        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            _contaminatedVomitFilth ??= new List<Filth>();
            _contaminatedVomitDiseases ??= new List<HediffDef>();
            _contaminatedVomitTicks ??= new List<int>();
            _contaminatedVomitInitialPotencies ??= new List<float>();
            _contaminatedVomitSourceDefs ??= new List<ThingDef>();
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

            float initialPotency = ContagionTransmissionUtility.GetSourceInfectivity(sourcePawn, resolvedProfile, fomiteVector);
            if (initialPotency <= 0f)
            {
                continue;
            }

            int index = _contaminatedVomitFilth.IndexOf(filth);
            if (index >= 0)
            {
                _contaminatedVomitDiseases[index] = resolvedProfile.DiseaseDef;
                _contaminatedVomitTicks[index] = Find.TickManager.TicksGame;
                SetListValue(_contaminatedVomitInitialPotencies, index, initialPotency, 1f);
                SetListValue(_contaminatedVomitSourceDefs, index, sourcePawn.def, null);
            }
            else
            {
                _contaminatedVomitFilth.Add(filth);
                _contaminatedVomitDiseases.Add(resolvedProfile.DiseaseDef);
                _contaminatedVomitTicks.Add(Find.TickManager.TicksGame);
                _contaminatedVomitInitialPotencies.Add(initialPotency);
                _contaminatedVomitSourceDefs.Add(sourcePawn.def);
            }

            ContagionDiagnostics.Record(ContagionDiagnosticCounter.VomitFilthContaminated);
            ContagionDiagnostics.Trace($"Vomit contaminated: {resolvedProfile.DiseaseDef.defName} from {sourcePawn.LabelShortCap} at potency {initialPotency:0.###}.");
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

            RemoveListValue(_contaminatedVomitDiseases, i);
            RemoveListValue(_contaminatedVomitTicks, i);
            RemoveListValue(_contaminatedVomitInitialPotencies, i);
            RemoveListValue(_contaminatedVomitSourceDefs, i);
        }
    }

    public void RunFomiteExposurePass(IReadOnlyList<Pawn> spawnedPawns, Map map)
    {
        if (_contaminatedVomitFilth.Count == 0)
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

                ThingDef sourceDef = contaminationIndex < _contaminatedVomitSourceDefs.Count ? _contaminatedVomitSourceDefs[contaminationIndex] : null;
                float sourceSpeciesFactor = GetFomiteSourceSpeciesFactor(sourceDef, pawn, resolvedProfile.Profile);
                if (sourceSpeciesFactor <= 0f)
                {
                    continue;
                }

                float potencyFactor = GetFomiteInitialPotency(contaminationIndex)
                    * GetFomitePotencyFactor(_contaminatedVomitTicks[contaminationIndex], fomiteVector.potencyDecayPerHour);
                if (potencyFactor <= MinFomitePotency)
                {
                    continue;
                }

                float suppression = ContagionTransmissionUtility.GetSpreadSuppressionFactor(map, resolvedProfile, pawn);

                ContagionDiagnostics.Record(ContagionDiagnosticCounter.FomiteAttempted);
                float chance = ContagionTransmissionUtility.BuildSeederChance(
                    fomiteVector.baseChancePerContact * potencyFactor * sourceSpeciesFactor * suppression,
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

    private float GetFomiteInitialPotency(int index)
    {
        return index < _contaminatedVomitInitialPotencies.Count
            ? Mathf.Max(0f, _contaminatedVomitInitialPotencies[index])
            : 1f;
    }

    private static float GetFomiteSourceSpeciesFactor(ThingDef sourcePawnDef, Pawn target, TransmissionProfile profile)
    {
        RaceProperties sourceRace = sourcePawnDef?.race;
        RaceProperties targetRace = target?.RaceProps;
        if (sourceRace == null || targetRace == null || profile == null)
        {
            return 1f;
        }

        if ((sourceRace.Humanlike && targetRace.Animal) || (sourceRace.Animal && targetRace.Humanlike))
        {
            return Mathf.Max(0f, profile.crossSpeciesTransmissionFactor);
        }

        if (sourceRace.Animal && targetRace.Animal && sourcePawnDef != target.def)
        {
            return Mathf.Max(0f, profile.animalCrossSpeciesFactor);
        }

        return 1f;
    }

    private static void SetListValue<T>(List<T> list, int index, T value, T defaultValue)
    {
        while (list.Count <= index)
        {
            list.Add(defaultValue);
        }

        list[index] = value;
    }

    private static void RemoveListValue<T>(List<T> list, int index)
    {
        if (index < list.Count)
        {
            list.RemoveAt(index);
        }
    }
}
