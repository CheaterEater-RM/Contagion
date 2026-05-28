using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

public sealed class ContagionDiseaseDirector : IExposable
{
    private const int TicksPerDay = 60000;

    private const float LowBurdenThreshold = 0.05f;

    private const float BurdenDebtDrain = 0.05f;

    private const float RecentSeedingDailyDecay = 0.92f;

    private const float RecentSeedingBaseCost = 1f;

    private const float RecentSeedingCarrierCost = 0.25f;

    private const float PostSeedDebtFactor = 0.4f;

    private const float IncubationBurdenFactor = 0.25f;

    private const float PrisonerBurdenWeight = 0.5f;

    private const float MinChance = 0.001f;

    private const float MaxChance = 0.10f;

    private float _humanPressureDebt;

    private float _animalPressureDebt;

    private float _recentSeeding;

    private int _ticksSinceLastSeededIncident = -1;

    private float _lastNormalizedHumanBurden;

    private float _lastNormalizedAnimalBurden;

    private float _lastDirectorMultiplier = 1f;

    private readonly struct DirectorTuning
    {
        public DirectorTuning(float debtGainPerDay, float maxDebt, float burdenScale, float chanceMultiplier)
        {
            DebtGainPerDay = debtGainPerDay;
            MaxDebt = maxDebt;
            BurdenScale = burdenScale;
            ChanceMultiplier = chanceMultiplier;
        }

        public float DebtGainPerDay { get; }

        public float MaxDebt { get; }

        public float BurdenScale { get; }

        public float ChanceMultiplier { get; }
    }

    public float HumanPressureDebt => _humanPressureDebt;

    public float AnimalPressureDebt => _animalPressureDebt;

    public float RecentSeeding => _recentSeeding;

    public int TicksSinceLastSeededIncident => _ticksSinceLastSeededIncident;

    public float LastNormalizedHumanBurden => _lastNormalizedHumanBurden;

    public float LastNormalizedAnimalBurden => _lastNormalizedAnimalBurden;

    public float LastDirectorMultiplier => _lastDirectorMultiplier;

    public void DailyTick(Map map)
    {
        DirectorTuning tuning = GetTuning();
        CalculateNormalizedBurden(map, out _lastNormalizedHumanBurden, out _lastNormalizedAnimalBurden);

        _recentSeeding *= RecentSeedingDailyDecay;
        if (_recentSeeding < 0.001f)
        {
            _recentSeeding = 0f;
        }

        UpdateDebt(ref _humanPressureDebt, _lastNormalizedHumanBurden, tuning);
        UpdateDebt(ref _animalPressureDebt, _lastNormalizedAnimalBurden, tuning);

        if (_ticksSinceLastSeededIncident >= 0)
        {
            _ticksSinceLastSeededIncident += TicksPerDay;
        }

        _lastDirectorMultiplier = CalculateMultiplierForBurden(_humanPressureDebt, _lastNormalizedHumanBurden, tuning);
        ContagionDiagnostics.UpdateDirectorSummary(
            _humanPressureDebt,
            _animalPressureDebt,
            _recentSeeding,
            _lastNormalizedHumanBurden,
            _lastNormalizedAnimalBurden,
            _lastDirectorMultiplier);
    }

    public float GetChanceMultiplier(TransmissionProfile profile)
    {
        DirectorTuning tuning = GetTuning();
        GetChannelState(profile, out float debt, out float burden);
        _lastDirectorMultiplier = CalculateMultiplierForBurden(debt, burden, tuning);
        ContagionDiagnostics.UpdateDirectorSummary(
            _humanPressureDebt,
            _animalPressureDebt,
            _recentSeeding,
            _lastNormalizedHumanBurden,
            _lastNormalizedAnimalBurden,
            _lastDirectorMultiplier);
        return _lastDirectorMultiplier;
    }

    public float ClampChance(float chance)
    {
        if (chance <= 0f)
        {
            return 0f;
        }

        return Mathf.Clamp(chance, MinChance, MaxChance);
    }

