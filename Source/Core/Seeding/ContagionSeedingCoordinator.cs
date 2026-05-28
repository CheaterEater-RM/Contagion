using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Contagion;

internal enum SeedingFulfillmentKind
{
    None,
    Arrival,
    AnimalLinked,
    Acausal,
    EnvironmentalWindow
}

public static class ContagionSeedingCoordinator
{
    private const float TicksPerDay = 60000f;

    private sealed class ArrivalCandidate
    {
        public ArrivalCandidate(ResolvedTransmissionProfile resolvedProfile, Seeder_Arrival seeder)
        {
            ResolvedProfile = resolvedProfile;
            Seeder = seeder;
        }

        public ResolvedTransmissionProfile ResolvedProfile { get; }

        public Seeder_Arrival Seeder { get; }
    }

    public static ContagionSeedingMode CurrentMode => Contagion_Mod.Settings?.seedingMode ?? ContagionSeedingMode.Storyteller;

    public static bool TryHandleStorytellerRequest(IncidentWorker_Disease worker, IncidentParms parms, ResolvedTransmissionProfile resolvedProfile, out bool result)
    {
        result = false;

        if (worker == null || parms?.target is not Map map || resolvedProfile?.Profile == null)
        {
            return false;
        }

        Contagion_MapTransmissionComponent component = map.GetComponent<Contagion_MapTransmissionComponent>();
        if (component == null)
        {
            return false;
        }

        if (CurrentMode == ContagionSeedingMode.Contagion)
        {
            ContagionDiagnostics.Trace($"Storyteller disease cancelled by Contagion mode: {resolvedProfile.DiseaseDef.defName}.");
            result = false;
            return true;
        }

        TransmissionSeeder gateSeeder = GetPrimaryStorytellerGateSeeder(resolvedProfile.Profile);
        if (gateSeeder != null && component.IsAtActiveCaseLimit(resolvedProfile, gateSeeder))
        {
            ContagionDiagnostics.Record(ContagionDiagnosticCounter.PendingDroppedAtCap);
            ContagionDiagnostics.Trace($"Storyteller request dropped at active-case cap: {resolvedProfile.DiseaseDef.defName}.");
            result = false;
            return true;
        }

        if (UsesEnvironmentalSeedingOnly(resolvedProfile.Profile))
        {
            result = TryOpenEnvironmentalWindow(component, resolvedProfile);
            return true;
        }

        Seeder_Storyteller storytellerSeeder = GetSeeder<Seeder_Storyteller>(resolvedProfile.Profile);
        if (storytellerSeeder == null)
        {
            Log.Warning($"[Contagion] Profile {resolvedProfile.DiseaseDef.defName} has no storyteller or environmental request path. Storyteller request was skipped.");
            result = false;
            return true;
        }

        if (resolvedProfile.Profile.pendingWindowDays <= 0f)
        {
            result = TryResolveImmediateAcausal(component, resolvedProfile, storytellerSeeder, "immediate storyteller fallback");
            return true;
        }

        if (component.GetPendingEvent(resolvedProfile.DiseaseDef) != null)
        {
            ContagionDiagnostics.Record(ContagionDiagnosticCounter.PendingDroppedDuplicate);
            ContagionDiagnostics.Trace($"Duplicate storyteller request dropped: {resolvedProfile.DiseaseDef.defName}.");
            result = false;
            return true;
        }

        int currentTick = Find.TickManager.TicksGame;
        component.AddPendingEvent(new PendingDiseaseEvent
        {
            diseaseDef = resolvedProfile.DiseaseDef,
            firedTick = currentTick,
            expiryTick = currentTick + Mathf.Max(1, Mathf.RoundToInt(resolvedProfile.Profile.pendingWindowDays * TicksPerDay))
        });

        ContagionDiagnostics.Record(ContagionDiagnosticCounter.PendingQueued);
        ContagionDiagnostics.Trace($"Queued pending storyteller request: {resolvedProfile.DiseaseDef.defName} until tick {currentTick + Mathf.Max(1, Mathf.RoundToInt(resolvedProfile.Profile.pendingWindowDays * TicksPerDay))}.");
        result = true;
        return true;
    }

