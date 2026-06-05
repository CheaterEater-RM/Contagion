using System.Collections.Generic;
using Verse;

namespace Contagion;

internal sealed class ContagionCorpseExposureProcessor
{
    private readonly Map _map;

    private readonly Dictionary<int, float> _pathDistanceByCell = new Dictionary<int, float>();

    private readonly Queue<IntVec3> _pathOpenCells = new Queue<IntVec3>();

    public ContagionCorpseExposureProcessor(Map map)
    {
        _map = map;
    }

    public void RunCorpseExposurePass(IReadOnlyList<Pawn> spawnedPawns, IReadOnlyList<Pawn> duePawns, int deltaTicks, int bucket)
    {
        if (_map == null || spawnedPawns == null || spawnedPawns.Count == 0)
        {
            return;
        }

        ProcessSpawnedCorpses(spawnedPawns, deltaTicks, bucket);
        ProcessCarriedCorpses(spawnedPawns, duePawns, deltaTicks);
    }

    private void ProcessSpawnedCorpses(IReadOnlyList<Pawn> spawnedPawns, int deltaTicks, int bucket)
    {
        List<Thing> corpses = _map.listerThings?.ThingsInGroup(ThingRequestGroup.Corpse);
        if (corpses == null || corpses.Count == 0)
        {
            return;
        }

        for (int corpseIndex = 0; corpseIndex < corpses.Count; corpseIndex++)
        {
            if (corpses[corpseIndex] is not Corpse corpse
                || !corpse.Spawned
                || corpse.Map != _map
                || !ContagionTransmissionStaggerUtility.IsDueThisTick(corpse, _map, deltaTicks, bucket))
            {
                continue;
            }

            if (!ContagionCorpseExposureUtility.TryGetCorpseFleaVector(
                corpse,
                out ResolvedTransmissionProfile resolvedProfile,
                out Vector_CorpseFlea vector))
            {
                continue;
            }

            ContagionCorpseExposureUtility.UpdateCorpseFleas(corpse, resolvedProfile, vector, deltaTicks);

            // Skip the path flood-fill for corpses with no living target within range — common for
            // corpse piles away from pawns. Path distance >= euclidean, so an euclidean pre-filter
            // never drops a reachable target.
            if (!AnyExposableWithin(corpse.Position, vector.maxRange, spawnedPawns))
            {
                continue;
            }

            ContagionTransmissionUtility.CollectReachablePathDistances(
                _map,
                corpse.Position,
                vector.maxRange,
                _pathDistanceByCell,
                _pathOpenCells);

            for (int pawnIndex = 0; pawnIndex < spawnedPawns.Count; pawnIndex++)
            {
                Pawn target = spawnedPawns[pawnIndex];
                if (!CanExpose(target)
                    || !_pathDistanceByCell.TryGetValue(_map.cellIndices.CellToIndex(target.Position), out float pathDistance))
                {
                    continue;
                }

                float distanceFactor = ContagionTransmissionUtility.GetDistanceFactor(pathDistance, vector.distanceFalloffRate);
                ContagionCorpseExposureUtility.TryApplyFleaExposure(
                    target,
                    corpse,
                    resolvedProfile,
                    vector,
                    vector.baseChancePerCheck,
                    distanceFactor);
            }
        }
    }

    private void ProcessCarriedCorpses(IReadOnlyList<Pawn> spawnedPawns, IReadOnlyList<Pawn> duePawns, int deltaTicks)
    {
        for (int carrierIndex = 0; carrierIndex < duePawns.Count; carrierIndex++)
        {
            Pawn carrier = duePawns[carrierIndex];
            if (!CanExpose(carrier)
                || carrier.carryTracker?.CarriedThing is not Corpse corpse)
            {
                continue;
            }

            if (ContagionCorpseExposureUtility.TryGetCorpseFleaVector(
                corpse,
                out ResolvedTransmissionProfile fleaProfile,
                out Vector_CorpseFlea fleaVector))
            {
                ContagionCorpseExposureUtility.UpdateCorpseFleas(corpse, fleaProfile, fleaVector, deltaTicks);
                ContagionCorpseExposureUtility.TryApplyFleaExposure(
                    carrier,
                    corpse,
                    fleaProfile,
                    fleaVector,
                    fleaVector.carriedBaseChancePerCheck,
                    1f);

                ContagionTransmissionUtility.CollectReachablePathDistances(
                    _map,
                    carrier.Position,
                    fleaVector.carriedRange,
                    _pathDistanceByCell,
                    _pathOpenCells);

                for (int targetIndex = 0; targetIndex < spawnedPawns.Count; targetIndex++)
                {
                    Pawn target = spawnedPawns[targetIndex];
                    if (target == carrier
                        || !CanExpose(target)
                        || !_pathDistanceByCell.TryGetValue(_map.cellIndices.CellToIndex(target.Position), out float pathDistance))
                    {
                        continue;
                    }

                    float distanceFactor = ContagionTransmissionUtility.GetDistanceFactor(pathDistance, fleaVector.distanceFalloffRate);
                    ContagionCorpseExposureUtility.TryApplyFleaExposure(
                        target,
                        corpse,
                        fleaProfile,
                        fleaVector,
                        fleaVector.baseChancePerCheck,
                        distanceFactor);
                }
            }

            if (ContagionCorpseExposureUtility.TryGetCorpseFluidVector(
                corpse,
                out ResolvedTransmissionProfile fluidProfile,
                out Vector_CorpseFluid fluidVector))
            {
                ContagionCorpseExposureUtility.TryApplyFluidExposure(
                    carrier,
                    corpse,
                    fluidProfile,
                    fluidVector,
                    ContagionCorpseFluidExposureKind.Carry);
            }
        }
    }

    private bool CanExpose(Pawn pawn)
    {
        return pawn != null && !pawn.Dead && pawn.Spawned && pawn.Map == _map;
    }

    private bool AnyExposableWithin(IntVec3 origin, float range, IReadOnlyList<Pawn> spawnedPawns)
    {
        for (int i = 0; i < spawnedPawns.Count; i++)
        {
            Pawn pawn = spawnedPawns[i];
            if (CanExpose(pawn) && origin.InHorDistOf(pawn.Position, range))
            {
                return true;
            }
        }

        return false;
    }
}