    public void NotifySeeded(TransmissionProfile profile, int carrierCount)
    {
        int safeCarrierCount = Mathf.Max(1, carrierCount);
        _recentSeeding += RecentSeedingBaseCost + RecentSeedingCarrierCost * safeCarrierCount;

        if (UsesAnimalChannel(profile))
        {
            _animalPressureDebt *= PostSeedDebtFactor;
        }
        else
        {
            _humanPressureDebt *= PostSeedDebtFactor;
        }

        _ticksSinceLastSeededIncident = 0;
        ContagionDiagnostics.UpdateDirectorSummary(
            _humanPressureDebt,
            _animalPressureDebt,
            _recentSeeding,
            _lastNormalizedHumanBurden,
            _lastNormalizedAnimalBurden,
            _lastDirectorMultiplier);
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref _humanPressureDebt, "humanPressureDebt");
        Scribe_Values.Look(ref _animalPressureDebt, "animalPressureDebt");
        Scribe_Values.Look(ref _recentSeeding, "recentSeeding");
        Scribe_Values.Look(ref _ticksSinceLastSeededIncident, "ticksSinceLastSeededIncident", -1);
        Scribe_Values.Look(ref _lastNormalizedHumanBurden, "lastNormalizedHumanBurden");
        Scribe_Values.Look(ref _lastNormalizedAnimalBurden, "lastNormalizedAnimalBurden");
        Scribe_Values.Look(ref _lastDirectorMultiplier, "lastDirectorMultiplier", 1f);
    }

    private static void UpdateDebt(ref float debt, float normalizedBurden, DirectorTuning tuning)
    {
        if (normalizedBurden < LowBurdenThreshold)
        {
            debt += tuning.DebtGainPerDay;
        }
        else
        {
            debt -= BurdenDebtDrain * normalizedBurden;
        }

        debt = Mathf.Clamp(debt, 0f, tuning.MaxDebt);
    }

    private float CalculateMultiplierForBurden(float debt, float normalizedBurden, DirectorTuning tuning)
    {
        float debtMultiplier = 1f + Mathf.Max(0f, debt);
        float sicknessSuppression = 1f / (1f + tuning.BurdenScale * Mathf.Max(0f, normalizedBurden));
        float recentSeedingSuppression = 1f / Mathf.Sqrt(1f + Mathf.Max(0f, _recentSeeding));
        return Mathf.Max(0f, tuning.ChanceMultiplier * debtMultiplier * sicknessSuppression * recentSeedingSuppression);
    }

    private void GetChannelState(TransmissionProfile profile, out float debt, out float burden)
    {
        if (UsesAnimalChannel(profile))
        {
            debt = _animalPressureDebt;
            burden = _lastNormalizedAnimalBurden;
            return;
        }

        debt = _humanPressureDebt;
        burden = _lastNormalizedHumanBurden;
    }

    private static bool UsesAnimalChannel(TransmissionProfile profile)
    {
        return profile != null && profile.affectsAnimals && !profile.affectsHumans;
    }

    private static DirectorTuning GetTuning()
    {
        return (Contagion_Mod.Settings?.difficulty ?? ContagionDifficulty.Normal) switch
        {
            ContagionDifficulty.Easier => new DirectorTuning(0.02f, 3f, 6f, 0.7f),
            ContagionDifficulty.Harder => new DirectorTuning(0.045f, 7f, 3f, 1.25f),
            _ => new DirectorTuning(0.03f, 5f, 4f, 1f)
        };
    }

    private static void CalculateNormalizedBurden(Map map, out float humanBurden, out float animalBurden)
    {
        humanBurden = 0f;
        animalBurden = 0f;
        float humanDenominator = 0f;
        float animalDenominator = 0f;

        IReadOnlyList<Pawn> spawnedPawns = map?.mapPawns?.AllPawnsSpawned;
        if (spawnedPawns == null)
        {
            return;
        }

        for (int i = 0; i < spawnedPawns.Count; i++)
        {
            Pawn pawn = spawnedPawns[i];
            if (pawn == null || pawn.Dead || pawn.health?.hediffSet == null)
            {
                continue;
            }

            if (IsHumanBurdenPawn(pawn, out float humanWeight))
            {
                humanDenominator += humanWeight;
                humanBurden += CalculatePawnDiseaseBurden(pawn, animalChannel: false) * humanWeight;
            }

            if (IsAnimalBurdenPawn(pawn))
            {
                animalDenominator += 1f;
                animalBurden += CalculatePawnDiseaseBurden(pawn, animalChannel: true);
            }
        }

        humanBurden = humanDenominator > 0f ? humanBurden / humanDenominator : 0f;
        animalBurden = animalDenominator > 0f ? animalBurden / animalDenominator : 0f;
    }

    private static bool IsHumanBurdenPawn(Pawn pawn, out float weight)
    {
        weight = 0f;
        if (pawn?.RaceProps?.Humanlike != true)
        {
            return false;
        }

        if (pawn.Faction == Faction.OfPlayer)
        {
            weight = 1f;
            return true;
        }

        if (pawn.IsPrisonerOfColony)
        {
            weight = PrisonerBurdenWeight;
            return true;
        }

        return false;
    }

    private static bool IsAnimalBurdenPawn(Pawn pawn)
    {
        return pawn?.RaceProps?.Animal == true && pawn.Faction == Faction.OfPlayer;
    }

    private static float CalculatePawnDiseaseBurden(Pawn pawn, bool animalChannel)
    {
        float burden = 0f;
        List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
        for (int i = 0; i < hediffs.Count; i++)
        {
            Hediff hediff = hediffs[i];
            if (hediff is Hediff_ContagionIncubation incubation)
            {
                if (incubation.TargetDiseaseDef != null
                    && DiseaseProfileCache.TryGetResolvedProfile(incubation.TargetDiseaseDef, out ResolvedTransmissionProfile incubationProfile)
                    && UsesAnimalChannel(incubationProfile.Profile) == animalChannel)
                {
                    burden += Mathf.Clamp01(incubation.Severity) * IncubationBurdenFactor * GetDiseaseDangerWeight(incubationProfile);
                }

                continue;
            }

            if (hediff?.def == null || !DiseaseProfileCache.TryGetResolvedProfile(hediff.def, out ResolvedTransmissionProfile resolvedProfile))
            {
                continue;
            }

            if (UsesAnimalChannel(resolvedProfile.Profile) != animalChannel)
            {
                continue;
            }

            burden += Mathf.Clamp01(hediff.Severity) * GetDiseaseDangerWeight(resolvedProfile);
        }

        return burden;
    }

    private static float GetDiseaseDangerWeight(ResolvedTransmissionProfile resolvedProfile)
    {
        string defName = resolvedProfile?.DiseaseDef?.defName;
        if (defName == "GutWorms")
        {
            return 0.75f;
        }

        if (defName == "Plague"
            || defName == "Animal_Plague"
            || defName == "Malaria"
            || defName == "SleepingSickness")
        {
            return 1.5f;
        }

        return 1f;
    }
}