    public static int HandleArrivals(IEnumerable<Pawn> pawns)
    {
        if (pawns == null)
        {
            return 0;
        }

        if (CurrentMode == ContagionSeedingMode.Storyteller)
        {
            int resolvedCount = 0;
            foreach (Pawn pawn in pawns)
            {
                if (TryResolvePendingArrival(pawn))
                {
                    resolvedCount++;
                }
            }

            return resolvedCount;
        }

        List<ArrivalCandidate> arrivalCandidates = BuildArrivalCandidates();
        if (arrivalCandidates.Count == 0)
        {
            return 0;
        }

        float outbreakMultiplier = Contagion_Mod.Settings?.outbreakFrequencyMultiplier ?? 1f;
        int seededCount = 0;
        foreach (Pawn pawn in pawns)
        {
            if (TryHandleSingleArrival(pawn, arrivalCandidates, outbreakMultiplier))
            {
                seededCount++;
            }
        }

        return seededCount;
    }

    public static bool HandleRaidArrival(Pawn pawn)
    {
        if (pawn == null || pawn.Dead || !pawn.Spawned)
        {
            return false;
        }

        Contagion_MapTransmissionComponent component = pawn.Map?.GetComponent<Contagion_MapTransmissionComponent>();
        if (component == null)
        {
            return false;
        }

        if (CurrentMode == ContagionSeedingMode.Storyteller)
        {
            return TryResolvePendingArrival(pawn);
        }

        List<ArrivalCandidate> arrivalCandidates = BuildArrivalCandidates();
        if (arrivalCandidates.Count == 0)
        {
            return false;
        }

        float outbreakMultiplier = Contagion_Mod.Settings?.outbreakFrequencyMultiplier ?? 1f;
        return TryHandleSingleArrival(pawn, arrivalCandidates, outbreakMultiplier);
    }

    public static void RunGeneralSeeding(Contagion_MapTransmissionComponent component, IReadOnlyList<Pawn> spawnedPawns)
    {
        if (component == null || spawnedPawns == null || spawnedPawns.Count == 0)
        {
            return;
        }

        if (CurrentMode == ContagionSeedingMode.Contagion)
        {
            ResolveModeSwitchPendingEvents(component, spawnedPawns);
            RunContinuousSeeders(component, spawnedPawns);
            return;
        }

        ResolvePendingMapFulfillment(component, spawnedPawns);
    }

    public static bool TryGetEnvironmentalSeedingContext(
        Contagion_MapTransmissionComponent component,
        ResolvedTransmissionProfile resolvedProfile,
        Seeder_Environmental seeder,
        out PendingDiseaseEvent windowEvent,
        out float chanceMultiplier)
    {
        windowEvent = null;
        chanceMultiplier = 1f;

        if (component == null || resolvedProfile?.Profile == null || seeder == null)
        {
            return false;
        }

        if (CurrentMode == ContagionSeedingMode.Storyteller)
        {
            windowEvent = component.GetPendingEvent(resolvedProfile.DiseaseDef);
            if (windowEvent == null || !windowEvent.IsEnvironmentalWindow)
            {
                return false;
            }

            if (windowEvent.IsExpired(Find.TickManager.TicksGame))
            {
                component.RemovePendingEvent(windowEvent);
                ContagionDiagnostics.Record(ContagionDiagnosticCounter.EnvironmentalWindowClosedExpiry);
                ContagionDiagnostics.Trace($"Environmental window expired: {resolvedProfile.DiseaseDef.defName}.");
                return false;
            }

            if (!windowEvent.HasRemainingBudget)
            {
                component.RemovePendingEvent(windowEvent);
                ContagionDiagnostics.Record(ContagionDiagnosticCounter.EnvironmentalWindowClosedBudget);
                ContagionDiagnostics.Trace($"Environmental window budget exhausted: {resolvedProfile.DiseaseDef.defName}.");
                return false;
            }

            return true;
        }

        if (!component.CanRunSeeder(resolvedProfile, seeder))
        {
            return false;
        }

        chanceMultiplier = (Contagion_Mod.Settings?.outbreakFrequencyMultiplier ?? 1f)
            * component.GetPressureMultiplier(resolvedProfile.DiseaseDef, resolvedProfile.Profile.pressureDecayDays);
        return true;
    }

