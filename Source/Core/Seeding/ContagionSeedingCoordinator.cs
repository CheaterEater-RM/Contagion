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

    private sealed class GroupSeedingPolicy
    {
        public GroupSeedingPolicy(float exposureMultiplier, float carrierSqrtScale, int carrierCap, float mildVisibleChance)
        {
            ExposureMultiplier = exposureMultiplier;
            CarrierSqrtScale = carrierSqrtScale;
            CarrierCap = carrierCap;
            MildVisibleChance = mildVisibleChance;
        }

        public float ExposureMultiplier { get; }

        public float CarrierSqrtScale { get; }

        public int CarrierCap { get; }

        public float MildVisibleChance { get; }
    }

    private sealed class CarrierCandidate
    {
        public CarrierCandidate(Pawn pawn, float weight)
        {
            Pawn = pawn;
            Weight = weight;
        }

        public Pawn Pawn { get; }

        public float Weight { get; }
    }

    private sealed class GroupExposureCandidate
    {
        public GroupExposureCandidate(ArrivalCandidate arrivalCandidate, List<CarrierCandidate> carrierCandidates, float exposureChance)
        {
            ArrivalCandidate = arrivalCandidate;
            CarrierCandidates = carrierCandidates;
            ExposureChance = exposureChance;
        }

        public ArrivalCandidate ArrivalCandidate { get; }

        public List<CarrierCandidate> CarrierCandidates { get; }

        public float ExposureChance { get; }
    }

    public static ContagionSeedingMode CurrentMode => Contagion_Mod.Settings?.seedingMode ?? ContagionSeedingMode.Storyteller;

    public static List<ResolvedTransmissionProfile> GetDeveloperArrivalProfiles()
    {
        List<ResolvedTransmissionProfile> profiles = new List<ResolvedTransmissionProfile>();
        List<ArrivalCandidate> arrivalCandidates = BuildArrivalCandidates();
        for (int i = 0; i < arrivalCandidates.Count; i++)
        {
            ResolvedTransmissionProfile resolvedProfile = arrivalCandidates[i].ResolvedProfile;
            if (resolvedProfile?.Profile?.affectsHumans != true)
            {
                continue;
            }

            profiles.Add(resolvedProfile);
        }

        profiles.Sort((left, right) => string.Compare(left?.DiseaseDef?.label, right?.DiseaseDef?.label, System.StringComparison.OrdinalIgnoreCase));
        return profiles;
    }

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
            ContagionDiagnostics.Record(ContagionDiagnosticCounter.StorytellerCancelled);
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
            return false;
        }

        if (resolvedProfile.Profile.pendingWindowDays <= 0f)
        {
            result = TryResolveImmediateAcausal(component, resolvedProfile, "immediate storyteller fallback");
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

    public static int HandleArrivalGroup(IEnumerable<Pawn> pawns, ContagionArrivalGroupKind groupKind)
    {
        ContagionDiagnostics.Record(ContagionDiagnosticCounter.ArrivalGroupChecked);
        List<Pawn> groupPawns = BuildSpawnedGroupPawns(pawns, out Map map);
        if (groupPawns.Count == 0 || map == null)
        {
            ContagionDiagnostics.Record(ContagionDiagnosticCounter.ArrivalGroupSkippedEmpty);
            ContagionDiagnostics.Trace($"Arrival group skipped ({groupKind}): no spawned pawns on a single map.");
            return 0;
        }

        if (CurrentMode == ContagionSeedingMode.Storyteller)
        {
            return TryResolvePendingArrivalGroup(groupPawns, map, groupKind);
        }

        return TryResolveContinuousArrivalGroup(groupPawns, map, groupKind);
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
            * component.DiseaseDirector.GetChanceMultiplier(resolvedProfile.Profile);
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
        component.DiseaseDirector.NotifySeeded(resolvedProfile.Profile, 1);
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
            component.NotifySeederFired(resolvedProfile, acausalSeeder);
            ContagionDiagnostics.Record(ContagionDiagnosticCounter.PendingExpiredToAcausal);
            ContagionDiagnostics.Trace($"Acausal resolution ({reason}) seeded {resolvedProfile.DiseaseDef.defName} on {seededPawn.LabelShortCap}.");
        }

        return seeded;
    }

    private static int TryResolvePendingArrivalGroup(IReadOnlyList<Pawn> groupPawns, Map map, ContagionArrivalGroupKind groupKind)
    {
        Contagion_MapTransmissionComponent component = map.GetComponent<Contagion_MapTransmissionComponent>();
        if (component == null)
        {
            return 0;
        }

        GroupSeedingPolicy policy = GetGroupPolicy(groupKind);
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

            if (!CanResolvePendingEventViaArrival(component, pendingEvent, resolvedProfile))
            {
                continue;
            }

            Seeder_Arrival arrivalSeeder = GetSeeder<Seeder_Arrival>(resolvedProfile.Profile);
            if (arrivalSeeder == null)
            {
                continue;
            }

            List<CarrierCandidate> carrierCandidates = BuildCarrierCandidates(groupPawns, resolvedProfile, map);
            int remainingCapacity = GetRemainingActiveCaseCapacity(component, resolvedProfile, arrivalSeeder);
            int carrierCount = DetermineCarrierCount(carrierCandidates.Count, resolvedProfile.Profile, policy, remainingCapacity);
            int seededCount = SeedCarrierPayload(carrierCandidates, resolvedProfile, policy, carrierCount);
            if (seededCount <= 0)
            {
                ContagionDiagnostics.Record(ContagionDiagnosticCounter.ArrivalNoEligibleCarriers);
                ContagionDiagnostics.Trace($"Pending arrival request could not seed {resolvedProfile.DiseaseDef.defName} from a {groupKind} group: no carrier payload was eligible.");
                continue;
            }

            component.RemovePendingEvent(pendingEvent);
            ContagionDiagnostics.Record(ContagionDiagnosticCounter.PendingResolvedArrival);
            ContagionDiagnostics.Record(ContagionDiagnosticCounter.ArrivalExposureSucceeded);
            ContagionDiagnostics.Record(ContagionDiagnosticCounter.ArrivalSeeded, seededCount);
            ContagionDiagnostics.Trace($"Pending arrival request resolved {resolvedProfile.DiseaseDef.defName} onto {seededCount} pawn(s) from a {groupKind} group.");
            return seededCount;
        }

        return 0;
    }

    private static int TryResolveContinuousArrivalGroup(IReadOnlyList<Pawn> groupPawns, Map map, ContagionArrivalGroupKind groupKind)
    {
        Contagion_MapTransmissionComponent component = map.GetComponent<Contagion_MapTransmissionComponent>();
        if (component == null)
        {
            return 0;
        }

        List<ArrivalCandidate> arrivalCandidates = BuildArrivalCandidates();
        HediffDef forcedDisease = GetForcedArrivalDisease(component);
        bool forcedArrivalApplied = false;
        if (forcedDisease != null)
        {
            List<ArrivalCandidate> forcedArrivalCandidates = FilterArrivalCandidates(arrivalCandidates, forcedDisease);
            if (IsForcedArrivalGroupQualifying(forcedArrivalCandidates, groupPawns, map))
            {
                component.DeveloperClearForcedArrival();
                arrivalCandidates = forcedArrivalCandidates;
                forcedArrivalApplied = true;
            }
        }

        if (arrivalCandidates.Count == 0)
        {
            ContagionDiagnostics.Record(ContagionDiagnosticCounter.ArrivalNoDiseaseCandidates);
            ContagionDiagnostics.Trace($"Arrival group skipped ({groupKind}): no diseases have arrival seeders.");
            if (forcedArrivalApplied)
            {
                ContagionDiagnostics.Trace($"Forced arrival override consumed for {forcedDisease.defName}, but no matching arrival seeder was available for a {groupKind} group.");
            }

            return 0;
        }

        GroupSeedingPolicy policy = GetGroupPolicy(groupKind);
        float outbreakMultiplier = Contagion_Mod.Settings?.outbreakFrequencyMultiplier ?? 1f;
        List<GroupExposureCandidate> exposureCandidates = new List<GroupExposureCandidate>();
        float exposureFailureChance = 1f;
        bool sawRunnableDiseaseCandidate = false;
        bool sawEligibleCarrierCandidate = false;

        for (int i = 0; i < arrivalCandidates.Count; i++)
        {
            ArrivalCandidate arrivalCandidate = arrivalCandidates[i];
            if (!component.CanRunSeeder(arrivalCandidate.ResolvedProfile, arrivalCandidate.Seeder))
            {
                continue;
            }

            int remainingCapacity = GetRemainingActiveCaseCapacity(component, arrivalCandidate.ResolvedProfile, arrivalCandidate.Seeder);
            if (remainingCapacity <= 0)
            {
                continue;
            }

            sawRunnableDiseaseCandidate = true;
            List<CarrierCandidate> carrierCandidates = BuildCarrierCandidates(groupPawns, arrivalCandidate.ResolvedProfile, map);
            if (carrierCandidates.Count == 0)
            {
                continue;
            }

            sawEligibleCarrierCandidate = true;
            float directorMultiplier = component.DiseaseDirector.GetChanceMultiplier(arrivalCandidate.ResolvedProfile.Profile);
            float exposureChance = component.DiseaseDirector.ClampChance(
                arrivalCandidate.Seeder.arrivalChance
                * policy.ExposureMultiplier
                * outbreakMultiplier
                * directorMultiplier
                * ContagionTransmissionUtility.GetSeasonalMultiplier(map, arrivalCandidate.ResolvedProfile.Profile));
            if (exposureChance <= 0f)
            {
                continue;
            }

            exposureCandidates.Add(new GroupExposureCandidate(arrivalCandidate, carrierCandidates, exposureChance));
            exposureFailureChance *= 1f - exposureChance;
        }

        if (exposureCandidates.Count == 0)
        {
            if (sawRunnableDiseaseCandidate && !sawEligibleCarrierCandidate)
            {
                ContagionDiagnostics.Record(ContagionDiagnosticCounter.ArrivalNoEligibleCarriers);
                ContagionDiagnostics.Trace($"Arrival group skipped ({groupKind}): no eligible carrier pawns for runnable disease candidates.");
            }
            else
            {
                ContagionDiagnostics.Record(ContagionDiagnosticCounter.ArrivalNoDiseaseCandidates);
                ContagionDiagnostics.Trace($"Arrival group skipped ({groupKind}): no runnable disease candidates.");
            }

            if (forcedArrivalApplied)
            {
                ContagionDiagnostics.Trace($"Forced arrival override for {forcedDisease.defName} on a {groupKind} group found no runnable exposure candidate.");
            }

            return 0;
        }

        float combinedExposureChance = Mathf.Clamp01(1f - exposureFailureChance);
        if (!Rand.Chance(combinedExposureChance))
        {
            if (forcedArrivalApplied)
            {
                ContagionDiagnostics.Trace($"Forced arrival override for {forcedDisease.defName} on a {groupKind} group failed its arrival exposure chance roll.");
            }

            return 0;
        }

        GroupExposureCandidate selectedExposure = SelectExposureCandidate(exposureCandidates);
        if (selectedExposure == null)
        {
            if (forcedArrivalApplied)
            {
                ContagionDiagnostics.Trace($"Forced arrival override for {forcedDisease.defName} on a {groupKind} group produced no selectable exposure candidate.");
            }

            return 0;
        }

        ContagionDiagnostics.Record(ContagionDiagnosticCounter.ArrivalExposureSucceeded);
        int selectedRemainingCapacity = GetRemainingActiveCaseCapacity(
            component,
            selectedExposure.ArrivalCandidate.ResolvedProfile,
            selectedExposure.ArrivalCandidate.Seeder);
        int carrierCount = DetermineCarrierCount(
            selectedExposure.CarrierCandidates.Count,
            selectedExposure.ArrivalCandidate.ResolvedProfile.Profile,
            policy,
            selectedRemainingCapacity);
        int seededCount = SeedCarrierPayload(
            selectedExposure.CarrierCandidates,
            selectedExposure.ArrivalCandidate.ResolvedProfile,
            policy,
            carrierCount);
        if (seededCount <= 0)
        {
            ContagionDiagnostics.Record(ContagionDiagnosticCounter.ArrivalNoEligibleCarriers);
            ContagionDiagnostics.Trace($"Arrival exposure for {selectedExposure.ArrivalCandidate.ResolvedProfile.DiseaseDef.defName} from a {groupKind} group seeded no carriers.");
            if (forcedArrivalApplied)
            {
                ContagionDiagnostics.Trace($"Forced arrival override for {forcedDisease.defName} on a {groupKind} group seeded no carriers.");
            }

            return 0;
        }

        component.NotifySeederFired(selectedExposure.ArrivalCandidate.ResolvedProfile, selectedExposure.ArrivalCandidate.Seeder);
        component.DiseaseDirector.NotifySeeded(selectedExposure.ArrivalCandidate.ResolvedProfile.Profile, seededCount);
        ContagionDiagnostics.Record(ContagionDiagnosticCounter.ArrivalSeeded, seededCount);
        ContagionDiagnostics.Trace($"Arrival group exposure seeded {selectedExposure.ArrivalCandidate.ResolvedProfile.DiseaseDef.defName} on {seededCount} pawn(s) from a {groupKind} group.");
        if (forcedArrivalApplied)
        {
            ContagionDiagnostics.Trace($"Forced arrival override succeeded for {forcedDisease.defName} on a {groupKind} group.");
        }

        return seededCount;
    }

    private static bool CanResolvePendingEventViaArrival(
        Contagion_MapTransmissionComponent component,
        PendingDiseaseEvent pendingEvent,
        ResolvedTransmissionProfile resolvedProfile)
    {
        if (pendingEvent.IsExpired(Find.TickManager.TicksGame))
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
                    if (CanAnimalLinkedResolveArrivalPrecedence(component, resolvedProfile))
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

    private static bool CanAnimalLinkedResolveArrivalPrecedence(
        Contagion_MapTransmissionComponent component,
        ResolvedTransmissionProfile resolvedProfile)
    {
        IReadOnlyList<Pawn> spawnedPawns = component?.Map?.mapPawns?.AllPawnsSpawned;
        if (spawnedPawns == null || !HasAnimalsOnMap(spawnedPawns))
        {
            return false;
        }

        Seeder_AnimalLinked animalSeeder = GetSeeder<Seeder_AnimalLinked>(resolvedProfile.Profile);
        if (animalSeeder == null)
        {
            return false;
        }

        for (int i = 0; i < spawnedPawns.Count; i++)
        {
            if (ContagionSeedingExecutionUtility.IsEligiblePawn(spawnedPawns[i], resolvedProfile, component.Map, out HediffDef _))
            {
                return true;
            }
        }

        return false;
    }

    private static List<Pawn> BuildSpawnedGroupPawns(IEnumerable<Pawn> pawns, out Map map)
    {
        map = null;
        List<Pawn> groupPawns = new List<Pawn>();
        if (pawns == null)
        {
            return groupPawns;
        }

        foreach (Pawn pawn in pawns)
        {
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map == null)
            {
                continue;
            }

            if (map == null)
            {
                map = pawn.Map;
            }

            if (pawn.Map != map)
            {
                continue;
            }

            groupPawns.Add(pawn);
        }

        return groupPawns;
    }

    private static List<CarrierCandidate> BuildCarrierCandidates(IReadOnlyList<Pawn> pawns, ResolvedTransmissionProfile resolvedProfile, Map map)
    {
        List<CarrierCandidate> carrierCandidates = new List<CarrierCandidate>();
        for (int i = 0; i < pawns.Count; i++)
        {
            Pawn pawn = pawns[i];
            float weight = ContagionTransmissionUtility.BuildSeederChance(
                1f,
                pawn,
                resolvedProfile,
                map,
                1f,
                out HediffDef _);
            if (weight <= 0f)
            {
                continue;
            }

            carrierCandidates.Add(new CarrierCandidate(pawn, weight));
        }

        return carrierCandidates;
    }

    private static int DetermineCarrierCount(
        int eligiblePawnCount,
        TransmissionProfile profile,
        GroupSeedingPolicy policy,
        int remainingCapacity)
    {
        if (eligiblePawnCount <= 0 || remainingCapacity <= 0)
        {
            return 0;
        }

        int carrierCap = Mathf.Min(policy.CarrierCap, eligiblePawnCount, remainingCapacity);
        if (carrierCap <= 0)
        {
            return 0;
        }

        float lambda = policy.CarrierSqrtScale
            * GetDiseaseClusterFactor(profile)
            * Mathf.Sqrt(eligiblePawnCount);
        int carrierCount = 1 + SamplePoisson(lambda);
        return Mathf.Clamp(carrierCount, 1, carrierCap);
    }

    private static int SeedCarrierPayload(
        List<CarrierCandidate> carrierCandidates,
        ResolvedTransmissionProfile resolvedProfile,
        GroupSeedingPolicy policy,
        int carrierCount)
    {
        if (carrierCandidates.Count == 0 || carrierCount <= 0)
        {
            return 0;
        }

        int seededCount = 0;
        List<CarrierCandidate> remainingCandidates = new List<CarrierCandidate>(carrierCandidates);
        while (seededCount < carrierCount && remainingCandidates.Count > 0)
        {
            int selectedIndex = SelectCarrierCandidateIndex(remainingCandidates);
            CarrierCandidate selectedCandidate = remainingCandidates[selectedIndex];
            remainingCandidates.RemoveAt(selectedIndex);

            if (ContagionSeedingExecutionUtility.TrySeedArrivalCarrier(
                selectedCandidate.Pawn,
                resolvedProfile,
                policy.MildVisibleChance,
                out bool visibleDisease))
            {
                seededCount++;
                ContagionDiagnostics.Record(ContagionDiagnosticCounter.ArrivalCarrierSeeded);
                ContagionDiagnostics.Record(visibleDisease
                    ? ContagionDiagnosticCounter.ArrivalCarrierMildVisible
                    : ContagionDiagnosticCounter.ArrivalCarrierLatent);
                ContagionDiagnostics.Trace($"Arrival carrier seeded: {resolvedProfile.DiseaseDef.defName} on {selectedCandidate.Pawn.LabelShortCap} ({(visibleDisease ? "mild visible" : "latent")}).");
            }
        }

        return seededCount;
    }

    private static int SelectCarrierCandidateIndex(List<CarrierCandidate> carrierCandidates)
    {
        float totalWeight = 0f;
        for (int i = 0; i < carrierCandidates.Count; i++)
        {
            totalWeight += Mathf.Max(0f, carrierCandidates[i].Weight);
        }

        if (totalWeight <= 0f)
        {
            return Rand.Range(0, carrierCandidates.Count);
        }

        float roll = Rand.Value * totalWeight;
        float runningWeight = 0f;
        for (int i = 0; i < carrierCandidates.Count; i++)
        {
            runningWeight += Mathf.Max(0f, carrierCandidates[i].Weight);
            if (roll <= runningWeight)
            {
                return i;
            }
        }

        return carrierCandidates.Count - 1;
    }

    private static GroupExposureCandidate SelectExposureCandidate(List<GroupExposureCandidate> exposureCandidates)
    {
        float totalWeight = 0f;
        for (int i = 0; i < exposureCandidates.Count; i++)
        {
            totalWeight += Mathf.Max(0f, exposureCandidates[i].ExposureChance);
        }

        if (totalWeight <= 0f)
        {
            return null;
        }

        float roll = Rand.Value * totalWeight;
        float runningWeight = 0f;
        for (int i = 0; i < exposureCandidates.Count; i++)
        {
            runningWeight += Mathf.Max(0f, exposureCandidates[i].ExposureChance);
            if (roll <= runningWeight)
            {
                return exposureCandidates[i];
            }
        }

        return exposureCandidates[exposureCandidates.Count - 1];
    }

    private static int SamplePoisson(float lambda)
    {
        if (lambda <= 0f)
        {
            return 0;
        }

        float threshold = Mathf.Exp(-lambda);
        float product = 1f;
        int sample = 0;
        do
        {
            sample++;
            product *= Rand.Value;
        }
        while (product > threshold && sample < 100);

        return sample - 1;
    }

    private static int GetRemainingActiveCaseCapacity(
        Contagion_MapTransmissionComponent component,
        ResolvedTransmissionProfile resolvedProfile,
        TransmissionSeeder seeder)
    {
        int activeCaseLimit = seeder?.maxActiveCases > 0 ? seeder.maxActiveCases : resolvedProfile.Profile.maxActiveCases;
        if (activeCaseLimit <= 0)
        {
            return int.MaxValue;
        }

        int activeCases = ContagionTransmissionUtility.CountActiveCases(component.Map, resolvedProfile);
        return Mathf.Max(0, activeCaseLimit - activeCases);
    }

    private static GroupSeedingPolicy GetGroupPolicy(ContagionArrivalGroupKind groupKind)
    {
        switch (groupKind)
        {
            case ContagionArrivalGroupKind.WandererJoin:
                return new GroupSeedingPolicy(8f, 0f, 1, 0.30f);
            case ContagionArrivalGroupKind.QuestGuest:
                return new GroupSeedingPolicy(8f, 0.35f, 3, 0.30f);
            case ContagionArrivalGroupKind.QuestJoiner:
                return new GroupSeedingPolicy(14f, 0.45f, 5, 0.45f);
            case ContagionArrivalGroupKind.HostileRaid:
                return new GroupSeedingPolicy(6f, 0.55f, 8, 0.20f);
            case ContagionArrivalGroupKind.TribalRaid:
                return new GroupSeedingPolicy(12f, 0.65f, 12, 0.30f);
            case ContagionArrivalGroupKind.FarmAnimals:
                return new GroupSeedingPolicy(10f, 0.40f, 4, 0.30f);
            default:
                return new GroupSeedingPolicy(6f, 0.35f, 3, 0.30f);
        }
    }

    private static float GetDiseaseClusterFactor(TransmissionProfile profile)
    {
        if (profile?.vectors == null)
        {
            return 1f;
        }

        bool hasAirborne = false;
        bool hasSocial = false;
        bool hasFomite = false;
        bool hasProximity = false;
        bool hasEnvironmental = false;
        bool hasFoodborne = false;
        for (int i = 0; i < profile.vectors.Count; i++)
        {
            switch (profile.vectors[i])
            {
                case Vector_Airborne:
                    hasAirborne = true;
                    break;
                case Vector_Social:
                    hasSocial = true;
                    break;
                case Vector_Fomite:
                    hasFomite = true;
                    break;
                case Vector_Proximity:
                    hasProximity = true;
                    break;
                case Vector_Environmental:
                    hasEnvironmental = true;
                    break;
                case Vector_Foodborne:
                    hasFoodborne = true;
                    break;
            }
        }

        float factor = 1f;
        if (hasAirborne)
        {
            factor += 0.45f;
        }

        if (hasSocial)
        {
            factor += 0.25f;
        }

        if (hasFomite)
        {
            factor += 0.15f;
        }

        if (hasProximity && !hasAirborne && !hasSocial)
        {
            factor *= 0.75f;
        }

        if ((hasEnvironmental || hasFoodborne) && !hasAirborne && !hasSocial && !hasFomite && !hasProximity)
        {
            factor *= 0.5f;
        }

        return Mathf.Clamp(factor, 0.5f, 1.85f);
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

        float directorMultiplier = component.DiseaseDirector.GetChanceMultiplier(resolvedProfile.Profile);
        float adjustedMtbDays = mtbDays / Mathf.Max(0.01f, outbreakMultiplier * directorMultiplier);
        if (!Rand.MTBEventOccurs(adjustedMtbDays, 60000f, 2500f))
        {
            return;
        }

        ContagionDiagnostics.Record(ContagionDiagnosticCounter.ContinuousSeederAttempted);
        bool seeded = weightSelector == null
            ? ContagionSeedingExecutionUtility.TrySeedRandomEligiblePawn(spawnedPawns, resolvedProfile, component.Map, out Pawn seededPawn)
            : ContagionSeedingExecutionUtility.TrySeedWeightedEligiblePawn(spawnedPawns, resolvedProfile, component.Map, weightSelector, out seededPawn);
        if (!seeded)
        {
            ContagionDiagnostics.Record(ContagionDiagnosticCounter.ContinuousSeederNoEligiblePawn);
            ContagionDiagnostics.Trace($"{seeder.GetType().Name} fired for {resolvedProfile.DiseaseDef.defName} but found no eligible pawn.");
            return;
        }

        component.NotifySeederFired(resolvedProfile, seeder);
        component.DiseaseDirector.NotifySeeded(resolvedProfile.Profile, 1);
        ContagionDiagnostics.Record(ContagionDiagnosticCounter.ContinuousSeederSeeded);
        ContagionDiagnostics.Trace($"{seeder.GetType().Name} seeded {resolvedProfile.DiseaseDef.defName} on {seededPawn.LabelShortCap}.");
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

    private static HediffDef GetForcedArrivalDisease(Contagion_MapTransmissionComponent component)
    {
        if (component == null)
        {
            return null;
        }

        if (Contagion_Mod.Settings?.DeveloperDiagnosticsEnabled != true)
        {
            if (component.DeveloperForcedArrivalDisease != null)
            {
                component.DeveloperClearForcedArrival();
            }

            return null;
        }

        return component.DeveloperForcedArrivalDisease;
    }

    private static List<ArrivalCandidate> FilterArrivalCandidates(List<ArrivalCandidate> arrivalCandidates, HediffDef diseaseDef)
    {
        if (arrivalCandidates == null || diseaseDef == null)
        {
            return arrivalCandidates ?? new List<ArrivalCandidate>();
        }

        List<ArrivalCandidate> filteredCandidates = new List<ArrivalCandidate>();
        for (int i = 0; i < arrivalCandidates.Count; i++)
        {
            if (arrivalCandidates[i].ResolvedProfile.DiseaseDef == diseaseDef)
            {
                filteredCandidates.Add(arrivalCandidates[i]);
            }
        }

        return filteredCandidates;
    }

    private static bool IsForcedArrivalGroupQualifying(
        List<ArrivalCandidate> arrivalCandidates,
        IReadOnlyList<Pawn> groupPawns,
        Map map)
    {
        if (arrivalCandidates == null || arrivalCandidates.Count == 0 || groupPawns == null || map == null)
        {
            return false;
        }

        for (int i = 0; i < arrivalCandidates.Count; i++)
        {
            if (BuildCarrierCandidates(groupPawns, arrivalCandidates[i].ResolvedProfile, map).Count > 0)
            {
                return true;
            }
        }

        return false;
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
        // True when the disease resolves only through environmental exposure — no arrival
        // carriers and no animal-linked handler seeding. Storyteller and acausal seeders are
        // compatible: Storyteller is the Mode 1 trigger that opens the window, and acausal
        // is the isolated-colony backstop. Neither implies a "carrier arrives" resolution.
        if (GetSeeder<Seeder_Environmental>(profile) == null)
        {
            return false;
        }

        if (GetSeeder<Seeder_Arrival>(profile) != null)
        {
            return false;
        }

        if (GetSeeder<Seeder_AnimalLinked>(profile) != null)
        {
            return false;
        }

        return true;
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
