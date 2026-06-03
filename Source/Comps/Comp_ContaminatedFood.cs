using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

public sealed class CompProperties_ContaminatedFood : CompProperties
{
    public CompProperties_ContaminatedFood()
    {
        compClass = typeof(Comp_ContaminatedFood);
    }
}

public sealed class Comp_ContaminatedFood : ThingComp
{
    private const float MinCleanlinessFactor = 0.1f;

    private const float MinContaminationFactor = 0f;

    private const float MaxCleanlinessFactor = 3f;

    private const int TicksPerDay = 60000;

    private HediffDef _contaminatedDiseaseDef;

    private float _contaminationFactor = 1f;

    private int _contaminationTick = -1;

    // Session-only trace-graph link: the node id that contaminated this stack. Carries the
    // infection chain forward (corpse/bench → meat → meal → eater). Deliberately NOT scribed —
    // the graph it points into is rebuilt fresh each session.
    public int sourceTraceNodeId = -1;

    public bool IsContaminated => _contaminatedDiseaseDef != null;

    public HediffDef ContaminatedDiseaseDef => _contaminatedDiseaseDef;

    public float ContaminationFactor => _contaminationFactor;

    // Called by the butchering postfix to stamp contamination from an infected animal's products.
    public void SetContamination(HediffDef diseaseDef, float factor)
    {
        if (diseaseDef == null || factor <= 0f)
        {
            return;
        }

        _contaminatedDiseaseDef = diseaseDef;
        _contaminationFactor = Mathf.Clamp(factor, MinContaminationFactor, MaxCleanlinessFactor);
        _contaminationTick = Find.TickManager.TicksGame;
        EnsureTraceNode();
    }