    public static void NotifyEnvironmentalSeeded(
        Contagion_MapTransmissionComponent component,
        ResolvedTransmissionProfile resolvedProfile,
        Seeder_Environmental seeder,
        PendingDiseaseEvent windowEvent)
    {
        if (component == null || resolvedProfile?.Profile == null || seeder == null)
        {
            return;
        }

        if (CurrentMode == ContagionSeedingMode.Storyteller)
        {
            if (windowEvent == null)
            {
                return;
            }

            windowEvent.infectionsApplied++;
            if (!windowEvent.HasRemainingBudget)
            {
                component.RemovePendingEvent(windowEvent);
                ContagionDiagnostics.Record(ContagionDiagnosticCounter.EnvironmentalWindowClosedBudget);
                ContagionDiagnostics.Trace($"Environmental window closed after reaching budget: {resolvedProfile.DiseaseDef.defName}.");
            }

            return;
        }

        component.NotifySeederFired(resolvedProfile, seeder);
        IncrementPressure(component, resolvedProfile);
    }

    private static bool TryOpenEnvironmentalWindow(Contagion_MapTransmissionComponent component, ResolvedTransmissionProfile resolvedProfile)
    {
        if (component.GetPendingEvent(resolvedProfile.DiseaseDef) != null)
        {
            ContagionDiagnostics.Record(ContagionDiagnosticCounter.PendingDroppedDuplicate);
            ContagionDiagnostics.Trace($"Duplicate environmental window request dropped: {resolvedProfile.DiseaseDef.defName}.");
            return false;
        }

        Seeder_Environmental environmentalSeeder = GetSeeder<Seeder_Environmental>(resolvedProfile.Profile);
        if (environmentalSeeder == null)
        {
            Log.Warning($"[Contagion] Environmental request for {resolvedProfile.DiseaseDef.defName} could not open because the environmental seeder is missing.");
            return false;
        }

        int currentTick = Find.TickManager.TicksGame;
        PendingDiseaseEvent pendingEvent = new PendingDiseaseEvent
        {
            diseaseDef = resolvedProfile.DiseaseDef,
            firedTick = currentTick,
            expiryTick = currentTick + Mathf.Max(1, Mathf.RoundToInt(environmentalSeeder.windowDays * TicksPerDay)),
            infectionBudget = Mathf.Max(1, environmentalSeeder.infectionBudget.RandomInRange)
        };

        component.AddPendingEvent(pendingEvent);
        ContagionDiagnostics.Record(ContagionDiagnosticCounter.PendingQueued);
        ContagionDiagnostics.Record(ContagionDiagnosticCounter.EnvironmentalWindowOpened);
        ContagionDiagnostics.Trace($"Opened environmental window for {resolvedProfile.DiseaseDef.defName} with budget {pendingEvent.infectionBudget}.");
        return true;
    }

