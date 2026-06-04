using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Contagion;

internal sealed class ContagionHotspotEntry : IExposable
{
    public IntVec3 Cell;

    public HediffDef DiseaseDef;

    public int Tick;

    public float Potency = 1f;

    public ThingDef SourceDef;

    public ContagionHotspotEntry()
    {
    }

    public ContagionHotspotEntry(IntVec3 cell, HediffDef diseaseDef, int tick, float potency, ThingDef sourceDef)
    {
        Cell = cell;
        DiseaseDef = diseaseDef;
        Tick = tick;
        Potency = potency;
        SourceDef = sourceDef;
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref Cell, "cell");
        Scribe_Defs.Look(ref DiseaseDef, "diseaseDef");
        Scribe_Values.Look(ref Tick, "tick");
        Scribe_Values.Look(ref Potency, "potency", 1f);
        Scribe_Defs.Look(ref SourceDef, "sourceDef");
    }
}

internal sealed class ContagionHotspotStore
{
    private List<ContagionHotspotEntry> _entries = new List<ContagionHotspotEntry>();

    public int Count => _entries.Count;

    public void ExposeData(string keyPrefix)
    {
        Scribe_Collections.Look(ref _entries, keyPrefix + "Entries", LookMode.Deep);

        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            _entries ??= new List<ContagionHotspotEntry>();
        }
    }

    public ContagionHotspotEntry Get(int index)
    {
        return index >= 0 && index < _entries.Count ? _entries[index] : null;
    }

    public void AddOrRefresh(IntVec3 cell, HediffDef diseaseDef, ThingDef sourceDef, float potency, int mergeRadius, int maxPerDisease)
    {
        int now = Find.TickManager.TicksGame;
        int clampedMergeRadius = Math.Max(0, mergeRadius);
        for (int i = 0; i < _entries.Count; i++)
        {
            ContagionHotspotEntry entry = _entries[i];
            if (entry?.DiseaseDef == diseaseDef && entry.Cell.InHorDistOf(cell, clampedMergeRadius))
            {
                entry.Cell = cell;
                entry.Tick = now;
                float clampedPotency = Math.Max(0f, potency);
                if (clampedPotency > entry.Potency || entry.SourceDef == null)
                {
                    entry.Potency = clampedPotency;
                    entry.SourceDef = sourceDef;
                }

                return;
            }
        }

        _entries.Add(new ContagionHotspotEntry(cell, diseaseDef, now, Math.Max(0f, potency), sourceDef));
        EnforceDiseaseCap(diseaseDef, maxPerDisease);
    }

    public void Cleanup(Map map, Func<ContagionHotspotEntry, bool> shouldRemove)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            ContagionHotspotEntry entry = _entries[i];
            bool remove = entry == null
                || entry.DiseaseDef == null
                || !entry.Cell.InBounds(map)
                || shouldRemove(entry);

            if (remove)
            {
                _entries.RemoveAt(i);
            }
        }
    }

    private void EnforceDiseaseCap(HediffDef diseaseDef, int maxPerDisease)
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
            ContagionHotspotEntry entry = _entries[i];
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
