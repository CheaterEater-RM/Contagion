using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Contagion;

internal sealed class ContagionFilthEntry : IExposable
{
    public Filth Filth;

    public HediffDef DiseaseDef;

    public int Tick;

    public float Potency = 1f;

    public ThingDef SourceDef;

    public ContagionFilthEntry()
    {
    }

    public ContagionFilthEntry(Filth filth, HediffDef diseaseDef, int tick, float potency, ThingDef sourceDef)
    {
        Filth = filth;
        DiseaseDef = diseaseDef;
        Tick = tick;
        Potency = potency;
        SourceDef = sourceDef;
    }

    public void ExposeData()
    {
        Scribe_References.Look(ref Filth, "filth");
        Scribe_Defs.Look(ref DiseaseDef, "diseaseDef");
        Scribe_Values.Look(ref Tick, "tick");
        Scribe_Values.Look(ref Potency, "potency", 1f);
        Scribe_Defs.Look(ref SourceDef, "sourceDef");
    }
}

internal sealed class ContagionFilthStore
{
    private List<ContagionFilthEntry> _entries = new List<ContagionFilthEntry>();

    public int Count => _entries.Count;

    public void ExposeData(string keyPrefix)
    {
        Scribe_Collections.Look(ref _entries, keyPrefix + "Entries", LookMode.Deep);

        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            _entries ??= new List<ContagionFilthEntry>();
        }
    }

    public ContagionFilthEntry Get(int index)
    {
        return index >= 0 && index < _entries.Count ? _entries[index] : null;
    }

    public void AddOrUpdate(Filth filth, HediffDef diseaseDef, ThingDef sourceDef, float potency)
    {
        ContagionFilthEntry entry = FindByFilth(filth);
        int now = Find.TickManager.TicksGame;
        if (entry != null)
        {
            entry.DiseaseDef = diseaseDef;
            entry.Tick = now;
            entry.Potency = potency;
            entry.SourceDef = sourceDef;
            return;
        }

        _entries.Add(new ContagionFilthEntry(filth, diseaseDef, now, potency, sourceDef));
    }

    public void Cleanup(Map map, Func<ContagionFilthEntry, bool> shouldRemove)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            ContagionFilthEntry entry = _entries[i];
            bool remove = entry?.Filth == null
                || entry.Filth.Destroyed
                || !entry.Filth.Spawned
                || entry.Filth.Map != map
                || entry.DiseaseDef == null
                || shouldRemove(entry);

            if (remove)
            {
                _entries.RemoveAt(i);
            }
        }
    }

    public void EnforceDiseaseCap(HediffDef diseaseDef, int maxPerDisease)
    {
        if (maxPerDisease <= 0)
        {
            return;
        }

        while (CountDiseaseEntries(diseaseDef) > maxPerDisease)
        {
            int oldestIndex = FindOldestDiseaseIndex(diseaseDef);
            if (oldestIndex < 0)
            {
                return;
            }

            _entries.RemoveAt(oldestIndex);
        }
    }

    private ContagionFilthEntry FindByFilth(Filth filth)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i]?.Filth == filth)
            {
                return _entries[i];
            }
        }

        return null;
    }

    private int CountDiseaseEntries(HediffDef diseaseDef)
    {
        int count = 0;
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i]?.DiseaseDef == diseaseDef)
            {
                count++;
            }
        }

        return count;
    }

    private int FindOldestDiseaseIndex(HediffDef diseaseDef)
    {
        int oldestIndex = -1;
        int oldestTick = int.MaxValue;
        for (int i = 0; i < _entries.Count; i++)
        {
            ContagionFilthEntry entry = _entries[i];
            if (entry?.DiseaseDef != diseaseDef)
            {
                continue;
            }

            if (entry.Tick < oldestTick)
            {
                oldestTick = entry.Tick;
                oldestIndex = i;
            }
        }

        return oldestIndex;
    }
}