    private static bool TryResolveImmediateAcausal(
        Contagion_MapTransmissionComponent component,
        ResolvedTransmissionProfile resolvedProfile,
        Seeder_Storyteller storytellerSeeder,
        string reason)
    {
        Seeder_Acausal acausalSeeder = GetSeeder<Seeder_Acausal>(resolvedProfile.Profile);
        if (acausalSeeder == null)
        {
            Log.Warning($"[Contagion] Storyteller request for {resolvedProfile.DiseaseDef.defName} needed immediate acausal resolution but no Seeder_Acausal was defined.");
            return false;
        }

        bool seeded = ContagionSeedingExecutionUtility.TrySeedRandomEligiblePawn(component.Map.mapPawns.AllPawnsSpawned, resolvedProfile, component.Map, out Pawn seededPawn);
        if (seeded)
        {
            ContagionDiagnostics.Record(ContagionDiagnosticCounter.PendingExpiredToAcausal);
            ContagionDiagnostics.Trace($"Acausal resolution ({reason}) seeded {resolvedProfile.DiseaseDef.defName} on {seededPawn.LabelShortCap}.");
        }

        return seeded;
    }

    private static bool TryResolvePendingArrival(Pawn pawn)
    {
        if (pawn == null || pawn.Dead || !pawn.Spawned)
        {
            return false;
        }

        Contagion_MapTransmissionComponent component = pawn.Map?.GetComponent<Contagion_MapTransmissionComponent>();
        if (component == null)
        {
            return false;
        }

        List<PendingDiseaseEvent> pendingEvents = new List<PendingDiseaseEvent>(component.PendingEvents);
        for (int i = 0; i < pendingEvents.Count; i++)
        {
            PendingDiseaseEvent pendingEvent = pendingEvents[i];
            if (pendingEvent == null || pendingEvent.IsEnvironmentalWindow)
            {
                continue;
            }

            if (!DiseaseProfileCache.TryGetResolvedProfile(pendingEvent.diseaseDef, out ResolvedTransmissionProfile resolvedProfile))
            {
                Log.Warning($"[Contagion] Pending disease request referenced missing profile {pendingEvent?.diseaseDef?.defName ?? "null"}; dropping it.");
                component.RemovePendingEvent(pendingEvent);
                continue;
            }

            if (!CanResolvePendingEventViaArrival(component, pendingEvent, resolvedProfile, pawn))
            {
                continue;
            }

            if (!ContagionSeedingExecutionUtility.TrySeedExactPawn(pawn, resolvedProfile, out HediffDef _))
            {
                continue;
            }

            component.RemovePendingEvent(pendingEvent);
            ContagionDiagnostics.Record(ContagionDiagnosticCounter.PendingResolvedArrival);
            ContagionDiagnostics.Record(ContagionDiagnosticCounter.ArrivalSeeded);
            ContagionDiagnostics.Trace($"Pending arrival request resolved {resolvedProfile.DiseaseDef.defName} onto {pawn.LabelShortCap}.");
            return true;
        }

        return false;
    }

    private static bool TryHandleSingleArrival(Pawn pawn, List<ArrivalCandidate> arrivalCandidates, float outbreakMultiplier)
    {
        if (CurrentMode == ContagionSeedingMode.Storyteller)
        {
            return TryResolvePendingArrival(pawn);
        }

        return TrySeedContinuousArrivalDisease(pawn, arrivalCandidates, outbreakMultiplier);
    }

    private static bool CanResolvePendingEventViaArrival(
        Contagion_MapTransmissionComponent component,
        PendingDiseaseEvent pendingEvent,
        ResolvedTransmissionProfile resolvedProfile,
        Pawn arrivingPawn)
    {
        if (pendingEvent.IsExpired(Find.TickManager.TicksGame))
        {
            return false;
        }

        if (!ContagionSeedingExecutionUtility.IsEligiblePawn(arrivingPawn, resolvedProfile, component.Map, out HediffDef _))
        {
            return false;
        }

        List<SeedingFulfillmentKind> order = GetFulfillmentOrder(resolvedProfile.Profile);
        for (int i = 0; i < order.Count; i++)
        {
            switch (order[i])
            {
                case SeedingFulfillmentKind.Arrival:
                    return true;
                case SeedingFulfillmentKind.AnimalLinked:
                    if (HasAnimalsOnMap(component.Map.mapPawns.AllPawnsSpawned))
                    {
                        return false;
                    }

                    break;
                case SeedingFulfillmentKind.Acausal:
                    if (pendingEvent.IsExpired(Find.TickManager.TicksGame))
                    {
                        return false;
                    }

                    break;
                default:
                    break;
            }
        }

        return false;
    }

