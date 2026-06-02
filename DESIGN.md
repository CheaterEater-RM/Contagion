# Contagion — Design

*Single source of truth for the Contagion mod's design. Last updated 2026-05-30.*

For vanilla code paths and hook points, see the engineering companion `IMPLEMENTATION.md`. For agent rules and repo conventions, see `CLAUDE.md`.

---

## Summary

Contagion changes disease acquisition in RimWorld from opaque random outbreaks into understandable cause-and-effect. Vanilla diseases, treatment, immunity races, beds, area restrictions, cleanliness, and penoxycyline stay relevant. The mod adds transmission logic, incubation, and clearer seeding so players can infer why an outbreak started and how to stop it.

This is a **behavior mod, not a content mod.** It adds no new zones, buildings, research, tabs, items, or player-only quarantine systems. Diseases remain vanilla `HediffDef`s; a `TransmissionProfile` `DefModExtension` tags a disease with its transmission behavior. That extension is the only modder-facing API — any modder can make any hediff contagious through XML alone.

---

## Problem Statement

Vanilla RimWorld mostly starts diseases through storyteller incidents. The storyteller picks a disease based on biome, then immediately applies the final disease hediff to a random slice of colonists or animals. That creates three problems:

- disease origin is abstract and hard to read in play
- once the incident fires, transmission inside the colony does not matter
- player counterplay is mostly treatment after the fact, not prevention through layout, hygiene, or isolation

Contagion keeps the same disease defs and treatment game, but changes the acquisition model from "the storyteller infected these pawns" to "a source seeded an infection, and the colony either contained it or failed to."

---

## Goals

- Make disease origins legible in normal play without adding a new tutorial system.
- Keep vanilla `HediffDef` diseases and vanilla treatment behavior intact whenever possible.
- Reuse existing player tools: medical beds, rest, room layout, area restrictions, work priorities, cleaning, masks, and penoxycyline.
- Support future contagious diseases through XML-first configuration; C# only for genuinely new vector logic.
- Preserve species separation by default. Human disease and animal disease remain separate unless a profile opts into crossover.
- A player who never learns about the mod but follows RimWorld common sense (sick pawns to medical beds, dedicated hospital, clean base, mask the sick) should contain most outbreaks through normal play.

## Non-Goals

- No new items, buildings, zones, research, or custom quarantine UI.
- No new player-facing concepts to learn.
- No rewrite of wound infection, mechanites, organ decay, or other non-target conditions.
- No broad replacement of the vanilla health system.
- No first-release caravan contagion simulation. Vanilla caravans keep existing disease behavior until a world-scope design is justified.
- No micromanagement: a sick pawn going to bed and being tended is a sufficient baseline response.

---

## Vanilla Baseline To Preserve

- Disease severity and immunity races come from the existing disease hediff.
- Tending, bed rest, and life-threatening stages remain vanilla.
- Penoxycyline and any future prophylactic hediffs keep working through vanilla immunity checks.
- Food poisoning, rotten food, and filthy kitchens should still feel like RimWorld, not a separate subsystem.

---

## Core Design Principles

**Mechanisms, not content.** The mod adds rules to existing systems rather than inventing parallel objects. A player quarantines by assigning a hospital area, forbidding cooking, or isolating bedrooms — not by learning a new Contagion widget.

**Vanilla-first disease model.** The real disease remains the vanilla hediff. Contagion adds a lightweight incubation layer and transmission rules around it, then hands control back to vanilla once the disease is active. Phases 3–4 of the lifecycle (active illness and recovery) are entirely vanilla.

**Clear sources.** Every outbreak should start from a plausible source: an incoming pawn, a biome/season, dirty food handling, contaminated vomit, animal-linked plague seeding, or a single storyteller-seeded carrier where no better in-world source exists. The mod should not silently recreate vanilla's "five random colonists are sick now" behavior.

**Species separation by default.** Vanilla already distinguishes human and animal disease incidents for flu and plague. Human flu and animal flu are separate infection pools unless a profile explicitly opts into cross-species spread.

**Save-conscious state.** Persistent state lives where RimWorld already saves safely: static data in `DefModExtension`, map-scoped runtime state in `MapComponent`, meal/filth contamination in `ThingComp`, and temporary per-pawn disease state in hidden hediffs only where necessary.

---

## Disease Lifecycle

Every contagious disease follows the same five-phase model. Phases 2–4 wrap the vanilla hediff unchanged:

1. **Seeded** — a source authorizes the disease to begin on a specific pawn.
2. **Incubating** — the pawn carries a hidden incubation hediff (`Hediff_ContagionIncubation`). No symptoms. May be contagious per the profile's incubation curve. On completion it removes itself and applies the real vanilla disease hediff.
3. **Active** — the real vanilla disease hediff progresses normally: symptoms, treatment, immunity race.
4. **Recovering** — vanilla handles the tail end as immunity wins and severity declines.
5. **Temporarily immune** — a hidden immunity source (`Contagion_TemporaryImmunity`, backed by `Hediff_ContagionExpiry`, or a profile-specified custom hediff) prevents immediate re-seeding for `immunityDurationDays`.

The key rule: Contagion adds only phase 1 (incubation wrapper) and phase 5 (immunity tracking). It does not create a parallel disease simulation once symptoms begin.

### Incubation notes

`Hediff_ContagionIncubation` uses severity to track progress (0.01 → 1.0 over the incubation duration). The `incubationInfectivityCurve` evaluates against this severity. Because the hediff is hidden (`Visible => false`), the severity-as-progress approach is invisible to the player. If incubation visibility is added later ("??? disease" at low medical skill), a separate internal progress field would be needed so severity display doesn't read as "how sick they are." Not needed for v1.

### Temporary post-recovery immunity

The mod owns short-term reinfection protection because vanilla `HediffComp_Immunizable` does not cover non-immunizable diseases (gut worms, food poisoning). On recovery, a hidden temporary immunity hediff is applied for `immunityDurationDays`. If a profile sets `immunityHediffDef`, that custom hediff is applied instead and manages its own duration (sister-mod hook).

**Interaction with vanilla immunity.** Vanilla stores per-disease immunity in `Pawn.health.immunity` as an `ImmunityRecord` that rises while sick and decays at `immunityPerDayNotSick` once recovered. `ImmunityHandler.DiseaseContractChanceFactor` returns 0 (fully immune) while the record is at or above 0.6, then linearly ramps the contract chance back to 100% as it decays to 0. For vanilla Flu (-0.06/day) that gives roughly a 7-day fully-safe window after recovery before reinfection becomes possible at all, hitting full susceptibility around day 17. Plague/Malaria/SleepingSickness decay much slower (-0.02 to -0.03/day) so vanilla already provides 30-50 days of meaningful protection. There is **no normal player-facing UI** for this lingering record — it only appears in a dev-mode "Table: Immunities" debug action ([HealthCardUtility](../Rimworld_References/Rimworld%201.6%20Decompiled%20Source/RimWorld/HealthCardUtility.cs)). The active disease's immunity-vs-severity bar disappears with the hediff.

`CanContractDiseaseNow` consults both layers: it short-circuits on the mod's `Contagion_TemporaryImmunity` hediff (hard yes/no), then falls back to `pawn.health.immunity.DiseaseContractChanceFactor(...) > 0` (vanilla's soft factor). So for diseases that set `immunityDurationDays 0`, vanilla's decay is the only gate — reinfection during the same outbreak is possible once vanilla immunity drops below 0.6.

---

## How Outbreaks Begin (Seeding)

Contagion has two seeding modes the player chooses in mod settings. Both share the same source paths and transmission engine — only the question of *when an outbreak starts* differs.

- **Mode 1 — Storyteller-driven (default).** The vanilla storyteller still picks diseases on its biome-aware schedule. Contagion intercepts each pick, turns it into a *pending disease event* with a per-disease expiry window, and fulfils it through whichever source path fits the disease best (arrival, animal contact, environmental window, etc.). On expiry, an acausal seed lands silently. The storyteller stops being a vector and becomes the scheduler.
- **Mode 2 — Contagion-driven.** Contagion runs all pacing itself. Arrivals carry continuous low-rate risk, environmental exposure is continuous, and a map-level disease director raises or suppresses future introductions based on quiet time, recent seeding, and current colony sickness. There is no acausal backstop in Mode 2; colonies that fully avoid a source path are rewarded. Storyteller disease incidents for profiled diseases are cancelled outright; unprofiled diseases (mechanites, other-mod additions) pass through to vanilla untouched.

Mode 1 is the default because the storyteller cadence matches most players' mental model and minimises mod-conflict surface area. Mode 2 is the opt-in for sim-leaning players who want continuous, legible pressure.

### Mode 1: Pending Events and Fulfillment Strategies

When the storyteller fires a disease incident for a profiled disease:

1. The vanilla `IncidentWorker_Disease.TryExecuteWorker` path is intercepted.
2. Instead of seeding a carrier immediately, Contagion records a `PendingDiseaseEvent { diseaseDef, firedTick, expiryTick, infectionBudget }` on the map's pending-events component.
3. The disease's *fulfillment strategy chain* runs in priority order. The first strategy that resolves the event fulfils it and clears the pending entry.
4. If no strategy resolves before `expiryTick`, the acausal fallback fires (silent incubation on a single eligible pawn) and the entry clears.

Strategies are reinterpretations of the existing seeder classes — same data, same gating — under the new framing of "way to fulfil a pending event" rather than "independent seeder on a timer."

| Disease | Strategy chain | Pending window |
|---|---|---|
| Flu | Arrival → Acausal | 15 days |
| Animal_Flu | Animal-arrival → Acausal | 15 days |
| Plague (unified) | Arrival → Acausal | 5 days |
| GutWorms | Environmental window → Acausal | converts to a time-bounded environmental event |
| MuscleParasites | Environmental window → Acausal | converts to a time-bounded environmental event |
| Malaria | Environmental window → Acausal | converts to a time-bounded environmental event |
| SleepingSickness | Environmental window → Acausal | converts to a time-bounded environmental event |

