# Seeding Redesign — Implementation Handoff

*Drafted 2026-05-28 after a design discussion between the project owner and an LLM collaborator. This is the working document for the seeding-model rewrite. The user-facing description lives in [`DESIGN.md`](DESIGN.md) → "How Outbreaks Begin." Vanilla code paths and existing hook points are documented in [`IMPLEMENTATION.md`](IMPLEMENTATION.md). This file is the bridge between them.*

---

## 1. What This Is

The existing seeding code runs four independent paths in parallel:

1. **Storyteller intercept** (`Patch_IncidentWorker_Disease`) — converts a vanilla disease incident into 1 immediate incubation seed.
2. **Arrival seeder** (`ContagionArrivalUtility` + arrival patches) — evaluates incoming groups as one exposure event, then seeds a capped carrier payload.
3. **Environmental seeder** (`Contagion_MapTransmissionComponent.RunEnvironmentalExposurePass`) — continuous biome+temperature+water risk every 2500 ticks.
4. **MTB seeders** (`Contagion_MapTransmissionComponent.RunGeneralSeederPass`) — `Seeder_Acausal` and `Seeder_AnimalLinked` fire on long MTBs.

These paths mostly do not collide, but the mental model is muddled and they don't compose. The redesign collapses them into **two clean modes** the player chooses in mod settings:

- **Mode 1 — Storyteller-driven (default).** The vanilla storyteller still picks diseases on its biome-aware schedule. Each pick becomes a *pending event* with a per-disease window; fulfillment strategies (arrival, animal-contact, environmental window, acausal) resolve it.
- **Mode 2 — Contagion-driven.** Contagion runs all pacing. Continuous low-rate seeding from all source paths. Storyteller picks for profiled diseases are cancelled outright. A map-level disease director raises chance after quiet stretches and suppresses it after recent introductions or active sickness.

**The transmission engine, vectors, incubation, immunity, masks, suppression — none of these change.** Only the orchestration layer above them changes.

---

## 2. The Design Conversation, Decisions Locked In

The conversation reached lock-in on these points. Implementer should treat them as fixed unless they hit a concrete blocker.

### Mode 1 — Storyteller-driven (default)

- **Storyteller is the scheduler, not a vector.** Intercept its disease pick. Don't seed a carrier immediately; create a pending event.
- **Pending events have per-disease expiry windows.** Long enough for the typical fulfillment path to land, short enough that the storyteller's event-spacing logic stays meaningful. Concretely:
  - Flu — 15 days (arrivals are common on most maps)
  - Plague — **5 days** (must resolve close to when the storyteller picked it, or it will collide with raids the storyteller deliberately spaced apart)
  - Gut worms — **0 days** (no outside vector; immediate acausal seed; no waiting)
  - Malaria, Sleeping Sickness — converts to a time-bounded environmental event with an `infectionBudget`; not a "pending" entry in the same sense
- **Strategy ranking per disease** (first that can resolve wins, fallthrough to next):
  - Flu: Arrival → Acausal
  - Animal_Flu: Animal-arrival → Acausal
  - Plague: Animal-contact → Arrival → Acausal
  - Animal_Plague: Animal-contact → Animal-arrival → Acausal
  - GutWorms: Acausal (immediate)
  - Malaria / SleepingSickness: Environmental window (event-scoped)