    private static bool TrySeedContinuousArrivalDisease(Pawn pawn, List<ArrivalCandidate> arrivalCandidates, float outbreakMultiplier)
    {
        if (pawn == null || pawn.Dead || !pawn.Spawned)
        {
            return false;
        }

        Contagion_MapTransmissionComponent component = pawn.Map?.GetComponent<Contagion_MapTransmissionComponent>();
        ContagionDiagnostics.Record(ContagionDiagnosticCounter.ArrivalAttempted);

        List<ArrivalCandidate> applicableCandidates = new List<ArrivalCandidate>();
        for (int i = 0; i < arrivalCandidates.Count; i++)
        {
            ArrivalCandidate candidate = arrivalCandidates[i];
            if (component != null && !component.CanRunSeeder(candidate.ResolvedProfile, candidate.Seeder))
            {
                continue;
            }

            float pressureMultiplier = component?.GetPressureMultiplier(candidate.ResolvedProfile.DiseaseDef, candidate.ResolvedProfile.Profile.pressureDecayDays) ?? 1f;
            float chance = ContagionTransmissionUtility.BuildSeederChance(
                candidate.Seeder.arrivalChance * pressureMultiplier,
                pawn,
                candidate.ResolvedProfile,
                pawn.Map,
                outbreakMultiplier,
                out HediffDef _);
            if (chance > 0f)
            {
                applicableCandidates.Add(candidate);
            }
        }

        if (applicableCandidates.Count == 0)
        {
            return false;
        }

        applicableCandidates.Shuffle();
        for (int i = 0; i < applicableCandidates.Count; i++)
        {
            ArrivalCandidate candidate = applicableCandidates[i];
            float pressureMultiplier = component?.GetPressureMultiplier(candidate.ResolvedProfile.DiseaseDef, candidate.ResolvedProfile.Profile.pressureDecayDays) ?? 1f;
            float arrivalChance = ContagionTransmissionUtility.BuildSeederChance(
                candidate.Seeder.arrivalChance * pressureMultiplier,
                pawn,
                candidate.ResolvedProfile,
                pawn.Map,
                outbreakMultiplier,
                out HediffDef _);
            if (arrivalChance <= 0f || !Rand.Chance(Mathf.Clamp01(arrivalChance)))
            {
                continue;
            }

            if (!ContagionSeedingExecutionUtility.TrySeedExactPawn(pawn, candidate.ResolvedProfile, out HediffDef _))
            {
                continue;
            }

            component?.NotifySeederFired(candidate.ResolvedProfile, candidate.Seeder);
            if (component != null)
            {
                IncrementPressure(component, candidate.ResolvedProfile);
            }

            ContagionDiagnostics.Record(ContagionDiagnosticCounter.ArrivalSeeded);
            ContagionDiagnostics.Trace($"Arrival seeded: {candidate.ResolvedProfile.DiseaseDef.defName} on {pawn.LabelShortCap}.");
            return true;
        }

        return false;
    }

