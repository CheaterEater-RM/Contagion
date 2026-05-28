# Contagion — Design

*Single source of truth for the Contagion mod's design. Last consolidated 2026-05-27 from the former `DESIGN.md`, `CONTAGION_SCHEMA_REDESIGN.md`, and `INFECTION_DESIGN.md`.*

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
5. **Temporarily immune** — a hidden immunity source (`Hediff_ContagionTemporaryImmunity`, or a profile-specified custom hediff) prevents immediate re-seeding for `immunityDurationDays`.

The key rule: Contagion adds only phase 1 (incubation wrapper) and phase 5 (immunity tracking). It does not create a parallel disease simulation once symptoms begin.

### Incubation notes

`Hediff_ContagionIncubation` uses severity to track progress (0.01 → 1.0 over the incubation duration). The `incubationInfectivityCurve` evaluates against this severity. Because the hediff is hidden (`Visible => false`), the severity-as-progress approach is invisible to the player. If incubation visibility is added later ("??? disease" at low medical skill), a separate internal progress field would be needed so severity display doesn't read as "how sick they are." Not needed for v1.

### Temporary post-recovery immunity

The mod owns short-term reinfection protection because vanilla `HediffComp_Immunizable` does not cover non-immunizable diseases (gut worms, food poisoning). On recovery, a hidden temporary immunity hediff is applied for `immunityDurationDays`. If a profile sets `immunityHediffDef`, that custom hediff is applied instead and manages its own duration (sister-mod hook).

---

## How Outbreaks Begin (Seeding)

**Storyteller seeding.** Vanilla storyteller disease selection stays useful as a biome-aware *source selector*, but for contagious diseases it seeds one carrier / a few incubation cases instead of an immediate colony-wide outbreak.

**Visitor and arrival seeding.** Visitors, travelers, and trade caravans can arrive in incubation, introducing respiratory disease without forcing the player to inspect arrivals.

**Environmental seeding.** Malaria and sleeping sickness come from biome, temperature, water, and exposure. The map is the source; a pawn is infected because they were exposed in a risky environment.

**Food system seeding.** Gut worms arise from contaminated meals — sick cooks and dirty kitchens are the cause.

**Animal-linked seeding.** Plague feels associated with animals without general animal-to-human contagion. Animal presence authorizes/biases the first human seed event; after that, human-to-human spread uses normal transmission rules. (This is "Option C" from the original design — seeding gated by animal presence, narratively framed as animal contact, with no ongoing cross-species transmission system.)

### Arrival Seeding Coverage

`Seeder_Arrival` runs when an applicable pawn appears on the map through one of the vanilla "guest arrives" paths. The table below catalogs every vanilla way a humanlike (or animal) pawn can arrive on a colony map and where Contagion does or doesn't hook in. "Spawned at hook?" matters because `SeedArrivals` only acts on pawns that are already `Spawned` with a `Map`.

| # | Arrival type | Vanilla path | Hook point | Spawned at hook? | Ease | Status / fit |
|---|---|---|---|---|---|---|
| 1 | Visitors, travelers, trade caravans, skylantern wanderers, tribute collectors | `IncidentWorker_NeutralGroup.SpawnPawns` | returns `List<Pawn>` | yes | Easy | Covered |
| 2 | Wanderer joins | `IncidentWorker_WandererJoin.SpawnJoiner(Map, Pawn)` | `Pawn` param | yes | Easy | Covered |
| 3 | Quest arrivals: refugees, lodgers, shuttle allies, returning lent pawns, reward joiners | `QuestPart_PawnsArrive.Notify_QuestSignalReceived` | public `pawns` field | walk-in yes / drop-pod no | Medium | Covered (walk-in); pod-mode skipped by design |
| 4 | Wild man wanders in | `IncidentWorker_WildManWandersIn.TryExecuteWorker` | inline spawn, no param | yes (needs discovery) | Medium-hard | Skipped (feral, low value) |
| 5 | Friendly raid (combat allies) | `IncidentWorker_Raid.PostProcessSpawnedPawns` | shared w/ enemy raids | yes | Medium | Skipped (transient) |
| 6 | Game-ended wanderers join | `IncidentWorker_GameEndedWanderersJoin` | `startingAndOptionalPawns` | yes | Easy | Skipped (endgame) |
| 7 | Creep joiner (Anomaly) | quest / `BaseCreepJoinerWorker` | quest path | varies | Hard | Skipped (anomalous entity) |
| 8 | Farm animals wander in | `IncidentWorker_FarmAnimalsWanderIn.SpawnAnimal` | private method | yes | Medium | Skipped — would require an animal-side `Seeder_Arrival`; none shipped |
| 9 | Enemy raids, sieges, mech clusters, infestations | raid/threat workers | — | yes | — | Out of scope (hostile; still transmit if already sick) |
| 10 | Orbital traders | `IncidentWorker_OrbitalTraderArrival` | — | no map pawns | N/A | N/A |
| 11 | Caravan meetings / world incidents | world-scope | — | no | N/A | Deferred (caravan scope) |

