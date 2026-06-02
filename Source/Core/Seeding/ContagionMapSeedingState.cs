using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

internal sealed class ContagionMapSeedingState : IExposable
{
    // One seeder cooldown record. Replaces three parallel lists (disease/key/tick) whose indices
    // had to stay in lockstep — a single Deep-scribed entry can never desync, and a dropped entry
    // (e.g. its HediffDef was removed by a mod update) takes its key and tick with it.
    private sealed class SeederCooldownEntry : IExposable
    {
        public HediffDef diseaseDef;

        public string seederKey;

        public int firedTick;

        public SeederCooldownEntry()
        {
        }

        public SeederCooldownEntry(HediffDef diseaseDef, string seederKey, int firedTick)
        {
            this.diseaseDef = diseaseDef;
            this.seederKey = seederKey;
            this.firedTick = firedTick;
        }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref diseaseDef, "diseaseDef");
            Scribe_Values.Look(ref seederKey, "seederKey");
            Scribe_Values.Look(ref firedTick, "firedTick");
        }
    }

    private List<SeederCooldownEntry> _seederCooldowns = new List<SeederCooldownEntry>();

    private List<PendingDiseaseEvent> _pendingEvents = new List<PendingDiseaseEvent>();

    private ContagionDiseaseDirector _diseaseDirector = new ContagionDiseaseDirector();

    public IReadOnlyList<PendingDiseaseEvent> PendingEvents => _pendingEvents;

    public ContagionDiseaseDirector DiseaseDirector => _diseaseDirector;

    public void ExposeData()
    {
        Scribe_Collections.Look(ref _seederCooldowns, "seederCooldowns", LookMode.Deep);
        Scribe_Collections.Look(ref _pendingEvents, "pendingEvents", LookMode.Deep);
        Scribe_Deep.Look(ref _diseaseDirector, "diseaseDirector");

        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            _seederCooldowns ??= new List<SeederCooldownEntry>();
            // Drop entries whose disease def no longer resolves (mod update / removed disease).
            _seederCooldowns.RemoveAll(entry => entry == null || entry.diseaseDef == null);
            _pendingEvents ??= new List<PendingDiseaseEvent>();
            _diseaseDirector ??= new ContagionDiseaseDirector();
        }
    }

    public bool IsAtActiveCaseLimit(Map map, ResolvedTransmissionProfile resolvedProfile, TransmissionSeeder seeder)
    {
        if (resolvedProfile?.Profile == null)
        {
            return false;
        }

        return ContagionTransmissionUtility.GetRemainingActiveCaseCapacity(map, resolvedProfile) <= 0;
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
        for (int i = 0; i < _seederCooldowns.Count; i++)
        {
            SeederCooldownEntry entry = _seederCooldowns[i];
            if (entry.diseaseDef == resolvedProfile.DiseaseDef && entry.seederKey == key)
            {
                return currentTick - entry.firedTick >= cooldownTicks;
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
        for (int i = 0; i < _seederCooldowns.Count; i++)
        {
            SeederCooldownEntry entry = _seederCooldowns[i];
            if (entry.diseaseDef == resolvedProfile.DiseaseDef && entry.seederKey == key)
            {
                entry.firedTick = currentTick;
                return;
            }
        }

        _seederCooldowns.Add(new SeederCooldownEntry(resolvedProfile.DiseaseDef, key, currentTick));
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