    private static void ResolvePendingMapFulfillment(Contagion_MapTransmissionComponent component, IReadOnlyList<Pawn> spawnedPawns)
    {
        List<PendingDiseaseEvent> pendingEvents = new List<PendingDiseaseEvent>(component.PendingEvents);
        int currentTick = Find.TickManager.TicksGame;

        for (int i = 0; i < pendingEvents.Count; i++)
        {
            PendingDiseaseEvent pendingEvent = pendingEvents[i];
            if (pendingEvent == null || pendingEvent.IsEnvironmentalWindow)
            {
                continue;
            }

            if (!DiseaseProfileCache.TryGetResolvedProfile(pendingEvent.diseaseDef, out ResolvedTransmissionProfile resolvedProfile))
            {
                Log.Warning($"[Contagion] Pending disease request referenced missing profile {pendingEvent?.diseaseDef?.defName ?? "null"}; dropping it.");
                component.RemovePendingEvent(pendingEvent);
                continue;
            }

            if (TryResolveAnimalLinked(component, resolvedProfile, pendingEvent, spawnedPawns))
            {
                continue;
            }

            if (!pendingEvent.IsExpired(currentTick))
            {
                continue;
            }

            TryResolvePendingAcausal(component, resolvedProfile, pendingEvent, spawnedPawns, "pending expiry");
        }
    }

    private static bool TryResolveAnimalLinked(
        Contagion_MapTransmissionComponent component,
        ResolvedTransmissionProfile resolvedProfile,
        PendingDiseaseEvent pendingEvent,
        IReadOnlyList<Pawn> spawnedPawns)
    {
        Seeder_AnimalLinked animalSeeder = GetSeeder<Seeder_AnimalLinked>(resolvedProfile.Profile);
        if (animalSeeder == null || !HasAnimalsOnMap(spawnedPawns))
        {
            return false;
        }

        bool seeded = ContagionSeedingExecutionUtility.TrySeedWeightedEligiblePawn(
            spawnedPawns,
            resolvedProfile,
            component.Map,
            pawn => GetAnimalLinkedWeight(pawn, animalSeeder),
            out Pawn seededPawn);
        if (!seeded)
        {
            return false;
        }

        component.RemovePendingEvent(pendingEvent);
        ContagionDiagnostics.Record(ContagionDiagnosticCounter.PendingResolvedAnimal);
        ContagionDiagnostics.Trace($"Animal-linked pending request resolved {resolvedProfile.DiseaseDef.defName} onto {seededPawn.LabelShortCap}.");
        return true;
    }

    private static bool TryResolvePendingAcausal(
        Contagion_MapTransmissionComponent component,
        ResolvedTransmissionProfile resolvedProfile,
        PendingDiseaseEvent pendingEvent,
        IReadOnlyList<Pawn> spawnedPawns,
        string reason)
    {
        Seeder_Acausal acausalSeeder = GetSeeder<Seeder_Acausal>(resolvedProfile.Profile);
        if (acausalSeeder == null)
        {
            Log.Warning($"[Contagion] Pending request for {resolvedProfile.DiseaseDef.defName} expired without a Seeder_Acausal fallback.");
            component.RemovePendingEvent(pendingEvent);
            return false;
        }

        bool seeded = ContagionSeedingExecutionUtility.TrySeedRandomEligiblePawn(spawnedPawns, resolvedProfile, component.Map, out Pawn seededPawn);
        component.RemovePendingEvent(pendingEvent);
        if (seeded)
        {
            ContagionDiagnostics.Record(ContagionDiagnosticCounter.PendingExpiredToAcausal);
            ContagionDiagnostics.Trace($"Acausal fallback ({reason}) seeded {resolvedProfile.DiseaseDef.defName} on {seededPawn.LabelShortCap}.");
        }

        return seeded;
    }