- **Arrival fulfillment is deterministic, not random.** The *next eligible arriving group* resolves the event. Carrier count is capped and scales sublinearly with eligible group size and disease cluster factor. This avoids unbounded pending-event growth on low-traffic maps while letting large groups carry more than one case.
- **Animal-contact fulfillment.** If a pending plague event exists and animals are on the map, the event resolves within the window onto a handler-biased pawn. Near-deterministic on animal-bearing maps. `Seeder_AnimalLinked.mtbDays` is Mode-2-only; Mode 1 doesn't gate by MTB.
- **Environmental fulfillment.** Storyteller's malaria/sleeping-sickness pick opens a time-bounded environmental window on the map. Continuous `Vector_Environmental` exposure runs for `windowDays`, capped by `infectionBudget` (event-scoped, distinct from colony-wide `maxActiveCases`). When budget exhausted or window expires, event clears.
- **Acausal expiry.** Silent single-pawn incubation on a random eligible pawn. The final fallback when the window closes unfulfilled, or the immediate resolution for diseases with no outside path.
- **Cooldown / maxActiveCases interactions.** In Mode 1, `cooldownDays` on individual seeders is redundant — the storyteller paces fires. `maxActiveCases` still applies: if the cap is hit, the pending event is dropped on creation (don't even queue it).

### Mode 2 — Contagion-driven

- **All storyteller disease incidents for profiled diseases are cancelled outright.** Option (a) from the discussion: cancel, do nothing else. Pressure + continuous seeding produce the cadence. Unprofiled diseases pass through to vanilla untouched.
- **Group arrival exposure.** Every neutral group, wanderer, quest arrival, hostile raid, and farm-animal wander-in is evaluated as one incoming group. `Seeder_Arrival.arrivalChance` is the disease's base group exposure chance; code policy multipliers tune group type. If exposure succeeds, Contagion picks one disease for the group and seeds a capped, sublinear number of carriers.
- **Continuous environmental exposure.** Same engine as the current `RunEnvironmentalExposurePass`. Gated by biome commonality, season, temperature, water proximity, indoor sheltering.
- **`Seeder_AnimalLinked` MTB stays.** Periodic animal-linked seeding when animals present, biased to handlers. Current implementation is correct for Mode 2.
- **`Seeder_Acausal` MTB stays.** Isolated-colony backstop, especially for gut worms (no other path).
- **Disease director.**
  - Mode 2 owns a `ContagionDiseaseDirector` per map; Mode 1 never consults it.
  - The director tracks quiet-time pressure debt, recent disease introductions, and normalized human/animal sickness burden.
  - Successful seeding spends pressure once per exposed group or continuous seeder fire, even when a group seeds several carriers.
  - Human profiles use the human burden/debt channel; animal-only profiles use the animal channel.
  - Current colonist/prisoner sickness and recent seeding suppress future introductions, so good quarantine still buys breathing room.

### Hostile raid hook (both modes)

- Hook the enemy raid worker's `PostProcessSpawnedPawns` (or equivalent — confirm the exact extension point in [`RimWorld/IncidentWorker_Raid.cs`](../Rimworld_References/Rimworld%201.6%20Decompiled%20Source/RimWorld/IncidentWorker_Raid.cs)).
- **Roll exposure once per raid group.** A 30-raider raid at 1% per pawn would near-saturate every raid with disease. Raids now roll group exposure once, then seed a capped number of carriers using `1 + Poisson(scale * diseaseClusterFactor * sqrt(eligibleRaiders))`.
- Friendly raids (combat allies) are skipped — they leave before incubation completes, so a seed on them never affects the colony.
- **Why this is interesting gameplay:** the prisoner-take loop. Down a raider, take them prisoner, the prison ward turns into a quarantine problem as proximity/airborne vectors spread inside. No new code needed for the spread itself — the existing engine handles it.

### Farm animal wander-in hook

- Hook `IncidentWorker_FarmAnimalsWanderIn.SpawnAnimal` (private; need reflection or a transpiler) or, better, hook the `TryExecuteWorker` postfix and walk the spawned animals.
- Group exposure chance for animal-disease profiles (`Animal_Flu`, `Animal_Plague`) with a farm-animal policy cap.
- This is the natural extension of "wild animals may occasionally carry plague."

### Schema reinterpretation, not rewrite

The existing seeder XML stays valid. Same classes (`Seeder_Storyteller`, `Seeder_Arrival`, `Seeder_Environmental`, `Seeder_AnimalLinked`, `Seeder_Acausal`), same fields. They are *reinterpreted* under each mode:

- Mode 1: seeders describe fulfillment strategies for pending events.
- Mode 2: seeders describe continuous source paths.

A handful of new fields are added (see §3). Existing XML profiles need additions but no deletions.

---

## 3. Data Model Changes

### New types

```csharp
public sealed class PendingDiseaseEvent : IExposable
{
    public HediffDef diseaseDef;
    public int firedTick;
    public int expiryTick;
    public int infectionBudget;          // environmental events only; 0 for others
    public int infectionsApplied;        // bookkeeping for environmental events

    public void ExposeData()
    {
        Scribe_Defs.Look(ref diseaseDef, "diseaseDef");
        Scribe_Values.Look(ref firedTick, "firedTick");
        Scribe_Values.Look(ref expiryTick, "expiryTick");
        Scribe_Values.Look(ref infectionBudget, "infectionBudget");
        Scribe_Values.Look(ref infectionsApplied, "infectionsApplied");
    }
}
```

### New `TransmissionProfile` fields

Add to [`Source/Core/TransmissionProfile.cs`](Source/Core/TransmissionProfile.cs):

```csharp
// Mode 1: how long a storyteller-fired pending event waits for a fulfillment strategy
// before falling back to acausal. 0 = no wait, immediately acausal (gut worms). Environmental
// diseases ignore this — they use Seeder_Environmental.windowDays instead.
public float pendingWindowDays = 5f;

```

### New `Seeder_Environmental` fields

```csharp
public sealed class Seeder_Environmental : TransmissionSeeder
{
    public float baseChanceMultiplier = 1f;

    // Mode 1 only: how long an environmental event window stays open
    public float windowDays = 14f;

    // Mode 1 only: cap on infections from a single environmental event
    public IntRange infectionBudget = new IntRange(2, 5);
}
```

### Settings additions

In [`Source/Settings.cs`](Source/Settings.cs):

```csharp
public enum ContagionSeedingMode
{
    // ORDINAL ORDER IS PERSISTED. Append only.
    Storyteller,   // Mode 1 — default
    Contagion,     // Mode 2
}

// On Contagion_Settings:
public ContagionSeedingMode seedingMode = ContagionSeedingMode.Storyteller;
```

Add a radio-button group to `DoSettingsWindowContents`, mirroring the existing difficulty radio pattern. New translation keys: `Contagion_SettingSeedingMode`, `Contagion_SeedingModeStoryteller`, `Contagion_SeedingModeStorytellerTooltip`, `Contagion_SeedingModeContagion`, `Contagion_SeedingModeContagionTooltip`.

---

## 4. New Map State

Two new tracking structures on `Contagion_MapTransmissionComponent` (or split into a sibling component `Contagion_SeedingComponent` if the existing component is getting large — implementer's call).

### Pending events list

```csharp
private List<PendingDiseaseEvent> _pendingEvents = new List<PendingDiseaseEvent>();
```

`Scribe_Collections.Look(ref _pendingEvents, "pendingEvents", LookMode.Deep)`. Re-init to empty in `PostLoadInit` if null (back-compat with saves made before the redesign — the field is just absent, which Scribe handles by leaving the default).

### Mode 2 disease director

```csharp
private ContagionDiseaseDirector _diseaseDirector = new ContagionDiseaseDirector();
```

The director is scribed on the map component and updated daily only when `seedingMode == Contagion`. Its tuning lives in one class:

```csharp
chanceMultiplier =
    difficultyChanceMult
    * (1f + pressureDebt)
    * (1f / (1f + burdenScale * normalizedBurden))
    * (1f / sqrt(1f + recentSeeding));
```

---

## 5. File-By-File Change List

### Modified files

- **[`DESIGN.md`](DESIGN.md)** — already revised in the same commit as this handoff doc (see "How Outbreaks Begin," "Fulfillment Strategies," "Implementation Status," "Decisions Log").

- **[`Source/Core/TransmissionProfile.cs`](Source/Core/TransmissionProfile.cs)** — add `pendingWindowDays`; add `Seeder_Environmental.windowDays` and `infectionBudget`.

- **[`Source/Settings.cs`](Source/Settings.cs)** — `ContagionSeedingMode` enum, `seedingMode` field, settings UI radio group, `ExposeData` line. Translation keys.

- **[`Source/Core/Contagion_MapTransmissionComponent.cs`](Source/Core/Contagion_MapTransmissionComponent.cs)** — add pending-events list and the Mode 2 disease director; scribe them; mode branching in `MapComponentTick`:
  - **Mode 1.** `RunGeneralSeederPass` becomes a pending-event resolver. For each pending event, evaluate strategies in disease-specific order (you'll need a small dispatch table or per-disease ranking field — `pendingWindowDays > 0` is the gate that says "this is a Mode 1 strategy-driven event"). Animal-contact, arrival-await, and environmental-window strategies all read from `_pendingEvents`. Acausal expiry runs when `Find.TickManager.TicksGame >= expiryTick`.
  - **Mode 2.** `RunGeneralSeederPass` runs the existing MTB seeders (acausal + animal-linked) but multiplies their chances by the director multiplier. `RunEnvironmentalExposurePass` similarly multiplies by director output. Successful seeding calls `DiseaseDirector.NotifySeeded(...)` once per introduction.

- **[`Source/Core/ContagionArrivalUtility.cs`](Source/Core/ContagionArrivalUtility.cs)** — branch on `Contagion_Mod.Settings.seedingMode`:
  - **Mode 1.** For each arriving group, check the map's pending events. The first pending arrival-fulfillable disease that the group can carry seeds a capped carrier payload and clears the pending entry. First match wins (don't seed one group with two pending diseases).
  - **Mode 2.** Compute one group exposure roll from `arrivalChance`, group policy, outbreak frequency, director output, and season. If exposed, choose one disease weighted by chance and seed a capped carrier payload.
- **[`Source/Patches/Patch_IncidentWorker_Disease.cs`](Source/Patches/Patch_IncidentWorker_Disease.cs)** — redirect storyteller fires:
  - **Mode 1.** `TryExecuteWorker` prefix builds a `PendingDiseaseEvent` instead of calling `SeedIncubationToPawns`. For environmental diseases (`UsesEnvironmentalSeedingOnly`), build an environmental window event from `Seeder_Environmental.windowDays` and `infectionBudget`. For diseases with `pendingWindowDays == 0` (gut worms), call the acausal resolver immediately instead of queuing. The trait-driven `ApplyToPawns` path is unchanged in both modes (trait MTB still produces a direct seed).
  - **Mode 2.** `TryExecuteWorker` prefix sets `__result = false` and returns false for any profiled disease — full cancel. `ApplyToPawns` (trait path) is unchanged.

- **[`1.6/Patches/Contagion_Profiles.xml`](1.6/Patches/Contagion_Profiles.xml)** — for each disease, add `<pendingWindowDays>` matching the table in §2; for Malaria/SleepingSickness, add `<windowDays>` and `<infectionBudget>` on their `Seeder_Environmental`.

- **Languages files** — new translation keys for the seeding-mode setting.

### New files

- **`Source/Patches/Patch_IncidentWorker_Raid_PostProcessSpawnedPawns.cs`** — postfix that passes the whole hostile raid group to `ContagionArrivalUtility` with hostile/tribal group context. Roll exposure once, then seed a capped carrier payload.

- **`Source/Patches/Patch_IncidentWorker_FarmAnimalsWanderIn.cs`** — postfix that walks the spawned animals and calls into a new `ContagionArrivalUtility.SeedAnimalArrivals` (or repurposes `SeedArrivals` if it filters species correctly). Confirm in [`RimWorld/IncidentWorker_FarmAnimalsWanderIn.cs`](../Rimworld_References/Rimworld%201.6%20Decompiled%20Source/RimWorld/IncidentWorker_FarmAnimalsWanderIn.cs) which method exposes the spawned animal list.

---

## 6. Implementation Order

Suggested phases, each independently testable:

### Phase A — Scaffolding (no observable behavior change)

1. Add `ContagionSeedingMode` enum and `seedingMode` field to settings, default Storyteller.
2. Add settings UI radio + translation keys.
3. Add `PendingDiseaseEvent` type, `pendingWindowDays` profile field, `Seeder_Environmental.windowDays` / `infectionBudget`.
4. Add pending-events list and the disease director to the map component, with scribe. Don't read from them yet.
5. Build clean. Diagnostics should show no behavior change.

### Phase B — Mode 2 (simpler — closer to current behavior)

1. Add `ContagionDiseaseDirector` and expose its chance multiplier through the map component.
2. Multiply director output into the group exposure roll in `ContagionArrivalUtility` (gated on `seedingMode == Contagion`).
3. Multiply director output into `RunGeneralSeederPass` and `RunEnvironmentalExposurePass` chances.
4. Change `Patch_IncidentWorker_Disease.TryExecuteWorker` Mode-2 branch to full cancel for any profiled disease.
5. Test: switch to Mode 2 in settings, confirm storyteller flu incidents are silently dropped, arrival/environmental seeds happen at expected rates, recent successful seeding and active sickness suppress follow-up rolls.

### Phase C — Mode 1 pending events

1. In `Patch_IncidentWorker_Disease.TryExecuteWorker` Mode-1 branch, build and queue a `PendingDiseaseEvent` instead of calling `SeedIncubationToPawns`. Respect `pendingWindowDays == 0` (acausal-immediate for gut worms) and the environmental branch (build a windowed event for malaria/sleeping sickness).
2. Rewrite `ContagionArrivalUtility.SeedArrivals` Mode-1 branch: drain at most one pending event into an eligible arriving group.
3. In `RunGeneralSeederPass`, add a pass that walks pending events:
   - For animal-linked plague events on maps with animals, seed onto a handler-biased pawn.
   - For events past `expiryTick`, fire acausal seed.
4. Environmental window events: `RunEnvironmentalExposurePass` checks pending environmental events on the map, runs exposure rolls only while the window is open, respects `infectionBudget`, clears event when budget exhausted or window expires.
5. Test: all the scenarios in §9.

### Phase D — New arrival hooks

1. Hostile raid hook (group exposure with capped carrier payload).
2. Farm-animal wander-in hook.
3. Verify in both modes.

### Phase E — Tuning pass

Numbers in §8 are starting points. Play-test and adjust.

---

## 7. Edge Cases and Gotchas

- **Multiple pending events for the same disease.** Don't queue two flu events. On `TryExecuteWorker`, if a pending event already exists for that disease on that map, drop the new one (or extend the expiry — implementer's call; the simpler "drop new" is fine to start).
- **`maxActiveCases` interaction.** Check before queuing a pending event: if the colony is already at the active-case limit, don't queue. The storyteller's pick is just lost — same as the current behavior.
- **Pending events across save/load.** Scribed on the map component; survives reload. Test by saving with a pending event and reloading.
- **Mode switched mid-save.** A Mode 1 → Mode 2 switch with pending events in the queue: either drain them silently or fast-forward to acausal resolution. Recommended: leave the events in the queue but have Mode 2's tick path resolve them via acausal at the next tick (treat them as immediately expired). Don't lose state.
- **Mode 2 → Mode 1 switch.** No pending events exist yet. Switching is fine, no data loss.
- **Trait-driven path.** Sickly's bypass through `ApplyToPawns` is unchanged in both modes — `TryExecuteWorker` is the only entry point we're redirecting. Trait cooldown logic (`Hediff_ContagionTraitSeedCooldown`) keeps working.
- **Foodborne / gut worms.** In Mode 1, gut worms with `pendingWindowDays = 0` should resolve immediately to acausal. In Mode 2, the existing Acausal MTB seeds them. The foodborne `Vector_Foodborne` is the *transmission* path between pawns once seeded — not a *seeding* path. Don't conflate them.
- **Environmental events colliding with continuous Mode 2 environmental.** Mode 2 doesn't use the windowed environmental events — its environmental exposure is always-on. Only Mode 1 uses windows. Make the windowed path conditional on `seedingMode == Storyteller`.
- **Plague window 5 days + animal-linked MTB 120 days.** The current `Seeder_AnimalLinked.mtbDays = 120` will not naturally resolve within 5 days. **In Mode 1, ignore `mtbDays` entirely** — animal-contact is a deterministic strategy (if animals present, fire within window). `mtbDays` is the Mode 2 continuous rate.
- **Mod removal mid-save with pending events.** Pending events are a `MapComponent` list of `IExposable`s — they're harmless on mod removal. Hard rule #1 (custom polymorphic subclasses in `LookMode.Deep` collections) applies: `PendingDiseaseEvent` is our class, so removing the mod silently drops the saved list. Acceptable — the player will see "Could not load PendingDiseaseEvent" warnings but the save loads, and no vanilla path depends on it.
- **`Seeder_Storyteller.seedCountRange` repurposing.** Currently it's the count of pawns the storyteller seeds. Under Mode 1, environmental events use it as the `infectionBudget` source unless the new `Seeder_Environmental.infectionBudget` overrides it. Decide: use the new field exclusively (cleaner) or fall back to `seedCountRange` when missing (back-compat). Recommend the new field.
- **Save-compat hard rule reminder.** New `Scribe_*` fields handle absent values fine on load (defaults). Don't rename any existing fields. The enum ordering rule (Hard Rule #3) means `ContagionSeedingMode` values can never be reordered or removed — append-only.

---

## 8. Starting Numbers

These are first-pass guesses. Expect tuning during Phase E.

### Pending windows (Mode 1)

| Disease | `pendingWindowDays` |
|---|---|
| Flu | 15 |
| Animal_Flu | 15 |
| Plague | 5 |
| Animal_Plague | 5 |
| GutWorms | 0 (immediate acausal) |
| Malaria | uses `Seeder_Environmental.windowDays` |
| SleepingSickness | uses `Seeder_Environmental.windowDays` |

### Environmental event windows (Mode 1)

| Disease | `windowDays` | `infectionBudget` |
|---|---|---|
| Malaria | 14 | 2~5 |
| SleepingSickness | 14 | 2~5 |

### Disease director (Mode 2)

| Field | Default | Notes |
|---|---|---|
| Human/animal pressure debt | 0..5 normal | Quiet maps accumulate debt daily; active burden drains it |
| Recent seeding | daily decay 0.92 | Successful seeding adds `1 + 0.25 * carrierCount` |
| Burden suppression | `1 / (1 + burdenScale * burden)` | Colonists count fully; prisoners count at half weight |
| Chance clamp | 0.1%..10% for arrival candidates | Zero remains zero |

Normal tuning starts at debt gain `0.03/day`, max debt `5`, burden scale `4`, and chance multiplier `1`. Easier slows debt gain and strengthens suppression; Harder raises debt gain and relaxes suppression.

### Group arrival exposure

Keep the existing `Seeder_Arrival.arrivalChance = 0.01` as the disease's base group exposure chance. Mode 2 multiplies it by group policy, outbreak frequency, director output, and season, then rolls exposure once for the whole group. If more than one disease candidate is valid, one disease is chosen weighted by exposure chance.

Carrier count is `1 + Poisson(groupCarrierSqrtScale * diseaseClusterFactor * sqrt(eligiblePawnCount))`, clamped by group policy, active-case cap, and eligible pawn count. Disease cluster factor is derived from vectors: airborne/social/fomite diseases cluster more than proximity-only plague. Carriers are 70% latent incubation and 30% mild visible disease by default; group policies can override the mild-visible share.

Default code policies:

| Group kind | Exposure multiplier | Carrier scale | Carrier cap | Mild visible |
|---|---:|---:|---:|---:|
| Neutral trader/visitor/traveler | 6 | 0.35 | 3 | 30% |
| Wanderer join | 8 | 0.00 | 1 | 30% |
| Quest guests | 8 | 0.35 | 3 | 30% |
| Quest joiner/refugee-style | 14 | 0.45 | 5 | 45% |
| Hostile raid | 6 | 0.55 | 8 | 20% |
| Tribal raid | 12 | 0.65 | 12 | 30% |
| Farm animals | 10 | 0.40 | 4 | 30% |

---

## 9. Test Plan

Run each in dev mode. Check `Player.log` is clean. Check diagnostics counters increment correctly. Incidence diagnostics should cover disease introductions and pending/director lifecycle; spread diagnostics should cover secondary vector transmission and contamination.

### Mode 1

1. **Flu storyteller fire.** Confirm a `PendingDiseaseEvent` appears on the map component. No immediate seed.
2. **Arrival within window.** Spawn a visitor/trader group while a flu pending event exists. Confirm the group seeds a capped carrier payload and the pending event clears.
3. **Window expiry.** Fast-forward 15 days with no arrivals. Confirm an acausal seed on a random colonist; pending event clears.
4. **Plague with animals.** Confirm a plague pending event resolves within 5 days onto a handler-biased pawn.
5. **Plague without animals.** Confirm the event falls through to arrival, then to acausal at 5 days.
6. **Gut worms storyteller fire.** Confirm immediate acausal seed on a colonist; no pending entry persists.
7. **Malaria storyteller fire on a tropical map.** Confirm an environmental window opens, exposure rolls trigger over 14 days, infection budget caps total cases at 2–5.
8. **`maxActiveCases` gate.** Fill the colony with active flu, fire storyteller flu, confirm no pending event is queued.

### Mode 2

1. **Storyteller flu fire.** Confirm fully cancelled — `__result = false`, no seed, no pending event.
2. **Arrivals over a season.** Confirm flu seeds accumulate at the expected rate.
3. **Recent-seeding cooloff.** Force two flu seeds within a day. Confirm subsequent arrival rolls are dampened (compare with verbose diagnostics).
4. **Debt recovery.** Wait through a quiet period. Confirm pressure debt rises and rolls return toward or above baseline.
5. **Burden suppression.** Seed flu into colonists/prisoners and confirm active sickness suppresses new introductions.
6. **Environmental continuous.** On a tropical map, malaria seeds happen continuously through summer with no event window.

### Mode switch mid-save

1. Mode 1 with a pending event → switch to Mode 2 → confirm event resolves via acausal at next tick (or is cleared safely).
2. Mode 2 → switch to Mode 1 → storyteller fires next disease → pending event appears normally.

### Save / load / mod removal

1. Mode 1, save with pending events → reload → events still tracked.
2. Mode 2, save with non-zero director debt/recent seeding → reload → director state is preserved and continues updating.
3. Save with pending events + mod removed → save loads (Hard Rule #1 — `PendingDiseaseEvent` is our class, silently dropped, no crash).

### New hooks

1. Hostile raid in either mode → confirm at most one raider is seeded (check by spawning a 20-pawn raid).
2. Take a sick raider prisoner → confirm prison-mate prisoners catch it via existing proximity/airborne vectors.
3. Farm-animal wander-in event → confirm an animal can arrive incubating Animal_Plague.

---

## 10. Open Questions for Future Tuning

Not blockers — implementer can ship Phase A–E with reasonable defaults and these can be revisited.

- **Pressure curve shape.** `1 / (1 + p)` is gentle. If Mode 2 still feels too swingy, try `1 / (1 + p²)` (sharper falloff) or a clamped exponential.
- **Per-disease pending-event collapse policy.** Currently "drop new if one already exists." Alternative: extend the expiry to give it a fresh window. Drop-new is simpler and matches the storyteller's "you already have flu queued" semantic better.
- **Should Mode 1 environmental events suppress further storyteller environmental picks?** Currently each fire opens a new window. If two malaria fires within 5 days produces two overlapping windows, that may be too much — consider treating it as the same pending bucket as contagious diseases (one at a time per disease).
- **Friendly-raid edge case.** They're currently skipped, but a sick combat ally could plausibly infect a colonist before leaving. Low priority — revisit if play-testing shows it matters.
- **Caravan support.** Still deferred — `spreadsDuringCaravan` is reserved.
- **Should the Mode 2 cancel be silent or produce a debug notification?** A silent cancel is cleaner for players, but a dev-mode "storyteller wanted to fire X, cancelled by Mode 2" trace would help during tuning. Recommend: silent for players, traced under verbose diagnostics.

---

## 11. Quick Reference — Where to Look in Existing Code

| What | File | Key methods |
|---|---|---|
| Current arrival seeder | [`Source/Core/ContagionArrivalUtility.cs`](Source/Core/ContagionArrivalUtility.cs) | `SeedArrivals`, `TrySeedArrivalDisease` |
| Current MTB seeders | [`Source/Core/Contagion_MapTransmissionComponent.cs`](Source/Core/Contagion_MapTransmissionComponent.cs) | `RunGeneralSeederPass`, `TryRunMtbSeeder` |
| Current environmental | same | `RunEnvironmentalExposurePass`, `TryApplyEnvironmentalExposure` |
| Storyteller intercept | [`Source/Patches/Patch_IncidentWorker_Disease.cs`](Source/Patches/Patch_IncidentWorker_Disease.cs) | `Patch_IncidentWorker_Disease_TryExecuteWorker.Prefix` |
| Trait-driven path | same | `Patch_IncidentWorker_Disease_ApplyToPawns.Prefix` |
| Profile schema | [`Source/Core/TransmissionProfile.cs`](Source/Core/TransmissionProfile.cs) | `TransmissionProfile`, `Seeder_*` classes |
| Settings | [`Source/Settings.cs`](Source/Settings.cs) | `Contagion_Settings`, `DoSettingsWindowContents` |
| Disease XML | [`1.6/Patches/Contagion_Profiles.xml`](1.6/Patches/Contagion_Profiles.xml) | Per-disease `<Operation>` blocks |
| Cooldown bookkeeping pattern | [`Source/Core/Contagion_MapTransmissionComponent.cs`](Source/Core/Contagion_MapTransmissionComponent.cs) | `_seederCooldownDiseases` / `_seederCooldownKeys` / `_seederCooldownTicks` parallel lists + `NotifySeederFired` |

---

## 12. Closing Notes

The redesign is a clarification, not a teardown. Read this doc, the relevant sections of `DESIGN.md`, and the four key existing files above before writing any code. The hardest part is the Mode 1 strategy dispatch in the map component — everything else is mechanical.

When in doubt, prefer the simpler implementation. The mental model the player gets to learn is the whole point of the redesign; if the code starts growing layers of strategy-selection abstraction, you're probably over-engineering. Per-disease behavior differences should live in XML and small per-disease branches, not in a class hierarchy.