The 5-day plague window is deliberately tight. The goal is for the storyteller's plague pick to resolve close to when it fired, so the storyteller's event-spacing logic (which considers raids, disasters, and other events) stays meaningful. A long pending window would let a disease event collide with a raid the storyteller deliberately spaced apart.

**Arrival fulfillment.** While a pending event exists for a contagious disease, the *next eligible arriving group* resolves it. Exposure is deterministic in Mode 1 because the storyteller event already represents disease pressure; carrier count is capped and scales sublinearly with eligible group size and disease cluster factor. The vanilla "only some pawns are vulnerable" feel comes from susceptibility factors gating eligibility, not from a low base chance.

**Unified plague fulfillment.** Human and animal plague incidents are both treated as unified plague scheduler events. Incoming humans and incoming animals are equally valid carriers; the original incident flavor may bias future tuning, but it does not constrain fulfillment.

**Environmental fulfillment.** The storyteller's pick opens a *time-bounded environmental window* on the map: continuous `Vector_Environmental` exposure runs for the window's duration, capped by an event-scoped `infectionBudget` (distinct from colony-wide scaled active-case caps). When the budget is spent or the window closes, the event clears. If the window expires with budget remaining and the profile has `Seeder_Acausal`, the remaining storyteller request resolves through the acausal fallback rather than disappearing. This matches vanilla's "some pawns get malaria, then the event ends" feel rather than a permanent biome hazard. Parasite diseases can also have arrival seeders for Mode 2; their Mode 1 storyteller events still resolve through the environmental window.

**Acausal fulfillment.** Silent incubation on eligible pawns. Used only as the final Mode 1 expiry fallback when the configured source path fails to spend the storyteller request in time.

### Mode 2: Contagion-Driven Pacing with the Disease Director

Mode 2 disregards the storyteller for profiled diseases and runs continuous, low-rate seeding:

- **Arrivals.** Every neutral group, wanderer, quest arrival, hostile raid, and farm-animal wander-in rolls group exposure once. If exposure succeeds, one disease is chosen for the group and a capped, sublinear number of eligible pawns become carriers.
- **Environmental exposure** runs continuously, gated by biome commonality, season, temperature, water proximity, and indoor sheltering — same engine as Mode 1's environmental windows, just always on.
- **No acausal MTB** runs in Mode 2. If a colony avoids arrivals, environmental exposure, and other configured source paths, that prevention stands.

**Disease director.** Mode 2 owns a `ContagionDiseaseDirector` per map. It tracks human and animal pressure debt, current normalized sickness burden, recent disease introductions, and ticks since the last seeded incident. Quiet colonies accumulate pressure debt; active colonist/prisoner sickness and recent successful seeding suppress new introductions. Animal-only profiles use the animal burden/debt channel, while human profiles use the human channel. A group exposure spends director pressure once, even if it seeds several carriers.

Mode 2 arrival exposure candidates are hard-clamped to 0.1%-10% after group policy, outbreak frequency, director output, and season are applied. Zero remains zero. This keeps director debt and large group multipliers from turning arrivals into guaranteed disease events while still allowing long quiet periods to matter.

Mode 2's storyteller intercept is simpler than Mode 1's: cancel the incident for any profiled disease, do nothing else. The director and continuous seeding produce the cadence on their own.

### Source Path Hooks (shared by both modes)

The vanilla "guest arrives" paths Contagion hooks are listed below. "Spawned at hook?" matters because the arrival path only acts on pawns that are already `Spawned` with a `Map`. Hooks apply no faction or species filter — disease profiles gate via `CanAffect`.

| # | Arrival type | Vanilla path | Hook point | Spawned at hook? | Status |
|---|---|---|---|---|---|
| 1 | Visitors, travelers, trade caravans, skylantern wanderers, tribute collectors | concrete neutral incident `TryExecuteWorker` postfixes | newly spawned pawns after success | yes | Covered |
| 2 | Wanderer joins | `IncidentWorker_WandererJoin.TryExecuteWorker` | newly spawned pawn after success | yes | Covered |
| 3 | Quest arrivals: refugees, lodgers, shuttle allies, returning lent pawns, reward joiners | `QuestPart_PawnsArrive.Notify_QuestSignalReceived` | public `pawns` field | walk-in yes / drop-pod no | Covered (walk-in); pod-mode skipped by design |
| 4 | Hostile raids / sieges (prisoner-take vector) | enemy raid worker `PostProcessSpawnedPawns` | shared with raid spawn | yes | Covered — group exposure with tribal/hostile policies |
| 5 | Farm animals wander in | `IncidentWorker_FarmAnimalsWanderIn.TryExecuteWorker` | newly spawned animals after success | yes | Covered — animal profile eligibility gates disease |
| 6 | Wild man wanders in | `IncidentWorker_WildManWandersIn.TryExecuteWorker` | inline spawn | yes (needs discovery) | Skipped (feral, low value) |
| 7 | Friendly raid (combat allies) | shared raid path | shared | yes | Skipped (transient — leaves before incubation completes) |
| 8 | Game-ended wanderers join | `IncidentWorker_GameEndedWanderersJoin` | `startingAndOptionalPawns` | yes | Skipped (endgame) |
| 9 | Creep joiner (Anomaly) | quest / `BaseCreepJoinerWorker` | quest path | varies | Skipped (anomalous entity) |
| 10 | Orbital traders | `IncidentWorker_OrbitalTraderArrival` | — | no map pawns | N/A |
| 11 | Caravan meetings / world incidents | world-scope | — | no | Deferred (caravan scope) |

Refugees in RimWorld 1.6 do not have a dedicated `IncidentWorker_RefugeeChased` / `RefugeePodCrash` — they arrive through the quest system via `QuestPart_PawnsArrive` (row 3). The drop-pod-mode exception is intentional: pod-mode quest arrivals are still inside the incoming pod when the quest signal fires, so they are not `Spawned` yet and arrival seeding correctly skips them. The walk-in case (the common one) is fully covered.

The hostile-raid hook deliberately treats raids as a low-frequency, high-impact vector. Raid exposure is rolled once for the group, then carrier count scales sublinearly with eligible raiders and disease cluster factor, with tribal raids tuned higher than pirate/outlander raids. The existing transmission engine already handles prisoner-to-prisoner spread via proximity and airborne vectors. Deliberately nursing a downed raider, taking them prisoner, and watching the prison ward become a quarantine problem is an emergent loop the design encourages.

### Trait Interactions (Sickly and friends)

Vanilla `TraitDegreeData.randomDiseaseMtbDays` lets a trait roll a random biome-weighted disease on a per-pawn MTB, separate from the storyteller's colony-wide disease cycle. **Sickly** is the canonical user — that field is literally how Sickly catches diseases more often than other colonists. Other mods can use the same field for custom traits and genes.

This path calls `IncidentWorker_Disease.ApplyToPawns` directly, bypassing `TryExecuteWorker`. Contagion's `TryExecuteWorker` prefix cancels the storyteller path entirely, so any call that reaches the `ApplyToPawns` prefix is, by elimination, trait-driven. That gives a free, reliable discriminator with no extra state.

**The amplification problem.** In vanilla, Sickly is contained: that one pawn gets sick, recovers, colony untouched. Under Contagion, a Sickly pawn becomes a recurring patient-zero — they catch flu via the trait MTB, then shed it via airborne/social/fomite vectors. That's a real behavior change for a trait the player may have picked without knowing the mod amplifies it.