    private static void ResolveModeSwitchPendingEvents(Contagion_MapTransmissionComponent component, IReadOnlyList<Pawn> spawnedPawns)
    {
        if (component.PendingEvents.Count == 0)
        {
            return;
        }

        List<PendingDiseaseEvent> pendingEvents = new List<PendingDiseaseEvent>(component.PendingEvents);
        for (int i = 0; i < pendingEvents.Count; i++)
        {
            PendingDiseaseEvent pendingEvent = pendingEvents[i];
            if (pendingEvent == null)
            {
                continue;
            }

            if (!DiseaseProfileCache.TryGetResolvedProfile(pendingEvent.diseaseDef, out ResolvedTransmissionProfile resolvedProfile))
            {
                component.RemovePendingEvent(pendingEvent);
                continue;
            }

            if (pendingEvent.IsEnvironmentalWindow)
            {
                component.RemovePendingEvent(pendingEvent);
                ContagionDiagnostics.Trace($"Cleared environmental window during mode switch: {resolvedProfile.DiseaseDef.defName}.");
                continue;
            }

            TryResolvePendingAcausal(component, resolvedProfile, pendingEvent, spawnedPawns, "mode switch");
        }
    }

    private static void RunContinuousSeeders(Contagion_MapTransmissionComponent component, IReadOnlyList<Pawn> spawnedPawns)
    {
        float outbreakMultiplier = Contagion_Mod.Settings?.outbreakFrequencyMultiplier ?? 1f;
        foreach (ResolvedTransmissionProfile resolvedProfile in DiseaseProfileCache.AllProfiles)
        {
            if (resolvedProfile.Profile.seeders == null)
            {
                continue;
            }

            for (int i = 0; i < resolvedProfile.Profile.seeders.Count; i++)
            {
                TransmissionSeeder seeder = resolvedProfile.Profile.seeders[i];
                if (seeder is Seeder_Acausal acausal)
                {
                    TryRunContinuousSeeder(component, resolvedProfile, acausal, acausal.mtbDays, spawnedPawns, outbreakMultiplier, null);
                }
                else if (seeder is Seeder_AnimalLinked animalLinked)
                {
                    if (!animalLinked.requiresAnimalsOnMap || HasAnimalsOnMap(spawnedPawns))
                    {
                        TryRunContinuousSeeder(
                            component,
                            resolvedProfile,
                            animalLinked,
                            animalLinked.mtbDays / Mathf.Max(0.01f, animalLinked.handlerBias),
                            spawnedPawns,
                            outbreakMultiplier,
                            pawn => GetAnimalLinkedWeight(pawn, animalLinked));
                    }
                }
            }
        }
    }

    private static void TryRunContinuousSeeder(
        Contagion_MapTransmissionComponent component,
        ResolvedTransmissionProfile resolvedProfile,
        TransmissionSeeder seeder,
        float mtbDays,
        IReadOnlyList<Pawn> spawnedPawns,
        float outbreakMultiplier,
        System.Func<Pawn, float> weightSelector)
    {
        if (!component.CanRunSeeder(resolvedProfile, seeder))
        {
            return;
        }

        float adjustedMtbDays = mtbDays / Mathf.Max(0.01f, outbreakMultiplier);
        if (!Rand.MTBEventOccurs(adjustedMtbDays, 60000f, 2500f))
        {
            return;
        }

        float pressureMultiplier = component.GetPressureMultiplier(resolvedProfile.DiseaseDef, resolvedProfile.Profile.pressureDecayDays);
        if (!Rand.Chance(Mathf.Clamp01(pressureMultiplier)))
        {
            ContagionDiagnostics.Trace($"Continuous seeder roll damped by pressure for {resolvedProfile.DiseaseDef.defName}: {pressureMultiplier:0.###}.");
            return;
        }

        bool seeded = weightSelector == null
            ? ContagionSeedingExecutionUtility.TrySeedRandomEligiblePawn(spawnedPawns, resolvedProfile, component.Map, out Pawn seededPawn)
            : ContagionSeedingExecutionUtility.TrySeedWeightedEligiblePawn(spawnedPawns, resolvedProfile, component.Map, weightSelector, out seededPawn);
        if (!seeded)
        {
            return;
        }

        component.NotifySeederFired(resolvedProfile, seeder);
        IncrementPressure(component, resolvedProfile);
        ContagionDiagnostics.Trace($"{seeder.GetType().Name} seeded {resolvedProfile.DiseaseDef.defName} on {seededPawn.LabelShortCap}.");
    }