    public override void Notify_RecipeProduced(Pawn pawn)
    {
        base.Notify_RecipeProduced(pawn);
        ClearContamination();

        if (pawn == null)
        {
            return;
        }

        foreach (ResolvedTransmissionProfile resolvedProfile in ContagionDiseaseUtility.GetContagiousProfiles(pawn))
        {
            if (!ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Foodborne foodborneVector))
            {
                continue;
            }

            float sourceInfectivity = ContagionTransmissionUtility.GetSourceInfectivity(pawn, resolvedProfile, foodborneVector);
            if (sourceInfectivity <= 0f)
            {
                continue;
            }

            // Typhoid Mary control: PPE on the infected cook (mask + gloves, source side) cuts how much
            // contamination they bake into the meal. A sealed-suit cook contaminates ~nothing.
            float cookSourceProtection = ContagionApparelProtectionUtility.GetCookSourceProtectionFactor(pawn, foodborneVector.cookSourceProtection);
            _contaminatedDiseaseDef = resolvedProfile.DiseaseDef;
            _contaminationFactor = sourceInfectivity * GetCleanlinessFactor(pawn.GetRoom(), foodborneVector.cleanlinessImpact) * cookSourceProtection;
            _contaminationTick = Find.TickManager.TicksGame;
            // Trace lineage: a contagious cook contaminates the bench they work at, which in turn
            // contaminates the meal — so route cook → stove → meal. The meal's own node is created
            // when it spawns (PostSpawnSetup), linking from the bench node recorded here.
            int cookNodeId = ContagionTrace.EnsureNode(pawn, _contaminatedDiseaseDef);
            sourceTraceNodeId = ContagionTrace.RouteThroughBench(pawn, _contaminatedDiseaseDef, cookNodeId, ContagionDebugVectorKind.Cooking);
            EnsureTraceNode();
            ContagionDiagnostics.Record(ContagionDiagnosticCounter.MealsContaminated);
            ContagionDiagnostics.Trace($"Meal contaminated: {_contaminatedDiseaseDef.defName} by {pawn.LabelShortCap}.");
            return;
        }
    }

    public override void PostSpawnSetup(bool respawningAfterLoad)
    {
        base.PostSpawnSetup(respawningAfterLoad);

        // Any contaminated stack gets its own trace node when it spawns, so meat and meals are
        // always marked while they exist. sourceTraceNodeId is not saved, so this only fires for
        // food produced in the current session — which is the target.
        if (respawningAfterLoad)
        {
            return;
        }

        EnsureTraceNode();
    }

    // Creates (or reuses) this stack's trace node and links it from any recorded upstream source
    // (butchery bench, ingredient meat, contagious cook). No-op if not spawned, uncontaminated, or
    // tracing is off. Called from every point contamination is gained so meat/meal nodes appear
    // reliably — including stack merges, where PostSpawnSetup never re-runs.
    private void EnsureTraceNode()
    {
        if (parent == null || !parent.SpawnedOrAnyParentSpawned || _contaminatedDiseaseDef == null)
        {
            return;
        }

        int selfNode = ContagionTrace.EnsureNode(parent, _contaminatedDiseaseDef);
        if (selfNode < 0)
        {
            return;
        }

        if (sourceTraceNodeId >= 0 && sourceTraceNodeId != selfNode)
        {
            ContagionTrace.Edge(parent.MapHeld, sourceTraceNodeId, selfNode, ContagionDebugVectorKind.Foodborne);
        }

        sourceTraceNodeId = selfNode;
    }

    // When a contaminated stack is split (stack-limit overflow on placement, hauling, trading),
    // Thing.SplitOff creates a fresh Thing that copies only stackCount/HitPoints — comp state is
    // lost, so the split-off piece would be clean. Carry contamination onto the new piece so every
    // stack descended from infected meat stays infected. The piece is a sibling of the source, so
    // it inherits the source's *upstream* origin (e.g. the butcher table) rather than pointing at
    // the source stack itself — otherwise a split spawned stack reads as a confusing meat→meat link.
    // Once the source has spawned its sourceTraceNodeId is its own node, so we look up its upstream
    // edge; before spawn (placement-time split) sourceTraceNodeId is still the real origin.
    public override void PostSplitOff(Thing piece)
    {
        base.PostSplitOff(piece);

        if (_contaminatedDiseaseDef == null || piece == parent)
        {
            return;
        }

        Comp_ContaminatedFood pieceComp = piece.TryGetComp<Comp_ContaminatedFood>();
        if (pieceComp == null)
        {
            return;
        }

        pieceComp._contaminatedDiseaseDef = _contaminatedDiseaseDef;
        pieceComp._contaminationFactor = _contaminationFactor;
        pieceComp._contaminationTick = _contaminationTick;

        int upstream = ContagionTrace.GetUpstreamNode(parent, _contaminatedDiseaseDef);
        pieceComp.sourceTraceNodeId = upstream >= 0 ? upstream : sourceTraceNodeId;
        pieceComp.EnsureTraceNode();
    }

    public override void PreAbsorbStack(Thing otherStack, int count)
    {
        base.PreAbsorbStack(otherStack, count);

        Comp_ContaminatedFood otherComp = otherStack.TryGetComp<Comp_ContaminatedFood>();
        if (otherComp?._contaminatedDiseaseDef == null)
        {
            return;
        }

        if (_contaminatedDiseaseDef == null)
        {
            _contaminatedDiseaseDef = otherComp._contaminatedDiseaseDef;
            _contaminationFactor = otherComp._contaminationFactor;
            _contaminationTick = otherComp._contaminationTick;
            if (sourceTraceNodeId < 0)
            {
                sourceTraceNodeId = otherComp.sourceTraceNodeId;
            }

            // A previously-clean spawned stack just became contaminated by absorbing tainted food;
            // PostSpawnSetup already ran, so create its trace node now.
            EnsureTraceNode();
            return;
        }

        if (_contaminatedDiseaseDef == otherComp._contaminatedDiseaseDef)
        {
            // Take max factor (worse contamination) but the newer timestamp (fresher item).
            if (otherComp._contaminationFactor > _contaminationFactor)
            {
                _contaminationFactor = otherComp._contaminationFactor;
            }

            // Newer tick = more recently contaminated; use max so the expiry is based on the
            // freshest contribution rather than the oldest piece in the merged stack.
            if (otherComp._contaminationTick > _contaminationTick)
            {
                _contaminationTick = otherComp._contaminationTick;
            }

            return;
        }

        if (!TryGetDiseaseSeverity(_contaminatedDiseaseDef, out float currentSeverity)
            || !TryGetDiseaseSeverity(otherComp._contaminatedDiseaseDef, out float otherSeverity))
        {
            return;
        }

        if (otherSeverity > currentSeverity
            || (Mathf.Approximately(otherSeverity, currentSeverity) && otherComp._contaminationFactor > _contaminationFactor))
        {
            _contaminatedDiseaseDef = otherComp._contaminatedDiseaseDef;
            _contaminationFactor = otherComp._contaminationFactor;
            _contaminationTick = otherComp._contaminationTick;
        }
    }

    public override void PostIngested(Pawn ingester)
    {
        base.PostIngested(ingester);

        if (ingester == null || _contaminatedDiseaseDef == null)
        {
            return;
        }

        // Caravan contagion is out of scope for v1; only seed disease on mapped pawns.
        if (ingester.MapHeld == null)
        {
            return;
        }

        if (!DiseaseProfileCache.TryGetResolvedProfile(_contaminatedDiseaseDef, out ResolvedTransmissionProfile resolvedProfile)
            || !ContagionDiseaseUtility.TryGetVector(resolvedProfile.Profile, out Vector_Foodborne foodborneVector))
        {
            ClearContamination();
            return;
        }

        if (IsContaminationExpired(foodborneVector))
        {
            ClearContamination();
            return;
        }

        ContagionDiagnostics.Record(ContagionDiagnosticCounter.FoodborneAttempted);
        // No spread suppression here: foodborne infection comes from a contaminated-food source, not
        // herd transmission between pawns, so it does not scale with the colony infection fraction.
        float transmissionMultiplier = Contagion_Mod.Settings?.EffectiveTransmissionMultiplier ?? 1f;
        float baseChance = ContagionIngestionUtility.ApplyTaintedFoodInfectionFactor(
            foodborneVector.baseChancePerMeal * _contaminationFactor,
            ingester);
        if (baseChance <= 0f)
        {
            return;
        }

        float chance = ContagionTransmissionUtility.BuildSeederChance(
            baseChance,
            ingester,
            resolvedProfile,
            ingester.MapHeld,
            transmissionMultiplier,
            out HediffDef _);
        bool passed = Rand.Chance(Mathf.Clamp01(chance));
        bool seeded = false;
        if (passed)
        {
            seeded = ContagionDiseaseUtility.TrySeedIncubation(
                ingester,
                resolvedProfile.DiseaseDef,
                resolvedProfile.PartsToAffect,
                null,
                ContagionDiagnosticOrigin.Spread,
                ContagionSeedSource.Foodborne,
                out HediffDef _);
            if (seeded)
            {
                ContagionDiagnostics.Record(ContagionDiagnosticCounter.FoodborneSeeded);
                ContagionDiagnostics.Trace($"Foodborne transmission: {resolvedProfile.DiseaseDef.defName} to {ingester.LabelShortCap}.");
                // Thing.Ingested calls this after the eaten item is removed/destroyed. Resolve via
                // the stored lineage so meals trace stove->pawn instead of falling back to a cell.
                ContagionTrace.FoodborneIngestion(parent, ingester, resolvedProfile.DiseaseDef, sourceTraceNodeId);
            }
        }

        ContagionDiagnostics.LogRoll(ContagionDebugVectorKind.Foodborne, parent, ingester, resolvedProfile.DiseaseDef, Mathf.Clamp01(chance), seeded);
    }

    public override string CompInspectStringExtra()
    {
        // Dev-only readout so clicking a meat/meal reveals hidden contamination at a glance.
        if (_contaminatedDiseaseDef == null || Contagion_Mod.Settings?.DeveloperDiagnosticsEnabled != true)
        {
            return null;
        }

        return "Contagion_ContaminatedFoodInspect".Translate(_contaminatedDiseaseDef.LabelCap, _contaminationFactor.ToString("0.00"));
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Defs.Look(ref _contaminatedDiseaseDef, "contaminatedDiseaseDef");
        Scribe_Values.Look(ref _contaminationFactor, "contaminationFactor", 1f);
        Scribe_Values.Look(ref _contaminationTick, "contaminationTick", -1);
        _contaminationFactor = Mathf.Clamp(_contaminationFactor, MinContaminationFactor, MaxCleanlinessFactor);
    }

    private void ClearContamination()
    {
        _contaminatedDiseaseDef = null;
        _contaminationFactor = 1f;
        _contaminationTick = -1;
    }

    private bool IsContaminationExpired(Vector_Foodborne vector)
    {
        if (vector.contaminationExpiryDays <= 0f || _contaminationTick < 0)
        {
            return false;
        }

        int expiryTicks = Mathf.RoundToInt(vector.contaminationExpiryDays * TicksPerDay);
        return Find.TickManager.TicksGame - _contaminationTick > expiryTicks;
    }

    private static float GetCleanlinessFactor(Room room, float cleanlinessImpact)
    {
        if (room == null || cleanlinessImpact <= 0f || room.PsychologicallyOutdoors)
        {
            return 1f;
        }

        float cleanliness = room.GetStat(RoomStatDefOf.Cleanliness);
        return Mathf.Clamp(1f - cleanliness * cleanlinessImpact, MinCleanlinessFactor, MaxCleanlinessFactor);
    }

    private static bool TryGetDiseaseSeverity(HediffDef diseaseDef, out float severity)
    {
        severity = 0f;
        if (diseaseDef == null || diseaseDef.lethalSeverity < 0f)
        {
            return false;
        }

        severity = diseaseDef.lethalSeverity;
        return true;
    }
}
