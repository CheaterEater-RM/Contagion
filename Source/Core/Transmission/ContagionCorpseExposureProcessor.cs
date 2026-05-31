using System.Collections.Generic;
using Verse;

namespace Contagion;

internal sealed class ContagionCorpseExposureProcessor
{
    private readonly Map _map;

    public ContagionCorpseExposureProcessor(Map map)
    {
        _map = map;
    }

    public void RunCorpseExposurePass(IReadOnlyList<Pawn> spawnedPawns, int deltaTicks)
    {
        if (_map == null || spawnedPawns == null || spawnedPawns.Count == 0)
        {
            return;
        }

        ProcessSpawnedCorpses(spawnedPawns, deltaTicks);
        ProcessCarriedCorpses(spawnedPawns, deltaTicks);
    }

    private void ProcessSpawnedCorpses(IReadOnlyList<Pawn> spawnedPawns, int deltaTicks)
    {
        List<Thing> corpses = _map.listerThings?.ThingsInGroup(ThingRequestGroup.Corpse);
        if (corpses == null || corpses.Count == 0)
        {
            return;
        }

        for (int corpseIndex = 0; corpseIndex < corpses.Count; corpseIndex++)
        {
            if (corpses[corpseIndex] is not Corpse corpse || !corpse.Spawned || corpse.Map != _map)
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
            for (int pawnIndex = 0; pawnIndex < spawnedPawns.Count; pawnIndex++)
            {
                Pawn target = spawnedPawns[pawnIndex];
                if (!CanExpose(target) || !target.Position.InHorDistOf(corpse.Position, vector.maxRange))
                {
                    continue;
                }

                float distance = ContagionTransmissionUtility.GetHorizontalDistance(corpse.Position, target.Position);
                float distanceFactor = ContagionTransmissionUtility.GetDistanceFactor(distance, vector.distanceFalloffRate);
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

    private void ProcessCarriedCorpses(IReadOnlyList<Pawn> spawnedPawns, int deltaTicks)
    {
        for (int carrierIndex = 0; carrierIndex < spawnedPawns.Count; carrierIndex++)
        {
            Pawn carrier = spawnedPawns[carrierIndex];
            if (!CanExpose(carrier) || carrier.carryTracker?.CarriedThing is not Corpse corpse)
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

                for (int targetIndex = 0; targetIndex < spawnedPawns.Count; targetIndex++)
                {
                    Pawn target = spawnedPawns[targetIndex];
                    if (target == carrier
                        || !CanExpose(target)
                        || !target.Position.InHorDistOf(carrier.Position, fleaVector.carriedRange))
                    {
                        continue;
                    }

                    float distance = ContagionTransmissionUtility.GetHorizontalDistance(carrier.Position, target.Position);
                    float distanceFactor = ContagionTransmissionUtility.GetDistanceFactor(distance, fleaVector.distanceFalloffRate);
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
}