Refugees deserve a footnote: in RimWorld 1.6 there is no `IncidentWorker_RefugeeChased` / `RefugeePodCrash`. Refugees (and most other named guests) now arrive through the **quest system** via `QuestPart_PawnsArrive` — covered above. The drop-pod-mode exception is intentional: pod-mode quest arrivals are still inside an incoming drop pod when the quest signal fires, so they're not `Spawned` yet and `SeedArrivals` correctly skips them. The walk-in case (the common one) is fully covered.

The hooks themselves apply no faction or species filter — disease profiles gate that via `CanAffect`. In practice, of the shipped diseases only Flu has a `Seeder_Arrival`, so the new hooks are inert for the others by design. Adding a `Seeder_Arrival` to animal-disease profiles (and patching row 8) would be the natural extension.

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
| `maxActiveCases` | int | 0 (= no limit) | Suppress all seeding at/above this active+incubating count |
| `spreadSuppressionScale` | float | 1.0 | Per-disease scaling of colony spread suppression (0 = exempt) |
| `outbreakNotification` | enum | FirstCase | Player notification mode: `None` / `FirstCase` / `EveryCase` |
| `corpseContagious` | bool | false | *Reserved:* dead pawns as contagion sources |
| `corpseInfectivityDecayPerDay` | float | 0.5 | *Reserved:* daily decay of corpse infectivity |
| `carrierChance` | float | 0.0 | *Reserved:* probability of becoming an asymptomatic carrier on recovery |
| `carrierHediffDef` | HediffDef | null | *Reserved:* hediff applied to carriers |
| `spreadsDuringCaravan` | bool | false | *Reserved* for future caravan support |

