using System.Collections.Generic;

namespace Contagion;

internal sealed class ContagionMapSlaughterOverrideTracker
{
    private const int SlaughterFlagMaxAgeTicks = 60000;

    private readonly Dictionary<int, int> _forceRotPawnIds = new Dictionary<int, int>();

    private readonly Dictionary<int, int> _butcherBypassPawnIds = new Dictionary<int, int>();

    public void ArmForceRot(int pawnId, int currentTick)
    {
        _butcherBypassPawnIds.Remove(pawnId);
        _forceRotPawnIds[pawnId] = currentTick;
    }

    public bool ConsumeForceRot(int pawnId)
    {
        return _forceRotPawnIds.Remove(pawnId);
    }

    public void ArmButcherBypass(int pawnId, int currentTick)
    {
        _forceRotPawnIds.Remove(pawnId);
        _butcherBypassPawnIds[pawnId] = currentTick;
    }

    public bool ConsumeButcherBypass(int pawnId)
    {
        return _butcherBypassPawnIds.Remove(pawnId);
    }

    public void CleanupStaleEntries(int currentTick)
    {
        CleanupEntries(_forceRotPawnIds, currentTick);
        CleanupEntries(_butcherBypassPawnIds, currentTick);
    }

    private static void CleanupEntries(Dictionary<int, int> entries, int currentTick)
    {
        if (entries.Count == 0)
        {
            return;
        }

        List<int> staleIds = null;
        foreach (KeyValuePair<int, int> entry in entries)
        {
            if (currentTick - entry.Value <= SlaughterFlagMaxAgeTicks)
            {
                continue;
            }

            staleIds ??= new List<int>();
            staleIds.Add(entry.Key);
        }

        if (staleIds == null)
        {
            return;
        }

        for (int i = 0; i < staleIds.Count; i++)
        {
            entries.Remove(staleIds[i]);
        }
    }
}