**Compensating mechanics ship with the trait.** Rather than carve a special "this incubation doesn't spread" case (which would break the mod's core contract that any active profiled disease can transmit), Contagion adds three things:

- A **per-pawn cooldown** (`Contagion_TraitSeedCooldown`, backed by `Hediff_ContagionExpiry`, 10 days) applied after any successful trait-driven seed. While active, further trait-driven seeds on that pawn are skipped, preventing back-to-back random illnesses.
- A **reduced outward-shedding factor** for Sickly pawns — every shipped human-contagious profile (Flu, Plague, GutWorms) carries a `SourceFactor_Trait` entry that halves Sickly's source infectivity. Sickly catches more, but spreads less; the trait identity becomes "more random misfortune, less of a spreader." Justified in fiction by the trait's boosted Medical skill (hygiene awareness).
- A **vanilla-style "feels unwell" letter** when a trait-driven seed succeeds. The storyteller path stays silent on success (incubation is meant to be a hidden window), but the trait path preserves the vanilla "Sickly Bob caught something" notification — vague about *what* they have, so the player still has the discovery moment, but visible enough to act on with a quarantine.

**Cooldown bucket conflation (known seam).** Trait events currently consume the `Seeder_Storyteller` cooldown and count against the profile's scaled active-case cap — pragmatic, but it conflates "the storyteller picked this disease for the colony" with "this pawn's trait rolled disease for themselves." If trait-driven events ever need independent tuning (different cooldown, different per-disease frequency), the right move is a new `Seeder_Trait` type rather than special-casing inside the storyteller seeder.

---

## Core Data Model

### TransmissionProfile (DefModExtension)

The entire modder-facing contract, attached to any `HediffDef` to make it contagious.

| Field | Type | Default | Purpose |
|---|---|---|---|
| `incubationDays` | float | 1.0 | Duration of hidden incubation phase |
| `immunityDurationDays` | float | 0 | Post-recovery reinfection protection (ignored when `immunityHediffDef` is set) |
| `immunityHediffDef` | HediffDef | null | Custom immunity hediff override; sister-mod hook |
| `targetBodyParts` | List\<BodyPartDef\> | null | Part-targeted application (gut worms → stomach) |
| `incubationInfectivityCurve` | SimpleCurve | null (= flat 0.0) | Infectivity during incubation, keyed on progress |
| `activeInfectivityCurve` | SimpleCurve | null (= default bell) | Infectivity during active disease, keyed on severity |
| `seasonalInfectivity` | SeasonalInfectivity | null (= all 1.0) | Per-season transmission multiplier weights |
| `susceptibilityFactors` | List\<SusceptibilityFactor\> | null (= all equal) | Target-side resistance/vulnerability modifiers |
| `sourceInfectivityFactors` | List\<SourceInfectivityFactor\> | null (= all equal) | Source-side infectivity modifiers |
| `affectsHumans` | bool | true | Species scope |
| `affectsAnimals` | bool | false | Species scope |
| `crossSpeciesTransmissionFactor` | float | 0.0 | Multiplier when transmission crosses the human/animal boundary |
| `vectors` | List\<TransmissionVector\> | required | Spread mechanisms |
| `seeders` | List\<TransmissionSeeder\> | required | Outbreak-initiation mechanisms |
| `useScaledActiveCaseCap` | bool | true | Enables population-scaled active+incubating caps for seeding and suppression load |
| `maxActiveCaseChanceOffset` | float | 0 | Per-disease percentage-point offset to the scaled cap chance |
| `spreadSuppressionScale` | float | 1.0 | Per-disease scaling of colony spread suppression (0 = exempt) |
| `outbreakNotification` | enum | FirstCase | Player notification mode: `None` / `FirstCase` / `EveryCase` |
| `outbreakEndDays` | float | 3.0 | Days after the most recent visible case before the outbreak is considered over. The next case resets back to a red first-case letter. |
| `corpseContagious` | bool | false | When true, animal and humanlike corpses are marked with `Comp_InfectedCorpse` at death. The corpse remains fresh, is visibly/inspectably infected, can be filtered in storage/bills, can expose eaters, and can still enter the butchery contamination chain if a bill explicitly allows it. |
| `showsSickSignal` | bool | false | When true, handlers who interact with an infected animal may notice a "sick" signal hediff that prompts a vet visit. Enables the animal disease chain — see [Animal Disease Chain](#animal-disease-chain). |
| `corpseInfectivityDecayPerDay` | float | 0.5 | *Reserved:* per-day infectivity decay of a contagious corpse as a proximity/fomite source. Not yet implemented. |

Fields marked *Reserved* are present in the schema (so modders can plan) but have no engine implementation in v1. See [Reserved & Future Work](#reserved--future-work).

### Corpse infection state

`Comp_InfectedCorpse` is corpse state: "this specific corpse carries disease X." It stores the `HediffDef` captured at corpse creation and becomes the stable source for corpse filters, inspect text, visuals, ingestion exposure, and butchery contamination.

Hediffs are pawn health state: "this pawn is incubating or has disease X." Dead pawns still exist inside corpses, so older/pre-comp corpses can be recognized by scanning the inner pawn's hediffs as a fallback. New corpses snapshot the hediff into the comp so later behavior does not depend on fragile live-pawn health state.

The visual implementation follows vanilla corpse rendering rather than drawing a separate world overlay: infected corpses tint the rendered pawn body a subtle green, similar to how rotten corpses apply their rotten color, and add inspect text such as "Infected corpse: plague."

### SeasonalInfectivity

Six per-season weights, all defaulting to 1.0 (no seasonal variation).

| Field | Type | Default |
|---|---|---|
| `spring` / `summer` / `fall` / `winter` | float | 1.0 |
| `permanentSummer` (equatorial) / `permanentWinter` (polar) | float | 1.0 |

---

## Infectivity Model

A source pawn's contagiousness is a multiplier, not an on/off window. Two `SimpleCurve`s drive it:

- **`incubationInfectivityCurve`** — X axis is incubation progress 0.0→1.0 (= the incubation hediff's severity). Y axis is the infectivity multiplier. Omitted → flat 0.0 (not contagious during incubation; opt-in for pre-symptomatic spread).
- **`activeInfectivityCurve`** — X axis is the real hediff's severity 0.0→1.0. Y axis is the multiplier. Omitted → a default bell: `(0.0, 0.3), (0.15, 0.7), (0.35, 1.0), (0.65, 1.0), (0.85, 0.3), (1.0, 0.0)` — ramp up, sustained peak, taper to zero near death.

This lets a disease be slightly contagious pre-symptom, peak mid-illness, and taper during recovery. A minimal profile that specifies no curves still works (default bell, no incubation infectivity). Vectors may override the profile curves with `activeInfectivityCurveOverride` / `incubationInfectivityCurveOverride` — e.g. fomite (vomit) transmission peaking at a different severity than airborne.

---

## Seasonal Infectivity

Rather than a raw calendar curve (which ignores hemisphere inversion and equatorial/polar tiles), the engine uses vanilla `SeasonUtility.GetSeason(yearPct, latitude, …)`, which outputs six continuous weights summing to 1.0 and already handles hemisphere flip and smooth ~5-day cross-fades at season transitions. The profile defines a multiplier per season; the engine computes the weighted sum:

```
multiplier = Σ (seasonWeightᵢ × profile.seasonalInfectivityᵢ)
```

All weights default to 1.0, so omitting `seasonalInfectivity` means no seasonal variation.

---

## Susceptibility Factors (Target Side)

A polymorphic list of multipliers applied to the target before the vanilla contract gate. Multiplicative stacking; any 0 factor blocks transmission. Omitted = all pawns equally susceptible.

| Class | Fields | Purpose |
|---|---|---|
| `Factor_Hediff` | `hediff`, `factor` | Hediff presence modifies susceptibility (penoxycyline, immunosuppressants) |
| `Factor_Gene` | `gene`, `factor` | Gene presence modifies susceptibility |
| `Factor_Trait` | `trait`, `factor` | Trait presence modifies susceptibility |
| `Factor_AgeRange` | `minAge`, `maxAge`, `factor` | Age bracket modifies susceptibility |
| `Factor_Stat` | `stat`, `curve` | Pawn stat value scales susceptibility via a modder-provided curve (curve mode only) |
| `Factor_HasInjury` | `factor` | Pawn has any open (non-permanent) wound — enables MRSA-type scenarios |

**Penoxycyline integration:** rather than hardcoding penoxy checks, a profile that should respect it includes a `Factor_Hediff` for `PenoxycylineHigh` with `factor 0.0`. Modders adding prophylactic drugs just add another factor entry — no C#.

**Interaction with vanilla:** the engine also multiplies in `ImmunityHandler.DiseaseContractChanceFactor`, which independently handles gene immunity, `makeImmuneTo`, existing-hediff/duplicate checks, mutant immunity, and non-flesh pawns. Susceptibility factors are a Contagion-layer multiplier applied *before* that vanilla gate.

**Seeders respect susceptibility factors** when selecting targets (a Robust pawn is less likely to be patient zero; penoxycyline at 0.0 blocks seeding). Scaled active-case caps are checked before factor evaluation — they are population gates, not per-pawn checks.

---

## Source Infectivity Factors

Structurally identical to susceptibility factors but evaluated against the contagious source pawn — a cough suppressant or face covering makes a pawn shed less. Multiplicative; omitted = no source-side modifiers. Initial types: `SourceFactor_Hediff`, `SourceFactor_Gene`, `SourceFactor_Stat`.

This is the primary soft-coupling hook for the planned sister mod: it applies a hediff (e.g. `SymptomSuppressant`), and Contagion's profile references it via XML. If the sister mod isn't installed, the hediff never appears and the factor entry is inert.

---

## Pawn-Local Context Model (LOS + Roofing + Bounded Room Air)

Airborne direct plume, social, and proximity vectors do **not** use room identity as a binary gate (a 50-cell hallway is one "room" but shouldn't transmit across its length; two pawns across an open doorway are in different rooms but 2 cells apart). Instead:

- **Roofing** — `map.roofGrid.Roofed(cell)` at both endpoints. Both roofed = enclosed, aerosols concentrate (factor 1.0). Either unroofed = outdoor dispersal (`outdoorFactor`). Per-cell, so roofed courtyards and mountain bases behave correctly.
- **Line of sight** — `GenSight.LineOfSight(source, target, map)`. Clear = air path; blocked by wall/closed door = `obstructedFactor` (airborne default 0.0). Open doors pass LOS (correct: an open doorway is an air path). *Known v1 limitation:* vents are `Impassable` to LOS, so airborne does not pass through them — physically wrong but conservative.
- **Distance falloff** — smooth exponential, not a hard cutoff, out to `maxRange`.
- **Bounded room air** — airborne vectors also have an optional same-room aerosol component. It is not LOS-gated, but only applies in the same non-outdoor room, within `roomAirMaxRange`, up to `roomAirMaxCells`; strength decays by `sqrt(room.CellCount)` so tiny rooms are risky and large/hall-like spaces are dilute.

Rooms also apply where they're the correct abstraction: **same-room aerosol** for airborne, **kitchen cleanliness** for foodborne, and **indoor cleanliness** for proximity (with an outdoor filth-count fallback within `outdoorFilthRadius`).

---

## Transmission Equation

The per-candidate probability for any vector:

```
effectiveChance = vectorBaseChance
                × infectivityMultiplier(source)        ← incubation or active curve (or per-vector override)
                × sourceInfectivityProduct(source)     ← source factor list
                × seasonalMultiplier(map tile)         ← seasonal weight blend
                × susceptibilityProduct(target)        ← target factor list
                × vanillaContractFactor(target)        ← DiseaseContractChanceFactor
                × vectorContextModifiers(...)          ← distance, LOS, roofing, cleanliness, etc.
                × respiratoryMaskFactor(source,target) ← respiratory vectors only
                × spreadSuppression(disease,target)    ← colonist targets, contagious vectors only
                × globalSettingsMultiplier             ← transmission-rate slider × difficulty scale
```

Each term is independently tunable via XML. The engine multiplies them; a 0 in any term blocks transmission.

---

## Spread Suppression

A colony-scoped balancing term that prevents an outbreak from deterministically reaching 100% of the colony. For a contagious roll toward a player-faction pawn, the chance is multiplied by:

```
suppression = (1 - infectedColonyFraction) ^ effectiveStrength
```

- `infectedColonyFraction` = player-faction pawns the profile can affect that already carry it (active or incubating) ÷ all player-faction pawns the profile can affect. Computed once per disease per pass.
- `effectiveStrength` = difficulty suppression strength × the profile's `spreadSuppressionScale`. Strength 0 → factor 1 (disabled).

**Scope rules:**
- **Target-gated:** applied only when the target is a player-faction pawn, matching the population the fraction is measured over. Visitors/raiders/other-faction pawns are neither counted nor throttled.
- **Vectors covered:** airborne, social, proximity, fomite (spread shed by infected colonists into shared space).
- **Vectors excluded:** foodborne (a contaminated-food source, not herd spread) and environmental seeding (sourced by the map). `spreadSuppressionScale = 0` on environmental diseases makes this explicit.

---

## Respiratory Protection (Masks, Lungs, Genes)

Airborne, social, and short-range physical/contact vectors (`Vector_Airborne`, `Vector_Social`, `Vector_Proximity`) share a `RespiratoryVector` base for historical/schema reasons. The shared base reduces transmission based on protection the source and target are actually wearing or carrying, keyed on the vanilla `ToxicEnvironmentResistance` stat so existing gear (gas masks etc.) is immediately relevant. Per side:

```
sideFactor = (1 - airwayBarrierResistance × maskEffectiveness)   ← physical barrier (apparel + body parts)
           × (1 - geneAirwayImmunity      × airwayImmunityFactor) ← whitelisted gene immunity
```

- **`airwayBarrierResistance`** = `ToxicEnvironmentResistance` summed from **worn apparel** (`equippedStatOffsets`) plus **air-filtering body parts / implants** (detoxifier lung, fleshmass lung), clamped 0–1. Counts at full effect, applied to both the susceptible target (less inhaled) and the infectious source (less shed). **Genes are deliberately excluded here** — most genetic toxic tolerance is metabolic, not an airway barrier. Transient drug/disease hediffs that happen to offset the stat are also excluded.
- **`geneAirwayImmunity`** = highest protection among the pawn's active genes that are explicitly whitelisted.

Vector fields: `maskTargetEffectiveness` (default 0.7, inhalation side), `maskSourceEffectiveness` (default 0.5, emission side), `airwayImmunityFactor` (default 1.0; set 0 for contact/flea vectors like plague proximity so breathless doesn't wrongly confer plague immunity). The whole layer can be disabled by the player via the "masks reduce spread" setting.

### Gene whitelist: `RespiratoryImmunityDef`

A standalone, fully patchable `Def` (shipped as `Contagion_RespiratoryImmunity`) lists genes that grant airway immunity. Genes are referenced by `defName` **as plain text**, not a `GeneDef` cross-reference, so listing a Biotech/Odyssey gene is harmless when that DLC is absent (the entry is silently skipped at resolve time). Multiple defs merge; players can `PatchOperation` the shipped def. `protection` is clamped 0–1 (1 ≈ airway-immune, still scaled per-vector by `airwayImmunityFactor`). Ships with breathless (a pawn who barely inhales) as effective airway immunity.

---

## Transmission Vectors

Composable classes. The composition *is* the API: "airborne + social + fomite" is flu, "live flea/contact proximity" is plague, "social only" is a contact disease a modder could build.

### Vector_Airborne (respiratory)
Primary respiratory vector. Direct plume uses distance falloff, roofing-based enclosure, and LOS obstruction. Optional room air is a separate same-room aerosol roll with room-size dilution and a short range cap.
- `baseChancePerCheck` (0.03), `outdoorFactor` (0.15), `maxRange` (10), `distanceFalloffRate` (0.25), `obstructedFactor` (0.0), `roomAirBaseChanceFactor` (0.25), `roomAirMaxRange` (10), `roomAirMaxCells` (100)

### Vector_Social (respiratory)
A booster on respiratory spread that fires on successful social interactions (face-to-face, so no distance falloff). Hooked via a postfix on `Pawn_InteractionsTracker.TryInteractWith`, evaluated in both directions.
- `baseChancePerInteraction` (0.02), `outdoorFactor` (0.5)

### Vector_Proximity (short-range physical/contact)
Short-range physical spread modulated by cleanliness. For plague this is live-host flea/contact transfer, not random near-person infection. It uses reachable path distance, so walls and closed doors block while open doors pass during the check. Uses indoor room cleanliness or an outdoor filth-count fallback. `airwayImmunityFactor` is typically set to 0 for plague (contact, not airway).
- `baseChancePerCheck` (0.025), `maxRange` (6), `distanceFalloffRate` (0.35), `cleanlinessImpact` (1.0), `outdoorFactor` (0.75), `outdoorFilthRadius` (4)

### Vector_Environmental
Ambient exposure for malaria/sleeping sickness and environmental parasites — seeded by the map, no person-to-person spread. Temperature factor (zero below `minTemperature`, peaks at `peakTemperature`), water-proximity factor, indoor shelter falloff by depth from the nearest unroofed cell, an optional human hygiene reduction, and an AC/cool-room reduction. Biome-gated via vanilla `BiomeDef.CommonalityOfDisease`.
- `baseChancePerCheck`, `humanExposureFactor` (1.0), `minTemperature` (15), `peakTemperature` (30), `waterProximityRadius` (10), `waterProximityWeight` (0.02), `indoorReductionPerCellFromEdge` (0.1), `coolRoomThreshold` (18)

### Vector_Fomite
Contaminated **vomit** filth (scoped to vomit so contamination is visible and cleanable). When a contagious pawn vomits, the resulting `Filth_Vomit` is tagged; pawns stepping on it roll for transmission, with potency decaying over time. Cleaning removes it. Activates mainly during severe, un-quarantined cases — a "you let this get out of hand" escalation.
- `contaminatesVomit` (true), `baseChancePerContact` (0.03), `potencyDecayPerHour` (0.1)

### Vector_Foodborne
Transmission through food — from infected cooks producing contaminated meals, contaminated raw meat being cooked into meals, or a pawn directly eating an infected corpse. `Comp_ContaminatedFood` sits on prepared meals, raw meat (all `MeatRaw` category items), and kibble. Contamination is baked in at production time and decays to zero after `contaminationExpiryDays` so year-old preserved food cannot start new outbreaks. Kitchen cleanliness modifies the contamination factor at cook time. Not subject to spread suppression.

**Contamination sources:**
- *Infected cook:* `Comp_ContaminatedFood.Notify_RecipeProduced` checks the cook's active disease profiles for a foodborne vector. Infectivity × kitchen cleanliness factor is stamped onto the meal.
- *Infected meat (butchering chain):* `Patch_Corpse_ButcherProducts` stamps raw meat with full (1.0) contamination when the butchered corpse carries a `corpseContagious` disease that can affect humans. Animals use the butcher's Animals skill to notice; humanlike corpses use Medicine. If the butcher notices, the products are discarded instead.
- *Direct corpse ingestion:* `Patch_Thing_Ingested` lets any pawn that eats an infected corpse roll/seed exposure through the normal Contagion incubation path. Animals may choose infected corpses naturally. Humanlike pawns reject infected corpses in automatic food search but can still be ordered to eat one from the right-click menu, which appends an infection warning.
- *Ingredient propagation:* `Patch_GenRecipe_MakeRecipeProducts` picks up contamination from any raw ingredient and propagates it to the produced meal, reduced by the recipe's `CookingContaminationExtension.reductionFactor`.

**Cooking reduction factors** (lower = safer):
| Food type | Factor |
|---|---|
| Survival meal | 0.05 |
| Lavish meal (all variants) | 0.10 |
| Fine meal (all variants) | 0.20 (code default) |
| Simple meal | 0.35 |
| Kibble | 0.60 |
| Pemmican | 0.70 |
| Raw meat | 1.0 (no reduction) |
| Nutrient paste | 0 (dispenser bypasses `MakeRecipeProducts` entirely — always safe) |

- `baseChancePerMeal` (0.08), `cleanlinessImpact` (1.0), `contaminationExpiryDays` (30)

### Vector_Lovin
*Reserved extensibility hook.* Not used by any shipped disease and **not currently wired to a job hook.** Present so future STD-style mods can use the framework.
- `baseChancePerAct` (0.15)

---

## Fulfillment Strategies (formerly Seeders)

The seeder classes describe *how an outbreak event resolves*. In Mode 1 they are fulfillment strategies for a pending storyteller-driven event; in Mode 2 they are continuous source paths. The schema is the same; the framing changes per mode.

Shared base field: `cooldownDays` (minimum gap between events of this type — primarily a Mode 2 throttle, redundant in Mode 1 where the storyteller paces fires). When a target track is at/above the profile's scaled active-case cap, strategies for that track are suppressed.

| Strategy | Mode 1 role | Mode 2 role | Key fields |
|---|---|---|---|
| `Seeder_Storyteller` | Driver. The storyteller's pick is intercepted and turned into a pending event; `seedCountRange` becomes the event's initial infection budget for environmental events. | Cancelled and discarded. | `seedCountRange` |
| `Seeder_Arrival` | Fulfillment: the next eligible arriving group resolves the pending event into a capped group payload. | Continuous group exposure roll; if exposed, one disease and a capped carrier payload. | `arrivalChance` |
| `Seeder_Environmental` | Fulfillment: opens a time-bounded environmental exposure window with `infectionBudget`. | Continuous environmental exposure (no event window). | `baseChanceMultiplier`, `windowDays` (Mode 1), `infectionBudget` (Mode 1) |
| `Seeder_AnimalLinked` | Fulfillment: requires animal presence; resolves onto a handler-biased pawn within the window. | Reserved for future source paths; no shipped disease currently uses it in Mode 2. | `mtbDays`, `requiresAnimalsOnMap`, `handlerBias` |
| `Seeder_Acausal` | Pending-event expiry fallback for diseases whose other strategies failed to resolve within the window. | Ignored. Mode 2 has no acausal backstop. | `mtbDays` (legacy/Mode 1 tuning only) |

---

## Animal Disease Chain

For diseases where `corpseContagious true` and `showsSickSignal true`, Contagion runs a full animal carrier pipeline. This is a major reservoir and food-chain mechanism for gut worms and muscle parasites. The chain has three stages:

### Stage 1 — Animal acquisition (environmental vector)

Wild and outdoor animals are exposed through `Vector_Environmental` just like human pawns. Animals kept indoors (roofed barn) receive the existing `indoorReductionPerCellFromEdge` shelter reduction, so they accumulate much less exposure than animals grazing outside. This naturally distinguishes:

- **Wild animals** on the map: full outdoor exposure, high carrier rate.
- **Outdoor livestock** (open pens): moderate exposure.
- **Indoor livestock** (roofed barns): minimal to zero exposure.

Because `crossSpeciesTransmissionFactor 0.0` blocks the pawn-to-pawn vectors, the environmental vector and arrival carrier seeding are the ways animals acquire the parasite. Person-to-animal spread through proximity/airborne does not occur.

### Stage 2 — Detection and diagnosis ("Sick" signal)

Infected animals do not immediately reveal their condition. Detection is handler-driven:

- **Detection:** When a colonist performs an `AnimalChat` interaction with an infected animal (training, tending, feeding), they roll `Animals skill / 20` as a detection chance. On success, the `Contagion_AnimalSick` hediff is applied — a visible, tendable signal that appears in the animal's health bar. A 3% false-positive rate applies to uninfected animals, so not every sick signal indicates real disease.
- **Sick signal behavior:** `Contagion_AnimalSick` is static (no severity progression) and self-clears if untreated by rolling once per day: 20%, then +10 percentage points per day, with a forced clear on day 5. It blocks the animal from the auto-slaughter queue while present.
- **Diagnosis:** When a doctor tends `Contagion_AnimalSick`, a unified diagnostic roll (`ContagionDiagnosticSkillUtility`) determines the outcome:
  - **True positive, roll passes:** Incubation collapses to mild active disease (severity 0.1). A letter fires. The player now sees the disease in the health tab.
  - **True positive, roll fails (false negative):** Sick signal cleared, disease stays hidden. A diagnosis cooldown prevents immediate re-presentation; the animal can get "sick" again after that cooldown expires.
  - **False positive (no underlying disease):** Sick signal cleared. "Nothing concerning found" message.

  The diagnostic roll uses Medical as the primary skill (sigmoid: ~75% at score 10, ~95% at score 15). Animals skill supplements at 0.60× weight with diminishing returns as Medical rises (capped at 14 raw support), reflecting a rancher's practical eye. Sight scales the whole result (30% floor, 140% cap). The Medical Specialist Ideology role gives a 1.5× Medical bonus. A skilled handler without a dedicated medic can still diagnose reliably; a mixed colony with a doctor is faster.

**Butchering contamination notice** uses the same utility with `isButchery: true`: Animals weight drops to 0.25× (less relevant when cutting than examining), Cooking adds at 0.60× (knowing what bad meat looks like).

### Stage 3 — Corpses, butchering, and scavenging

When an animal or humanlike pawn carrying a `corpseContagious` disease dies, the corpse remains a normal fresh corpse and receives `Comp_InfectedCorpse`. The comp snapshots the disease from the inner pawn's hediffs at spawn time. This avoids the old rot-for-safety workaround and lets normal RimWorld corpse systems decide what happens next.

**Auto-slaughter exclusion:** Sick-signalled animals (`Contagion_AnimalSick` present) are excluded from the auto-slaughter queue. The player must resolve the signal before the animal is automatically queued for slaughter.

**Storage and filters:** Contagion adds two corpse special filters:
- `AllowInfectedCorpses`, allowed by default.
- `AllowUninfectedCorpses`, allowed by default.

Because both are allowed by default, stockpiles accept all corpses unless the player deliberately separates infected-only or clean-only storage. Filter workers read `Comp_InfectedCorpse`, with fallback hediff scanning for older/pre-comp corpses.

**Butcher bill safety:** New `ButcherCorpseFlesh` bills disallow `AllowInfectedCorpses` by default, and a one-time save migration applies the same safety default to existing butcher bills. Players can opt a specific bill back in by enabling infected corpses in that bill's ingredient filter. This keeps storage permissive while making food production conservative.

**Butchering contamination:** `Patch_Corpse_ButcherProducts` runs when a corpse is infectious (detected via the transmission-facing `TryGetCorpseInfectionForTransmission`, which includes hidden/undiagnosed disease — infected meat is infectious whether or not the disease was ever revealed):
1. **Known infection:** if the corpse already displayed an infected *or* suspected-infected label (`IsInfected` / `IsSuspectedInfected`), the player saw the warning and allowed butchering via the bill filter. Skip the notice roll and go straight to contamination — never silently discard products the player chose to butcher.
2. **Unknown infection:** the butcher stumbled onto it mid-job. Roll a notice chance via `ContagionDiagnosticSkillUtility` (`isButchery: true`). Medical is primary; Animals adds at 0.25× weight for animal corpses; Cooking adds at 0.60× weight for both. Sight-scaled; Medical Specialist bonus applies. This is a deliberately weak fallback (~15% at low skill, ~50% at medical 10) now that players can run a dedicated post-mortem inspection to diagnose suspicious corpses on demand.
   - **Notice:** all products are discarded, the butcher sends an alert, the remnants are forbidden.
   - **Miss:** falls through to contamination.
3. **Contamination:** each raw meat product that has `Comp_ContaminatedFood` receives `contaminationFactor 1.0` (full contamination), with the timestamp set for `contaminationExpiryDays` expiry. Contamination is carried onto split-off stacks via `Comp_ContaminatedFood.PostSplitOff`, so every stack from one corpse stays infected, not just the first.

**Ingredient propagation:** `Patch_GenRecipe_MakeRecipeProducts` propagates contamination from raw ingredients to cooked products, reduced by the recipe's `CookingContaminationExtension.reductionFactor`. See [Vector_Foodborne](#vector_foodborne) for the full reduction table.

**Corpse ingestion:** Animals can naturally choose and eat infected corpses. Humanlike pawns reject infected corpses in automatic food search via `FoodUtility.WillEat(Thing)`, but the normal right-click ingest command remains available and warns that the corpse is infected. When any pawn eats an infected corpse, Contagion rolls/seeds exposure through the same incubation utility used by the other spread paths.

### Animal Flu — explicitly excluded

Animal flu has `affectsHumans false` and no `corpseContagious`, so:
- Butchering flu-infected animals produces clean meat.
- The `Patch_Corpse_ButcherProducts` hook checks `resolvedProfile.Profile.affectsHumans` before contaminating; it skips flu-infected corpses.
- No "sick" signal for animal flu (no `showsSickSignal`), so no vet-visit overhead.

---

## Shipped Disease Configurations

Vanilla disease profiles are patched onto their `HediffDef` in `1.6/Patches/Contagion_Profiles.xml`. `Contagion_MuscleParsites` is a new `HediffDef` defined in `1.6/Defs/Contagion_DiseaseDefs.xml`.

| Disease | Vectors | Seeders | Incubation | Immunity | Species | Notes |
|---|---|---|---|---|---|---|
| Flu | Airborne, Social, Fomite (vomit) | Storyteller, Arrival, Acausal | 1.5 d | none* | Human | Seasonal (winter-peaking); acausal is Mode 1 fallback only; scaled active-case cap offset 0 |
| Animal_Flu | Airborne, Fomite | Storyteller, Arrival, Acausal | 1.5 d | none* | Animal | Species-isolated; acausal is Mode 1 fallback only; safe to butcher (no `corpseContagious`) |
| Plague | Proximity (cleanliness) | Storyteller, Arrival, Acausal | 1.0 d | none* | Human + Animal | Unified cluster: `animalVariantDef Animal_Plague` (48 h tend); `crossSpeciesTransmissionFactor 0.5`; incoming humans and animals can carry it; `airwayImmunityFactor` 0; `corpseContagious`; `showsSickSignal`; separate scaled human/animal caps, offset 0 |
| GutWorms | Foodborne, Fomite (vomit), Environmental (water) | Storyteller, Environmental, Arrival, Acausal | 3.0 d | 15 d | Human + Animal | `corpseContagious`; `showsSickSignal`; `targetBodyParts: Stomach`; water-primary environmental; humans use `humanExposureFactor 0.50`; Mode 2 uses environmental + arrival; scaled active-case cap offset 0; `spreadSuppressionScale 0` |
| MuscleParasites | Foodborne, Environmental (soil) | Storyteller, Environmental, Arrival, Acausal | 5.0 d | 20 d | Human + Animal | Vanilla Core hediff (`Disease_MuscleParasites` incident); `corpseContagious`; `showsSickSignal`; no vomiting; soil-biased outdoor exposure; humans use `humanExposureFactor 0.45`; Mode 2 uses environmental + arrival; scaled active-case cap offset 0; `spreadSuppressionScale 0` |
| Malaria | Environmental | Environmental, Acausal | 2.0 d | none* | Human | Mosquito pressure; broad warm/wet source; partial indoor penetration; `spreadSuppressionScale 0`, seasonal |
| SleepingSickness | Environmental | Environmental, Acausal | 2.5 d | none* | Human | Tsetse habitat pressure; hotter, wetter, rarer, and more strongly blocked by deep indoor shelter; `spreadSuppressionScale 0` |

\* Diseases with a vanilla immunity race set `immunityDurationDays 0` and rely on vanilla immunity to prevent reinfection. Non-immunizable diseases (gut worms, muscle parasites) use mod-owned temporary immunity.

**Food poisoning is out of scope for v1.** Vanilla food poisoning already works well; making it contagious would change the food-safety loop without clear benefit. `Vector_Foodborne` covers the real case (a sick cook or infected meat contaminating meals). Modders who want contagious food poisoning can add a `TransmissionProfile` to a custom hediff — the system supports it.

---

## Outbreak Notification System

Every disease activation fires a floating message (`NegativeHealthEvent`) and optionally a persistent letter. The letter tier depends on whether an active outbreak is already in progress.

### Three-tier system

| Tier | Letter type | Condition |
|---|---|---|
| Red "first case" letter | `NegativeEvent` (red) | No active outbreak on this map for this disease + track |
| Yellow "cluster case" letter | `NeutralEvent` (yellow) | Active outbreak detected; replaces the previous yellow letter if undismissed |
| Floating message only | — | Letter fires for both tiers; floating message fires unconditionally |

Human and animal tracks are independent. An animal carrying a disease does not suppress the human first-case letter, and vice versa.

Pure animal diseases (`affectsHumans false`) receive no persistent letter on the animal track. They use the separate sick-signal and animal examination path.

### Outbreak lifecycle

An outbreak is **active** while `TicksGame - lastCaseTick ≤ outbreakEndTicks`. `outbreakEndTicks = outbreakEndDays × 60000` (default 3 days).

1. First visible case: red letter fires → `RecordHumanOutbreakCase` (or animal equivalent) sets `lastCaseTick`.
2. Subsequent cases within the window: yellow cluster letter fires. If the previous yellow letter is still on the stack, it is replaced with an updated count. If dismissed, a fresh yellow letter is created.
3. No new cases for `outbreakEndDays` → `PruneStaleOutbreaks` clears the entry → next case triggers a fresh red letter.

The cluster letter is not saved across sessions (Letter references don't survive save/load). On the first post-load cluster case the mod creates a new yellow letter.

### Source attribution

`ContagionSeedSource` is stored on `Hediff_ContagionIncubation` and forwarded to `NotifyDiseaseActivated` when the incubation activates. The first-case red letter body text is selected from a set of translation keys based on this value:

| Source | Letter key suffix |
|---|---|
| `Environmental` | `_Environmental` — "came from the local environment" |
| `Arrival` | `_Arrival` — "arrived with recent visitors" |
| `Foodborne` | `_Foodborne` — "traced to contaminated food" |
| `Cooking` | `_Cooking` — "contracted while preparing contaminated ingredients" |
| `Corpse` | `_Corpse` — "contracted from handling an infected corpse" |
| `CorpseIngestion` | `_CorpseIngestion` — "contracted from eating a diseased corpse" |
| `Contact` / `Storyteller` | *(base key)* — "transmission may already be underway" |
| `Acausal` / `Unknown` / `Developer` | `_Unknown` — "infection vector is unknown" |

### Animal cluster notification toggle

`suppressAnimalClusterNotifications` (default: on) suppresses yellow cluster letters for animals. The red first-case letter and floating messages are unaffected. Human, slave, and prisoner notifications are unaffected by this setting.

---

## Counterplay & Quarantine — No New Systems

The mod adds no quarantine mechanics; existing systems produce quarantine behavior naturally:

- **Medical rest** puts a sick pawn in a bed/room. A dedicated hospital away from living/work areas contains airborne spread by walls; a hospital that doubles as the barracks does not. This drives hospital separation organically.
- **Area restrictions** confine sick pawns — quarantine using existing tools.
- **Work restrictions** stop sick pawns from cooking, preventing foodborne spread.
- **Room layout** matters: walls/closed doors block airborne LOS, so separate bedrooms contain disease better than open barracks.
- **Cleaning** removes contaminated vomit and lowers proximity-vector cleanliness pressure; dedicated cleaners gain value during outbreaks.
- **Masks & respiratory protection** reduce airborne/social/contact transmission for wearer and bystanders via `ToxicEnvironmentResistance`. Outfitting a caregiver or sick pawn with a gas mask is meaningful counterplay using existing gear.
- **Penoxycyline** works exactly as vanilla (malaria, plague, sleeping sickness) through `DiseaseContractChanceFactor` and/or a `Factor_Hediff` entry — no separate hardcoded list.
- **Animal husbandry** matters for food-borne diseases. Animals kept in roofed barns accumulate far less environmental exposure than outdoor grazers. Skilled handlers notice sick animals sooner. Diagnosing infected animals before slaughter prevents contaminated meat from entering the food supply. Assigning a dedicated vet and cooking in a clean kitchen compound the safety margin.
- **Corpse and butchery choices** use vanilla filters and bills. Stockpiles accept infected corpses by default, so disposal/storage remains easy. Butcher bills reject infected corpses by default, so unsafe meat only enters the food chain when the player explicitly opts that bill into infected corpses.

---

## Difficulty And Settings

Standard `ModSettings` page.

### Infectivity difficulty

| Difficulty | Transmission scale |
|---|---:|
| Easy | 0.7× |
| Medium | 1.0× |
| Hard | 1.35× |

### Spread suppression mode

Suppression uses `load = active_cases / scaled_max_cases` for the target track, then applies a clamped smoothstep curve.

| Mode | Start load | Stop load | Floor | Intent |
|---|---:|---:|---:|---|
| Strong | 0.50 | 1.00 | 0.00 | Protective cap for easier play |
| Medium | 0.90 | 1.10 | 0.05 | Default soft cap with slight overshoot |
| Weak | 0.98 | 2.00 | 0.15 | Hard-mode warning, not a hard wall |
| Let 'er rip | disabled | disabled | 1.00 | No suppression |

Scaled max cases are calculated separately for human and animal colony tracks: `floor(population × clamp(30% + 1% per pawn + maxActiveCaseChanceOffset, 0%, 50%))`, minimum 1 for a non-empty affected track.

### Toggles and advanced tuning

| Setting | Range | Default | Effect |
|---|---|---|---|
| Seeding Mode | Storyteller / Contagion | Storyteller | Storyteller mode (Mode 1) intercepts storyteller picks into pending events; Contagion mode (Mode 2) cancels storyteller disease and runs continuous low-rate seeding with the disease director |
| Masks reduce spread | on/off | on | Apparel + air-filtering body parts reduce respiratory transmission |
| Diagnostics | Off/Summary/Verbose/Developer | Off | Summary/Verbose keep the current in-settings counters and traces; Developer adds dev-only runtime helpers (director forcing, pawn seeding gizmos, hover chance readouts) on top |

Per-disease behavior (`maxActiveCaseChanceOffset`, `useScaledActiveCaseCap`, `spreadSuppressionScale`, per-vector mask effectiveness) and the gene airway-immunity whitelist live in XML for player/modder patching.

---

## Developer Diagnostics (In Progress)

The existing diagnostics counters remain the lightweight default. A separate **Developer** diagnostics mode extends them with interactive tools for debugging contagion behavior during live playtesting.

### Gating and persistence

- The mode is **developer-only**. It is meant for testers running RimWorld with `Prefs.DevMode` on, not for normal players.
- If the enum changes, it must be **append-only**. `ContagionDiagnosticsMode` is persisted by ordinal value, so `Developer` is added at the end rather than replacing or reordering existing values.
- Interactive diagnostic state is **runtime-only**. No queued test action, selected disease override, hovered-target cache, or temporary UI selection should be scribed into saves.
- Runtime ownership should still be map-scoped, not static. A Contagion map component remains the save/load and public façade boundary, while delegating developer controls, runtime-only one-shot commands, and transmission processors to internal helpers so those concerns do not collapse back into one monolithic class.

### Mode 2 director forcing

When diagnostics mode is **Developer**, the mod settings window exposes extra runtime controls. The disease-director control lives there, not in a separate inspect tab.

- The settings page can show a runtime-only "force next arrival disease" control when all three conditions are true: Developer diagnostics are active, Seeding Mode is **Contagion** (Mode 2), and `Find.CurrentMap` exists.
- Outside a live map, the control should be hidden or disabled with a plain reason string. This is a map-scoped test action and should not pretend to work from the main menu or world map.
- Choosing a disease does **not** seed a pawn directly. It arms a non-persistent map-scoped override: the **next qualifying arrival group** is evaluated as that disease instead of going through normal weighted disease selection.
- The real arrival pipeline still runs: eligibility filters, scaled active-case caps, carrier-payload sizing, species gating, and director bookkeeping all remain in place. This is a forced **attempt**, not a guaranteed seed, which makes it suitable for testing actual arrival mechanics.
- The override is consumed after the first qualifying arrival attempt, whether the attempt succeeds or fails. This keeps the tool legible and avoids hidden sticky debug state.
- The settings action should be paired with a visible "armed override" summary and a clear/cancel button so the tester always knows whether a forced disease is pending.

### Pawn incubation gizmo

Every selected pawn gets a Contagion developer gizmo while Developer diagnostics are active.

- The gizmo lives on the normal pawn gizmo surface rather than a `ThingComp`, so it introduces no new persistent pawn state and no mod-removal risk.
- Right-click opens a disease list built from `DiseaseProfileCache`; choosing a disease uses the existing `TrySeedIncubation(...)` path so all normal immunity, duplicate-incubation, and species checks still apply.
- The icon can switch when the pawn currently harbors `Hediff_ContagionIncubation`, making hidden incubation visible to testers without using the health debug UI.
- Because the repo currently has no custom texture pipeline, the first implementation should either use a vanilla icon or add one small cached command texture explicitly for this tool.
- The same gizmo surface is also the right place for a local "clear contagion traces" action when the selected pawn is the source or target of stored debug trace lines.

### Hover spread readout

When a contagious pawn is selected and the cursor is over another pawn, the developer tools add a spread readout to the normal mouseover panel.

- This should be a `MouseoverReadout.MouseoverReadoutOnGUI()` postfix so it extends the existing bottom-left readout instead of creating a separate debug window.
- The numbers should come from the same helper stack the simulation uses now (`GetSourceInfectivity`, `GetTargetEligibilityFactor`, `BuildSourceTargetChance`), not from a parallel approximation.
- The readout should be **per disease and per vector**, not one synthetic aggregate. Airborne and proximity are true current-tick chances; social should either be shown separately as an **on interaction** chance or omitted from the hover readout to avoid implying it rolls every tick.
- The tooltip should include the major factors explicitly: base chance, source infectivity, seasonal multiplier, target eligibility, distance/context multiplier, mask factor, cleanliness or enclosure factor where relevant, and spread suppression when applicable.
- A short world-space line between the selected source pawn and the hovered target pawn should accompany the tooltip while the mouseover readout is active. `GenDraw.DrawLineBetween(...)`, as used in LOS-Check's CE diagnostic overlay, is the right fit for this rather than a custom mesh system.
- This UI runs every repaint, so the calculation must stay narrowly scoped to the selected source pawn and the hovered target pawn. Dev-only gating keeps the cost low, but the implementation should still avoid broad map scans beyond the disease-specific suppression factor already required for the displayed pair.

### Nominal spread overlay

When a contagious pawn is selected, Developer diagnostics also draw a local range overlay around that pawn showing **nominal** distance-based spread chance.

- This is a selection overlay, not a permanent map overlay. A postfix on `Pawn.DrawExtraSelectionOverlays()` is the clean vanilla hook.
- The overlay should use the same instanced-cell rendering pattern as Lookouts and LOS-Check: batch `MeshPool.plane10` quads at `AltitudeLayer.MetaOverlays` with a small set of color bands rather than drawing a unique material per cell.
- "Nominal" means the overlay is target-agnostic: it assumes a typical susceptible pawn rather than the exact pawn standing in each cell. The intended reading is falloff and geometry, not the full contract chance for a specific pawn.
- The overlay still benefits from current source-side state such as infectivity stage and seasonal multiplier, but it should not depend on per-target immunity, apparel, or trait modifiers.
- Because social transmission is event-driven and fomite/foodborne are not radial space vectors, the nominal overlay should be limited to the vectors that have a meaningful spatial falloff surface, chiefly airborne and proximity.

### Infection trace lines

When an actual transmission succeeds, Developer diagnostics retain a short-lived trace entry so the tester can see **who infected whom**.

- Each trace entry should record source pawn, target pawn, disease, vector kind, and tick. This is runtime-only map state and should not be saved.
- The visual is a world-space line between source and target, ideally with a simple direction marker or source/target cap so direction is obvious. LOS-Check's line drawing gives the base pattern; a small plane marker near the target end is enough for directionality.
- The trace system needs bounded lifetime and bounded count. A short time-to-live plus a capped ring buffer prevents diagnostic clutter from becoming permanent map noise.
- Clearing should be available in two places: a global button in the Developer diagnostics section of mod settings, and a selected-pawn gizmo action that clears traces involving that pawn.

**Food-chain node graph.** Beyond pawn↔pawn lines, the trace graph chains contamination through the *workstations* food passes over, so the path reads logically:

- **Butchery:** `corpse → butcher table → meat`. The corpse contaminates the bench (job `TargetA`); each meat product links from the bench node.
- **Cooking:** routed through the bench via `ContagionTrace.RouteThroughBench`, giving `… meat → stove → meal` (ingredient path) or `cook → stove → meal` (when the cook is the vector). The meal links from the durable *stove* node rather than the meat node, which is consumed mid-recipe — this also avoids a dangling edge when the meat node is spliced out.
- **Eating:** `meal → eater`; once the meal is consumed its (Item) node splices out, reconnecting `stove → eater`.

Consumed/destroyed food (`Item`) nodes splice out of the chain (predecessor → successor reconnect, carrying the downstream vector), while benches/stoves/corpses/pawns persist. Benches are inert nodes kept alive only while reachable from an active carrier (a contaminated stack, an infectious corpse, or an infected pawn). A split-off stack inherits the source stack's *upstream* origin (e.g. the butcher table) rather than pointing at the source stack, so partial hauls don't produce confusing meat→meat links. Net effect for a typical run: `corpse → butcher table → stove → eater`, with the transient meat and meal stacks splicing out as they're consumed.

---

## Scope Boundary For First Implementation

The first implementation is **map-scoped:** colony maps are fully supported; visitors and other spawned pawns are valid sources/targets; caravans keep vanilla disease behavior. Vanilla storyteller disease still targets caravans, and the map-only transmission engine deliberately does not extend there. Caravan contagion is deferred until a world/caravan transmission model exists.

---

## Extensibility — Modder Guide

### Minimal example (XML only)

```xml
<li Class="Contagion.TransmissionProfile">
  <incubationDays>2</incubationDays>
  <immunityDurationDays>10</immunityDurationDays>
  <vectors>
    <li Class="Contagion.Vector_Airborne">
      <baseChancePerCheck>0.03</baseChancePerCheck>
    </li>
  </vectors>
  <seeders>
    <li Class="Contagion.Seeder_Arrival">
      <arrivalChance>0.01</arrivalChance>
    </li>
  </seeders>
</li>
```

Eight lines, sensible defaults (default active bell curve, no incubation infectivity), makes any hediff contagious.

### Making an existing hediff contagious

Use the flat two-step `PatchOperationAdd` pattern (add `<modExtensions/>` if absent, then add the profile `<li>`) — never `PatchOperationSequence`/`Conditional` against a `<comps>`/`<modExtensions>` node that may not exist. See `Contagion_Profiles.xml` for the canonical form.

### Adding a new vector type (requires C#)

Subclass `TransmissionVector` (or `RespiratoryVector` for airway-based spread). The engine handles immunity/duplicate/suppression checks centrally; the vector supplies its own context modifiers. New susceptibility/source factor types subclass `SusceptibilityFactor` / `SourceInfectivityFactor`.

---

## Sister Mod Architecture (Pathology)

Contagion answers "how does a pawn get sick?" A planned sister mod (working title **Pathology**) would answer "what happens after they're sick?" — progression, treatment, symptoms, complications. Both attach independent `DefModExtension`s to the same `HediffDef`; either works alone.

| Concern | Owner |
|---|---|
| Transmission vectors & seeding | Contagion |
| Infectivity curves (source contagiousness) | Contagion |
| Susceptibility / source-infectivity factors | Contagion |
| Post-recovery immunity (simple timer) | Contagion |
| Post-recovery immunity (complex/waning) | Sister mod, via `immunityHediffDef` override |
| Carrier state | Contagion (hediff-removal hook) |
| Severity progression / treatment / symptoms / comorbidity | Sister mod |

**Soft coupling via hediffs, no compile-time dependency:** the sister mod applies a hediff (e.g. `SymptomSuppressant`); Contagion's `sourceInfectivityFactors` references it by XML. If the sister mod isn't installed, the hediff never appears and the entry is inert. The pattern works both ways. For the sister mod to extend Contagion, `TransmissionProfile` and contained types stay `public`, `DiseaseProfileCache` is accessible for lookups, and the map component exposes active-case counts.

---

## Implementation Status & Known Gaps

*As of 2026-05-30. All vectors, seeding orchestration, arrival hooks, infected-corpse food-chain rules, and the animal disease chain are complete and stable; the build is clean (0 warnings, 0 errors).*

**Implemented and stable:** all six active vectors (airborne, social, proximity, environmental, fomite, foodborne); incubation + temporary/custom immunity with a recovery hook; spread suppression; respiratory/mask protection with the gene whitelist; difficulty presets, sliders, and diagnostics; map-component save state (contaminated vomit, seeder cooldowns, pending events, director state) and contaminated-meal/raw-meat/corpse comp state; arrival hooks for neutral groups, wanderer joins, quest arrivals, hostile raids, and farm-animal wander-ins; the storyteller intercept patches (`IncidentWorker_Disease.ApplyToPawns` + `TryExecuteWorker`); Mode 1 and Mode 2 seeding orchestration; environmental windows for gut worms and muscle parasites even though those profiles also have Mode 2 arrival seeders; `selfSchedules` auto-incident generation; the full animal disease chain — `corpseContagious` death hook (`Comp_InfectedCorpse` on fresh animal and humanlike corpses), `showsSickSignal` detection via `AnimalChat` interaction (Animals skill roll), `Contagion_AnimalSick` hediff, hidden active animal disease display until diagnosis, diagnosis via `TendUtility.DoTend` (Medical skill roll, true/false positive/negative outcomes), auto-slaughter exclusion for sick animals, infected/uninfected corpse filters, butcher bills excluding infected corpses by default with old-save migration, human auto-food rejection with manual ingest warning, corpse ingestion exposure, infected corpse inspect text and green corpse tint, `Patch_Corpse_ButcherProducts` (Animals/Medicine + Cooking notice check, contaminated raw meat), `Patch_GenRecipe_MakeRecipeProducts` (ingredient contamination with cooking reduction factors), `CookingContaminationExtension` on RecipeDefs, `Comp_ContaminatedFood` extended to raw meat and kibble with timestamp + expiry; gut worms redesigned to water-environmental + affectsAnimals + fomite + foodborne; muscle parasites (vanilla Core `MuscleParasites`, patched with `TransmissionProfile`) given soil-environmental + foodborne chain, `corpseContagious`, `showsSickSignal`.

**Pending — tuning pass.** Starting numbers for pending windows, director parameters, group arrival exposure policies, plague `crossSpeciesTransmissionFactor`, and the new environmental/butchering disease parameters are first-pass guesses. Play-testing and adjustment are needed before v1 ships.

**Reserved — see below.** `Vector_Lovin` is intentionally schema-only with no engine implementation in v1. `corpseInfectivityDecayPerDay` (corpses as active proximity/fomite sources with decay) is also reserved — `corpseContagious` currently marks corpses for filtering, butchery, visuals, and ingestion exposure, not passive proximity/fomite emission.

---

## Reserved & Future Work

- **Corpse as active contagion source** (`corpseInfectivityDecayPerDay`) — `corpseContagious` already marks animal and humanlike corpses with `Comp_InfectedCorpse` for filters, visuals, butchery, and ingestion exposure. The next step — corpses acting as proximity/fomite sources while they decay — is still reserved. This would extend the existing corpse comp with decay-aware passive emission and cleanup hooks for cremation/burial.
- **Carrier state** — Typhoid-Mary dynamics: a chance on recovery to become an asymptomatic contagious carrier with its own (flat, low) infectivity curve. Removed from the profile schema until it has an engine implementation.
- **Caravan spread** — requires a world/caravan transmission model, deliberately deferred. Removed from the profile schema until that model exists.
- **`Vector_Lovin`** — STD-style transmission; needs a `JobDriver_Lovin` completion hook.
- *(Unified plague — completed. `Plague` now owns both species via `animalVariantDef Animal_Plague`. Cross-species transmission factor 0.5. Corpse contagious. Sick signal enabled.)*
- **Future vector types** (need their own design pass): `Vector_Combat`/`Vector_MeleeDamage` (bites/melee — rage viruses, scaria, zombies) and `Vector_Pregnancy` (mother-to-child, Biotech).
- **Transmission directionality** — the C# vector API should accept the profile/role so future asymmetric concepts (animal reservoirs, environmental-only sources) are expressible without symmetric source/target assumptions.

---

## Decisions Log

| Decision | Rationale |
|---|---|
| Distance + LOS + roofing, not room identity | Hallways are large "rooms" but shouldn't transmit across their length; open doorways are air paths, closed doors are barriers |
| Two infectivity curves, not a binary contagious window | Models pre-symptomatic, peak, and tapering contagiousness; modder-tunable |
| Seasonal weights via `SeasonUtility`, not a calendar curve | Handles hemisphere inversion and equatorial/polar tiles for free |
| Social = boosted respiratory, separate vector class | Enables composition (social-only diseases) without a second social system |
| Fomites scoped to vomit only | Visible and cleanable; no invisible contamination |
| No cross-species transmission by default (float, not bool) | Keeps animal husbandry from being punishing; float is strictly more expressive |
| Unified plague: one profile, two hediffs | `Plague` (12 h tend) for humans, `Animal_Plague` (48 h tend) for animals — same transmission cluster, species-tuned treatment curves. `animalVariantDef` on the primary profile drives hediff selection at application time; `DiseaseProfileCache` indexes the variant def back to the primary profile so carriers of either hediff are correctly identified. |
| Animal plague remains hidden until diagnosis | `Animal_Plague` is the real active animal disease, but for `showsSickSignal` profiles it stays hidden like incubation until a successful vet diagnosis reveals it. Players see the sick signal first rather than a free health-tab diagnosis. |
| `crossSpeciesTransmissionFactor 0.5` for plague | Plague crosses species fairly readily (flea vector doesn't care about species), but with a 50% barrier. First-pass value; needs tuning. |
| Penoxycyline via `DiseaseContractChanceFactor` / `Factor_Hediff` | Stays vanilla-compatible; no hardcoded list |
| Susceptibility/source factors as polymorphic XML lists | New modifiers need no C#; enables the sister-mod soft API |
| Suppression target-gated to colony pawns | A fully-infected colony must not throttle unrelated visitors/raiders or arriving non-colony carriers |
| Contagious food poisoning cut from v1 | Vanilla food safety already works; changing it adds churn without benefit |
| Reserved fields shipped in schema | Modders can plan for corpse/carrier/caravan without waiting on the engine |
| Two seeding modes (storyteller-driven default, Contagion-driven opt-in) | Vanilla cadence preserved as default; opt-in continuous pressure for sim-leaning players; mode toggle keeps the mental model clear per player |
| Mode 1: pending events with per-disease fulfillment chains | Replaces four parallel independent seeders with one scheduler + ranked strategies — clearer mental model, unified semantics, and the strategy/window can be tuned per disease |
| Mode 1 plague window 5 days | Tight windows preserve storyteller event-spacing — a long pending window would let a disease event collide with raids the storyteller deliberately spaced apart |
| Gut worms and muscle parasites use Seeder_Environmental as primary Mode 1 trigger | Storyteller pick opens a water/soil environmental window rather than seeding a carrier directly. Mode 2 can also seed them through arrivals, especially farm-animal wander-ins. Acausal seeder is retained only as the Mode 1 expiry fallback |
| Malaria and sleeping sickness keep acausal only as a Mode 1 environmental expiry fallback | Storyteller mode is allowed to say "someone gets sick somehow" if the environmental window fails to spend its budget. Mode 2 still has no acausal backstop, so environmental prevention stands. |
| Mode 1 arrival fulfillment = next eligible group (deterministic exposure) | Avoids unbounded pending-event growth on low-traffic maps while allowing large groups to carry a capped, sublinear payload |
| Mode 1 environmental: time-bounded window with infection budget | Event-scoped budget is distinct from colony-wide scaled active-case caps — matches vanilla's "outbreak happens then ends" feel rather than turning environmental disease into a permanent biome hazard |
| Mode 2: storyteller incidents cancelled for profiled diseases | Mode 2 owns pacing; letting the storyteller inject extra events would undermine the director cadence the player is learning to read |
| Mode 2: map-level disease director | Quiet periods raise pressure, active sickness and recent successful seeding suppress new introductions. Good quarantine still buys breathing room because an attempted threat counts even if colonists avoid infection |
| Group arrival exposure | Per-pawn chance on large groups would saturate arrivals with disease. Exposure is incident-level, carrier count is group-size-aware but capped, and director pressure is spent once per exposed group |
| Developer diagnostics are UI-only and non-persistent | Debug helpers must not add save-state churn or mod-removal hazards; runtime-only overrides and caches are sufficient |
| Mode 2 arrival testing uses a one-shot forced disease override | Lets testers exercise the real arrival pipeline without adding a parallel fake seeding path or permanently mutating the director |
| Hover diagnostics show vector breakdown, not one aggregate spread percent | Airborne/proximity are current-tick rolls; social is event-driven. A single combined percentage would be misleading |
| Hover diagnostics explain zero target eligibility | When target susceptibility/eligibility reaches 0, the developer tooltip reports the block reason, such as existing disease, incubation, temporary immunity, vanilla immunity, a species barrier, or a susceptibility factor. |
| Developer controls live in mod settings, not a separate map tab | The user explicitly wants the mode toggle and director action in the in-game settings surface; map-specific actions there must disable cleanly when no current map exists |
| The nominal spread overlay is target-agnostic by design | Its job is to visualize distance and geometry around a contagious source, not to predict exact chance for every possible pawn |
| Successful transmissions leave bounded runtime trace lines | This gives post-event attribution without introducing persistent forensic state into saves |
| Infected corpses stay fresh with `Comp_InfectedCorpse` | Rotting corpses create noxious/filth behavior and are outside normal food behavior. A saved corpse marker keeps the corpse legible and filterable while stockpiles, butcher bills, predators, and manual ingest commands each get their own appropriate safety rule |
| Stockpiles allow infected corpses by default; butcher bills do not | Storage should be permissive so disposal and sorting remain easy. Food production should be conservative so contaminated meat only enters the food chain when a specific bill explicitly opts in |
| Human auto-food search avoids infected corpses, manual ingest remains | Prevents accidental cannibal/desperation ingestion while preserving RimWorld's direct player command surface for unusual emergencies |
| Sick signal detection tied to AnimalChat interaction | Fires naturally during handler routines (training, feeding, tending) without a new dedicated job or polling system. Animals that are never handled may never show the signal — rewarding attentive husbandry |
| Diagnosis is a skill roll on tending, not a separate inspection job | Reuses the existing vet-tending loop. False negatives feel like skill-based variance rather than arbitrary RNG because the cause (low medicine skill) is visible and fixable |
| False positives on healthy animals at 3% per interaction | Makes the sick signal a diagnostic signal rather than a certainty. Players learn to diagnose rather than immediately slaughter any animal flagged "sick." False positive rate is low enough that it's noise, not harassment |
| Cooking factor 0 for nutrient paste via dispenser exclusion | The nutrient paste dispenser does not call `MakeRecipeProducts`, so it bypasses the contamination chain automatically. No special-case code needed; the design is self-enforcing |
| Muscle parasites separate from gut worms, no vomiting | Different disease character (muscle weakness vs gut disruption), different narrative origin (meat vs water), different player-facing spread path. Combining them would collapse a meaningful distinction |
| Muscle parasites uses the vanilla Disease_MuscleParasites incident | Vanilla Core already defines this incident; no selfSchedules or hand-authored incident needed. Contagion intercepts the storyteller pick as it would for any other profiled disease and opens an environmental window |

---

## Success Criteria

The design succeeds if:

- outbreaks usually begin from a readable source
- players can contain disease with normal RimWorld tools
- the real disease behavior still feels vanilla once symptoms begin
- future diseases can be made contagious mostly through XML
- the implementation avoids broad, conflict-heavy Harmony cancellations
