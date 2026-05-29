using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

internal sealed class ContagionMapSeedingState
{
    private List<HediffDef> _seederCooldownDiseases;

    private List<string> _seederCooldownKeys;

    private List<int> _seederCooldownTicks;

    private List<PendingDiseaseEvent> _pendingEvents;

    private ContagionDiseaseDirector _diseaseDirector;

    public IReadOnlyList<PendingDiseaseEvent> PendingEvents => _pendingEvents;

    public ContagionDiseaseDirector DiseaseDirector => _diseaseDirector;

    public void Rebind(
        List<HediffDef> seederCooldownDiseases,
        List<string> seederCooldownKeys,
        List<int> seederCooldownTicks,
        List<PendingDiseaseEvent> pendingEvents,
        ContagionDiseaseDirector diseaseDirector)
    {
        _seederCooldownDiseases = seederCooldownDiseases;
        _seederCooldownKeys = seederCooldownKeys;
        _seederCooldownTicks = seederCooldownTicks;
        _pendingEvents = pendingEvents;
        _diseaseDirector = diseaseDirector;
    }

    public bool IsAtActiveCaseLimit(Map map, ResolvedTransmissionProfile resolvedProfile, TransmissionSeeder seeder)
    {
        if (resolvedProfile?.Profile == null)
        {
            return false;
        }

        int activeCaseLimit = seeder?.maxActiveCases > 0 ? seeder.maxActiveCases : resolvedProfile.Profile.maxActiveCases;
        return ContagionTransmissionUtility.IsProfileActiveOnMap(map, resolvedProfile, activeCaseLimit);
    }

    public bool CanRunSeeder(Map map, ResolvedTransmissionProfile resolvedProfile, TransmissionSeeder seeder)
    {
        if (resolvedProfile?.Profile == null || seeder == null)
        {
            return false;
        }

        if (IsAtActiveCaseLimit(map, resolvedProfile, seeder))
        {
            return false;
        }

        if (seeder.cooldownDays <= 0f)
        {
            return true;
        }

        string key = GetSeederCooldownKey(seeder);
        int cooldownTicks = Mathf.RoundToInt(seeder.cooldownDays * 60000f);
        int currentTick = Find.TickManager.TicksGame;
        for (int i = 0; i < _seederCooldownDiseases.Count; i++)
        {
            if (_seederCooldownDiseases[i] == resolvedProfile.DiseaseDef && _seederCooldownKeys[i] == key)
            {
                return currentTick - _seederCooldownTicks[i] >= cooldownTicks;
            }
        }

        return true;
    }

    public PendingDiseaseEvent GetPendingEvent(HediffDef diseaseDef)
    {
        if (diseaseDef == null)
        {
            return null;
        }

        for (int i = 0; i < _pendingEvents.Count; i++)
        {
            if (_pendingEvents[i]?.diseaseDef == diseaseDef)
            {
                return _pendingEvents[i];
            }
        }

        return null;
    }

    public void AddPendingEvent(PendingDiseaseEvent pendingEvent)
    {
        if (pendingEvent == null)
        {
            return;
        }

        _pendingEvents.Add(pendingEvent);
    }

    public void RemovePendingEvent(PendingDiseaseEvent pendingEvent)
    {
        if (pendingEvent == null)
        {
            return;
        }

        _pendingEvents.Remove(pendingEvent);
    }

    public void NotifySeederFired(ResolvedTransmissionProfile resolvedProfile, TransmissionSeeder seeder)
    {
        if (resolvedProfile?.DiseaseDef == null || seeder == null || seeder.cooldownDays <= 0f)
        {
            return;
        }

        string key = GetSeederCooldownKey(seeder);
        int currentTick = Find.TickManager.TicksGame;
        for (int i = 0; i < _seederCooldownDiseases.Count; i++)
        {
            if (_seederCooldownDiseases[i] == resolvedProfile.DiseaseDef && _seederCooldownKeys[i] == key)
            {
                _seederCooldownTicks[i] = currentTick;
                return;
            }
        }

        _seederCooldownDiseases.Add(resolvedProfile.DiseaseDef);
        _seederCooldownKeys.Add(key);
        _seederCooldownTicks.Add(currentTick);
    }

    public void DailyTick(Map map)
    {
        _diseaseDirector.DailyTick(map);
    }

    private static string GetSeederCooldownKey(TransmissionSeeder seeder)
    {
        return seeder.GetType().FullName;
    }
}