`Reserved` fields are present in the schema (so modders can plan) but have **no engine implementation in v1.** See [Reserved & Future Work](#reserved--future-work).

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

**Seeders respect susceptibility factors** when selecting targets (a Robust pawn is less likely to be patient zero; penoxycyline at 0.0 blocks seeding). `maxActiveCases` is checked *before* factor evaluation — it's a population gate, not a per-pawn check.

---

## Source Infectivity Factors

Structurally identical to susceptibility factors but evaluated against the contagious source pawn — a cough suppressant or face covering makes a pawn shed less. Multiplicative; omitted = no source-side modifiers. Initial types: `SourceFactor_Hediff`, `SourceFactor_Gene`, `SourceFactor_Stat`.

This is the primary soft-coupling hook for the planned sister mod: it applies a hediff (e.g. `SymptomSuppressant`), and Contagion's profile references it via XML. If the sister mod isn't installed, the hediff never appears and the factor entry is inert.

---

## Pawn-Local Context Model (LOS + Roofing, not Rooms)

Airborne, social, and proximity vectors do **not** use room identity as a binary gate (a 50-cell hallway is one "room" but shouldn't transmit across its length; two pawns across an open doorway are in different rooms but 2 cells apart). Instead:

- **Roofing** — `map.roofGrid.Roofed(cell)` at both endpoints. Both roofed = enclosed, aerosols concentrate (factor 1.0). Either unroofed = outdoor dispersal (`outdoorFactor`). Per-cell, so roofed courtyards and mountain bases behave correctly.
- **Line of sight** — `GenSight.LineOfSight(source, target, map)`. Clear = air path; blocked by wall/closed door = `obstructedFactor` (airborne default 0.0). Open doors pass LOS (correct: an open doorway is an air path). *Known v1 limitation:* vents are `Impassable` to LOS, so airborne does not pass through them — physically wrong but conservative.
- **Distance falloff** — smooth exponential, not a hard cutoff, out to `maxRange`.

Rooms still apply where they're the correct abstraction: **kitchen cleanliness** for foodborne, and **indoor cleanliness** for proximity (with an outdoor filth-count fallback within `outdoorFilthRadius`).

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

Respiratory vectors (`Vector_Airborne`, `Vector_Social`, `Vector_Proximity`) share a `RespiratoryVector` base that reduces transmission based on protection the source and target are actually wearing or carrying, keyed on the vanilla `ToxicEnvironmentResistance` stat so existing gear (gas masks etc.) is immediately relevant. Per side:

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

Composable classes. The composition *is* the API: "airborne + social + fomite" is flu, "proximity only" is plague, "social only" is a contact disease a modder could build.

### Vector_Airborne (respiratory)
Primary respiratory vector. Distance falloff, roofing-based enclosure, LOS obstruction.
- `baseChancePerCheck` (0.03), `outdoorFactor` (0.15), `maxRange` (15), `distanceFalloffRate` (0.25), `obstructedFactor` (0.0)

### Vector_Social (respiratory)
A booster on respiratory spread that fires on successful social interactions (face-to-face, so no distance falloff). Hooked via a postfix on `Pawn_InteractionsTracker.TryInteractWith`, evaluated in both directions.
- `baseChancePerInteraction` (0.02), `outdoorFactor` (0.5)

### Vector_Proximity (respiratory)
Short-range contact spread modulated by cleanliness — the generalized "flea" vector for plague. Uses indoor room cleanliness or an outdoor filth-count fallback. `airwayImmunityFactor` is typically set to 0 for plague (contact, not airway).
- `baseChancePerCheck` (0.025), `maxRange` (6), `distanceFalloffRate` (0.35), `cleanlinessImpact` (1.0), `outdoorFactor` (0.75), `outdoorFilthRadius` (4)

### Vector_Environmental
Ambient exposure (mosquito model) for malaria/sleeping sickness — seeded by the map, no person-to-person spread. Temperature factor (zero below `minTemperature`, peaks at `peakTemperature`), water-proximity factor, indoor shelter falloff by depth from the nearest unroofed cell, and an AC/cool-room reduction. Biome-gated via vanilla `BiomeDef.CommonalityOfDisease`.
- `baseChancePerCheck`, `minTemperature` (15), `peakTemperature` (30), `waterProximityRadius` (10), `waterProximityWeight` (0.02), `indoorReductionPerCellFromEdge` (0.1), `coolRoomThreshold` (18)

### Vector_Fomite
Contaminated **vomit** filth (scoped to vomit so contamination is visible and cleanable). When a contagious pawn vomits, the resulting `Filth_Vomit` is tagged; pawns stepping on it roll for transmission, with potency decaying over time. Cleaning removes it. Activates mainly during severe, un-quarantined cases — a "you let this get out of hand" escalation.
- `contaminatesVomit` (true), `baseChancePerContact` (0.03), `potencyDecayPerHour` (0.1)

### Vector_Foodborne
Transmission through prepared meals (gut worms; a flu-infected cook contaminating food). A `ThingComp` on meals captures the cooking pawn at production; ingestion rolls for transmission. Kitchen cleanliness modifies the chance. Not subject to spread suppression.
- `baseChancePerMeal` (0.08), `cleanlinessImpact` (1.0)

### Vector_Lovin
*Reserved extensibility hook.* Not used by any shipped disease and **not currently wired to a job hook.** Present so future STD-style mods can use the framework.
- `baseChancePerAct` (0.15)

---

## Seeders

Shared base fields: `cooldownDays` (minimum gap between seed events) and an optional per-seeder `maxActiveCases` override (profile-level field is the default). When at/above the active-case limit, all seeders for that profile are suppressed — preventing the storyteller from piling infections onto a struggling colony.

| Seeder | Purpose | Key fields |
|---|---|---|
| `Seeder_Storyteller` | Converts a vanilla storyteller disease incident into 1–few incubation cases instead of a colony-wide outbreak. | `seedCountRange` (1~1) |
| `Seeder_Arrival` | Incoming pawns arrive in incubation. | `arrivalChance` (0.01) |
| `Seeder_Environmental` | The map is the continuous source (biome/temperature/season). | `baseChanceMultiplier` (1.0) |
| `Seeder_AnimalLinked` | Seeds humans when animals are present (plague), biased toward handlers; ongoing spread is then human→human. | `mtbDays` (120), `requiresAnimalsOnMap` (true), `handlerBias` (2.0) |
| `Seeder_Acausal` | MTB fallback for isolated colonies (also primary for gut worms). | `mtbDays` (90) |

---

## Shipped Disease Configurations

All seven profiles are patched onto vanilla hediffs in `1.6/Patches/Contagion_Profiles.xml` using the flat two-step `PatchOperationAdd` pattern.

| Disease | Vectors | Seeders | Incubation | Immunity | Species | Notes |
|---|---|---|---|---|---|---|
| Flu | Airborne, Social, Fomite (vomit) | Storyteller, Arrival | 1.5 d | none* | Human | Seasonal (winter-peaking); `maxActiveCases` 5 |
| Animal_Flu | Airborne, Fomite | Storyteller | 1.5 d | none* | Animal | Species-isolated |
| Plague | Proximity (cleanliness) | Storyteller, AnimalLinked | 1.0 d | none* | Human | `airwayImmunityFactor` 0; `maxActiveCases` 4 |
| Animal_Plague | Proximity | Storyteller | 1.0 d | none* | Animal | Species-isolated |
| GutWorms | Foodborne | Storyteller, Acausal | 3.0 d | 15 d | Human | `targetBodyParts: Stomach`; `maxActiveCases` 3 |
| Malaria | Environmental | Environmental | 2.0 d | none* | Human | `outbreakNotification None`, `spreadSuppressionScale 0`, seasonal |
| SleepingSickness | Environmental | Environmental | 2.5 d | none* | Human | Tropical-weighted; `outbreakNotification None`, `spreadSuppressionScale 0` |

\* Diseases that have a vanilla natural-immunity race set `immunityDurationDays 0` and rely on vanilla immunity to prevent reinfection. Only non-immunizable diseases (gut worms) need mod-owned temporary immunity.

**Food poisoning is out of scope for v1.** Vanilla food poisoning already works well as a consequence of dirty kitchens / unskilled cooks; making it contagious would change the food-safety loop without clear benefit. `Vector_Foodborne` covers the real case (a sick cook contaminating meals). Modders who want contagious food poisoning can add a `TransmissionProfile` to a custom hediff — the system supports it.

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

---

## Difficulty And Settings

Standard `ModSettings` page.

### Difficulty preset

| Difficulty | Transmission scale | Spread suppression strength |
|---|---|---|
| Easier | 0.7× | 3.5 (strong — outbreaks rarely reach the whole colony) |
| Normal | 1.0× | 2.0 (moderate — a well-run colony can usually contain it) |
| Harder | 1.35× | 0 (**disabled** — an untreated outbreak can sweep the colony) |

Difficulty *multiplies* the Transmission Rate slider rather than replacing it, so the two layers compose.

### Toggles and advanced tuning

| Setting | Range | Default | Effect |
|---|---|---|---|
| Masks reduce spread | on/off | on | Apparel + air-filtering body parts reduce respiratory transmission |
| Transmission Rate | 0.25×–2.0× | 1.0× | Global multiplier on vector base chances (composed with difficulty) |
| Outbreak Frequency | 0.25×–2.0× | 1.0× | Multiplier on seeder MTB timers |
| Incubation Length | 0.25×–2.0× | 1.0× | Multiplier on incubation durations |
| Diagnostics | Off/Summary/Verbose | Off | In-settings counters and (dev-mode) trace logging; optional performance stats |

Per-disease behavior (`spreadSuppressionScale`, per-vector mask effectiveness) and the gene airway-immunity whitelist live in XML for player/modder patching.

---

## Scope Boundary For First Implementation

The first implementation is **map-scoped:** colony maps are fully supported; visitors and other spawned pawns are valid sources/targets; caravans keep vanilla disease behavior. Vanilla storyteller disease still targets caravans, and the map-only transmission engine deliberately does not extend there. Caravan contagion is deferred, not accidentally half-supported (`spreadsDuringCaravan` is reserved).

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

*As of 2026-05-27. The schema redesign and transmission engine are fully implemented; the build is clean (0 warnings, 0 errors). Tested incrementally in-game during development.*

**Implemented and wired:** all six active vectors (airborne, social, proximity, environmental, fomite, foodborne); all five seeders; storyteller→incubation interception via `IncidentWorker_Disease.ApplyToPawns` + `TryExecuteWorker` (which also covers trait-driven single-pawn disease, since that routes through `ApplyToPawns`); incubation + temporary/custom immunity with a recovery hook; outbreak notifications; spread suppression; respiratory/mask protection with the gene whitelist; difficulty presets, sliders, and diagnostics; map-component save state (contaminated vomit, seeder cooldowns) and contaminated-meal comp state.

**Arrival seeding coverage.** `Seeder_Arrival` is hooked through three patches: `IncidentWorker_NeutralGroup.SpawnPawns` (visitors / travelers / trade caravans / skylantern wanderers / tribute collectors), `IncidentWorker_WandererJoin.SpawnJoiner` (wanderer joins), and `QuestPart_PawnsArrive.Notify_QuestSignalReceived` (quest pawns including walk-in refugees and lodgers). Remaining gaps, all intentional: **drop-pod-mode** quest arrivals (pawns not yet `Spawned` when the signal fires), wild man, friendly raids, and animal arrivals (no animal-side `Seeder_Arrival` ships). See the full chart under [Arrival Seeding Coverage](#arrival-seeding-coverage).

**Reserved — see below.** Corpse contagion, carrier state, caravan spread, and `Vector_Lovin` are intentionally schema-only with no engine implementation in v1.

---

## Reserved & Future Work

- **Corpse contagion** (`corpseContagious`, `corpseInfectivityDecayPerDay`) — corpses of pawns who died with the disease as proximity/fomite sources, decaying daily; cremation/burial removes them. Extensibility hook for plague-pit/zombie diseases. Default off.
- **Carrier state** (`carrierChance`, `carrierHediffDef`) — Typhoid-Mary dynamics: a chance on recovery to become an asymptomatic contagious carrier with its own (flat, low) infectivity curve.
- **Caravan spread** (`spreadsDuringCaravan`) — requires a world/caravan transmission model, deliberately deferred.
- **`Vector_Lovin`** — STD-style transmission; needs a `JobDriver_Lovin` completion hook.
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
| Plague seeds onto humans gated by animal presence | No cross-species system needed; narrative framing handles it |
| Penoxycyline via `DiseaseContractChanceFactor` / `Factor_Hediff` | Stays vanilla-compatible; no hardcoded list |
| Susceptibility/source factors as polymorphic XML lists | New modifiers need no C#; enables the sister-mod soft API |
| Suppression target-gated to player faction | A fully-infected colony must not throttle unrelated visitors/raiders |
| Contagious food poisoning cut from v1 | Vanilla food safety already works; changing it adds churn without benefit |
| Reserved fields shipped in schema | Modders can plan for corpse/carrier/caravan without waiting on the engine |

---

## Success Criteria

The design succeeds if:

- outbreaks usually begin from a readable source
- players can contain disease with normal RimWorld tools
- the real disease behavior still feels vanilla once symptoms begin
- future diseases can be made contagious mostly through XML
- the implementation avoids broad, conflict-heavy Harmony cancellations
