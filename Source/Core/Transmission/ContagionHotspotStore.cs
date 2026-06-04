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
    private const int TicksPerDay = 60000;

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

    // Records a fresh shed. A shed matching an existing node (same disease, within mergeRadius — 0
    // means same cell only) accumulates additively: fouling a spot repeatedly builds a stronger,
    // longer-lived node, capped at maxPotency. Otherwise a new node is created.
    public void AddOrRefresh(
        IntVec3 cell,
        HediffDef diseaseDef,
        ThingDef sourceDef,
        float potency,
        int mergeRadius,
        int maxPerDisease,
        float decayPerDay,
        float maxPotency)
    {
        int now = Find.TickManager.TicksGame;
        int clampedMergeRadius = Math.Max(0, mergeRadius);
        float newPotency = Math.Max(0f, potency);
        for (int i = 0; i < _entries.Count; i++)
        {
            ContagionHotspotEntry entry = _entries[i];
            if (entry?.DiseaseDef == diseaseDef && entry.Cell.InHorDistOf(cell, clampedMergeRadius))
            {
                // Decay the existing node's potency up to now BEFORE adding the new shed. This is what
                // stops a tiny shed (a rat) from resetting a big, older node (an elephant pat) for
                // free: the old potency keeps fading on its own schedule and the newcomer only adds its
                // own contribution. Tick moves to now so the hard-expiry clock tracks the latest fouling.
                // decayPerDay is the fraction lost per day (daily multiplier = 1 - it), matching
                // ContagionFecalOralTracker.GetHotspotPotency, so the decay-to-now here stays in sync.
                float elapsedDays = Math.Max(0f, (now - entry.Tick) / (float)TicksPerDay);
                float decayedExisting = ContagionRiskMath.HotspotPotencyAfterDecay(entry.Potency, decayPerDay, elapsedDays);
                float combined = ContagionRiskMath.AddHotspotPotency(decayedExisting, newPotency, maxPotency);

                // Attribute the node to whichever shed dominates its current potency, so a small
                // newcomer can't relabel a large node's source species (used for cross-species factors).
                if (newPotency >= decayedExisting || entry.SourceDef == null)
                {
                    entry.SourceDef = sourceDef;
                }

                entry.Cell = cell;
                entry.Tick = now;
                entry.Potency = combined;
                return;
            }
        }

        _entries.Add(new ContagionHotspotEntry(cell, diseaseDef, now, Math.Min(newPotency, Math.Max(0f, maxPotency)), sourceDef));
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
