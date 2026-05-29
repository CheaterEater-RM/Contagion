using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

internal sealed class ContagionMapDeveloperDiagnosticsController
{
    private const int DeveloperTraceMaxCount = 128;

    private readonly Map _map;

    private HediffDef _forcedArrivalDisease;

    private readonly List<ContagionTransmissionTrace> _transmissionTraces = new List<ContagionTransmissionTrace>();

    private bool _traceCaptureEnabled = true;

    private Pawn _hoverSourcePawn;

    private Pawn _hoverTargetPawn;

    private int _hoverFrame = -1;

    public ContagionMapDeveloperDiagnosticsController(Map map)
    {
        _map = map;
    }

    public HediffDef ForcedArrivalDisease => _forcedArrivalDisease;

    public IReadOnlyList<ContagionTransmissionTrace> TransmissionTraces => _transmissionTraces;

    public bool TraceCaptureEnabled => _traceCaptureEnabled;

    public void ArmForcedArrival(HediffDef diseaseDef)
    {
        if (diseaseDef == null)
        {
            return;
        }

        _forcedArrivalDisease = diseaseDef;
    }

    public void ClearForcedArrival()
    {
        _forcedArrivalDisease = null;
    }

    public void RecordTransmissionTrace(
        Pawn sourcePawn,
        Pawn targetPawn,
        HediffDef diseaseDef,
        ContagionDebugVectorKind vectorKind)
    {
        if (Contagion_Mod.Settings?.DeveloperDiagnosticsEnabled != true
            || !_traceCaptureEnabled
            || sourcePawn == null
            || targetPawn == null
            || sourcePawn == targetPawn
            || diseaseDef == null
            || sourcePawn.Map != _map
            || targetPawn.Map != _map)
        {
            return;
        }

        PruneTransmissionTraces();
        for (int i = 0; i < _transmissionTraces.Count; i++)
        {
            if (_transmissionTraces[i]?.Matches(sourcePawn, targetPawn, diseaseDef, vectorKind) == true)
            {
                return;
            }
        }

        if (_transmissionTraces.Count >= DeveloperTraceMaxCount)
        {
            _transmissionTraces.RemoveAt(0);
        }

        _transmissionTraces.Add(new ContagionTransmissionTrace(
            sourcePawn,
            targetPawn,
            diseaseDef,
            vectorKind,
            Find.TickManager.TicksGame));
    }

    public void ClearAllTraces()
    {
        _transmissionTraces.Clear();
    }

    public void ToggleTraceCapture()
    {
        _traceCaptureEnabled = !_traceCaptureEnabled;
    }

    public void ClearTracesForPawn(Pawn pawn)
    {
        if (pawn == null || _transmissionTraces.Count == 0)
        {
            return;
        }

        _transmissionTraces.RemoveAll(trace => trace == null || trace.Contains(pawn));
    }

    public bool HasTracesForPawn(Pawn pawn)
    {
        if (pawn == null)
        {
            return false;
        }

        PruneTransmissionTraces();
        for (int i = 0; i < _transmissionTraces.Count; i++)
        {
            if (_transmissionTraces[i]?.Contains(pawn) == true)
            {
                return true;
            }
        }

        return false;
    }

    public void SetHoverPair(Pawn sourcePawn, Pawn targetPawn)
    {
        if (Contagion_Mod.Settings?.DeveloperDiagnosticsEnabled != true
            || sourcePawn == null
            || targetPawn == null
            || sourcePawn == targetPawn
            || sourcePawn.Map != _map
            || targetPawn.Map != _map)
        {
            ClearHoverPair();
            return;
        }

        _hoverSourcePawn = sourcePawn;
        _hoverTargetPawn = targetPawn;
        _hoverFrame = Time.frameCount;
    }

    public bool TryGetHoverPair(out Pawn sourcePawn, out Pawn targetPawn)
    {
        sourcePawn = null;
        targetPawn = null;

        if (Contagion_Mod.Settings?.DeveloperDiagnosticsEnabled != true)
        {
            ClearHoverPair();
            return false;
        }

        if (_hoverSourcePawn == null
            || _hoverTargetPawn == null
            || Time.frameCount - _hoverFrame > 1
            || _hoverSourcePawn.Map != _map
            || _hoverTargetPawn.Map != _map
            || !_hoverSourcePawn.Spawned
            || !_hoverTargetPawn.Spawned)
        {
            ClearHoverPair();
            return false;
        }

        sourcePawn = _hoverSourcePawn;
        targetPawn = _hoverTargetPawn;
        return true;
    }

    public void ClearHoverPair()
    {
        _hoverSourcePawn = null;
        _hoverTargetPawn = null;
        _hoverFrame = -1;
    }

    public void Update()
    {
        PruneTransmissionTraces();

        if (Contagion_Mod.Settings?.DeveloperDiagnosticsEnabled != true || Find.CurrentMap != _map)
        {
            ClearHoverPair();
            return;
        }

        if (TryGetHoverPair(out Pawn hoverSourcePawn, out Pawn hoverTargetPawn))
        {
            ContagionDeveloperOverlayDrawer.DrawHoverLine(hoverSourcePawn, hoverTargetPawn);
        }

        if (_traceCaptureEnabled)
        {
            ContagionDeveloperOverlayDrawer.DrawTransmissionTraces(_map, _transmissionTraces);
        }
    }

    private void PruneTransmissionTraces()
    {
        if (_transmissionTraces.Count == 0)
        {
            return;
        }

        _transmissionTraces.RemoveAll(trace => trace == null || !trace.IsValidFor(_map));
    }
}