    private static void IncrementPressure(Contagion_MapTransmissionComponent component, ResolvedTransmissionProfile resolvedProfile)
    {
        component.IncrementPressure(
            resolvedProfile.DiseaseDef,
            resolvedProfile.Profile.pressureGain,
            resolvedProfile.Profile.pressureDecayDays);
        ContagionDiagnostics.Record(ContagionDiagnosticCounter.PressureIncremented);
    }

    private static float GetAnimalLinkedWeight(Pawn pawn, Seeder_AnimalLinked seeder)
    {
        if (pawn?.skills == null)
        {
            return 1f;
        }

        SkillRecord animalSkill = pawn.skills.GetSkill(SkillDefOf.Animals);
        if (animalSkill == null)
        {
            return 1f;
        }

        float normalizedSkill = Mathf.Clamp01(animalSkill.Level / 20f);
        return Mathf.Max(1f, 1f + (Mathf.Max(1f, seeder.handlerBias) - 1f) * normalizedSkill);
    }

    private static bool HasAnimalsOnMap(IReadOnlyList<Pawn> spawnedPawns)
    {
        for (int i = 0; i < spawnedPawns.Count; i++)
        {
            if (spawnedPawns[i]?.RaceProps?.Animal == true)
            {
                return true;
            }
        }

        return false;
    }

    private static TransmissionSeeder GetPrimaryStorytellerGateSeeder(TransmissionProfile profile)
    {
        return UsesEnvironmentalSeedingOnly(profile)
            ? GetSeeder<Seeder_Environmental>(profile)
            : GetSeeder<Seeder_Storyteller>(profile);
    }

    private static List<ArrivalCandidate> BuildArrivalCandidates()
    {
        List<ArrivalCandidate> arrivalCandidates = new List<ArrivalCandidate>();

        foreach (ResolvedTransmissionProfile resolvedProfile in DiseaseProfileCache.AllProfiles)
        {
            Seeder_Arrival seeder = GetSeeder<Seeder_Arrival>(resolvedProfile.Profile);
            if (seeder != null)
            {
                arrivalCandidates.Add(new ArrivalCandidate(resolvedProfile, seeder));
            }
        }

        return arrivalCandidates;
    }

    private static List<SeedingFulfillmentKind> GetFulfillmentOrder(TransmissionProfile profile)
    {
        List<SeedingFulfillmentKind> order = new List<SeedingFulfillmentKind>();
        if (profile?.seeders == null)
        {
            return order;
        }

        for (int i = 0; i < profile.seeders.Count; i++)
        {
            switch (profile.seeders[i])
            {
                case Seeder_Arrival:
                    order.Add(SeedingFulfillmentKind.Arrival);
                    break;
                case Seeder_AnimalLinked:
                    order.Add(SeedingFulfillmentKind.AnimalLinked);
                    break;
                case Seeder_Acausal:
                    order.Add(SeedingFulfillmentKind.Acausal);
                    break;
                case Seeder_Environmental:
                    order.Add(SeedingFulfillmentKind.EnvironmentalWindow);
                    break;
            }
        }

        return order;
    }

    private static bool UsesEnvironmentalSeedingOnly(TransmissionProfile profile)
    {
        return GetSeeder<Seeder_Storyteller>(profile) == null && GetSeeder<Seeder_Environmental>(profile) != null;
    }

    private static TSeeder GetSeeder<TSeeder>(TransmissionProfile profile)
        where TSeeder : TransmissionSeeder
    {
        if (profile?.seeders == null)
        {
            return null;
        }

        for (int i = 0; i < profile.seeders.Count; i++)
        {
            if (profile.seeders[i] is TSeeder typedSeeder)
            {
                return typedSeeder;
            }
        }

        return null;
    }
}
