# Plague — Contagion Profile

Flea-borne bacterial disease that crosses freely between humans and animals. One unified disease cluster: `Plague` (human variant, 12 h tend) and `Animal_Plague` (animal variant, 48 h tend) share all transmission logic. Infected animal corpses are dangerous. Handlers are primary initial targets.

---

## Identity

| Field | Value |
|---|---|
| Primary HediffDef | `Plague` |
| Animal variant HediffDef | `Animal_Plague` |
| Species | Human + Animal |
| Vanilla incidents | `Disease_Plague` (human), `Disease_AnimalPlague` (animal) |
| Vanilla lethal severity | 1.0 (both) |
| Human tend cycle | 12 h (`severityPerDayTended −0.3628`) |
| Animal tend cycle | 48 h (`severityPerDayTended −0.4254`) |
| Human immunity | `immunityPerDaySick 0.5224`, `severityPerDayNotImmune 0.666` |
| Animal immunity | `immunityPerDaySick 0.6092`, `severityPerDayNotImmune 0.666` |

### Why two hediffs, one cluster

Animals need a 48 h tend cycle because they rarely receive perfect care. Using the human hediff on animals would force vet attention every 12 h — punishing and unrealistic. The animal variant has the same disease stages and severity progression; only the tending parameters differ. The Contagion engine selects `Animal_Plague` when seeding non-humanlike targets, and `Plague` for humanlike targets, automatically.

---

## Vanilla Disease Characteristics

Plague is serious. Without tending the immunity race is tight: `severityPerDayNotImmune 0.666` against `immunityPerDaySick 0.5224` means severity climbs ~0.14/day even while immune. A pawn hitting the life-threatening stage (0.9+) loses Breathing capacity. Untreated plague kills in roughly 7 days. Tended plague recovers in roughly 5 days.

For animals: the 48 h tend window means a vet must act within 2 days of noticing sickness. Missed tending windows compound quickly. Animals without an attentive vet loop (skilled handler + doctor role) face high mortality.

---

## How It Enters the Colony

### Mode 1 (Storyteller-driven)
Storyteller fires `Disease_Plague` or `Disease_AnimalPlague` → pending event created with a **5-day window** (deliberately tight — see decisions log in DESIGN.md).

**Fulfillment chain:**
1. **Animal-contact (AnimalLinked)** — if any animals are present on the map, the event resolves onto a handler-biased pawn within the window. Near-deterministic on animal-bearing maps.
2. **Arrival** — next qualifying arriving group carries a capped carrier payload (`arrivalChance 0.01`). Fallback for colonies with no animals.
3. **Acausal** — if 5 days pass unfulfilled, silent incubation on a random eligible pawn.

The 5-day window keeps the storyteller's event-spacing meaningful. A long window would let plague collide with raids the storyteller deliberately spaced apart.

### Mode 2 (Contagion-driven)
- **AnimalLinked** — MTB 120 days, `requiresAnimalsOnMap true`, `handlerBias 2.0`. Triggers on a pawn biased toward Animals skill, selecting from player-faction colonists. Animals themselves are seeded via the vanilla `Disease_AnimalPlague` incident (which Contagion does not cancel in Mode 2) or via cross-species transmission from infected colonists.
- **Arrival** — `arrivalChance 0.01` per qualifying group, `cooldownDays 3`.
- **Acausal** — MTB 180 days, `cooldownDays 15`. Backstop for colonies with no animals.
- Storyteller incidents for plague are cancelled in Mode 2; the disease director and these seeders own pacing.

**Storyteller seeder cooldown:** 15 days.

### Animal seeding path

Vanilla `Disease_AnimalPlague` incident is NOT suppressed. It still fires via the storyteller and applies `Animal_Plague` directly to a fraction of one animal species. The Contagion engine then recognises these animals as Plague-cluster carriers and spreads the disease further via proximity. The combined result: vanilla provides the initial wild-animal seed, Contagion handles subsequent spread.

### Arrival seeding

Raiders, caravan members, and wanderers may arrive already carrying `Plague`. This is the main path for plague arriving on maps with no animals (arctic, toxic fallout, etc.).

---

## Spread Vectors

### Vector_Proximity (only vector)

Plague spreads by contact and flea transfer. Not airborne. Airway barriers (breathless gene, gas masks' airway component) have no effect on transmission.

| Parameter | Value | Notes |
|---|---|---|
| baseChancePerCheck | 0.025 | Per 250-tick pass |
| maxRange | 6 | Short range — requires close contact |
| distanceFalloffRate | 0.35 | Steeper falloff than airborne; matters a lot inside 3 cells |
| cleanlinessImpact | 1.0 | Filthy areas increase transmission — fleas thrive in debris |
| outdoorFactor | 0.75 | Outdoor spread is still significant (fleas outdoors) |
| outdoorFilthRadius | 4 | Outdoor filth within 4 cells increases transmission |
| maskTargetEffectiveness | 0.4 | Physical barrier (gas mask) reduces flea contact somewhat |
| maskSourceEffectiveness | 0.3 | |
| airwayImmunityFactor | **0** | Gene-based airway immunity (breathless) does nothing — fleas are not inhaled |

### Cross-species transmission

| Parameter | Value | Notes |
|---|---|---|
| crossSpeciesTransmissionFactor | 0.5 | Human↔animal proximity spread at 50% of same-species rate |

An infected animal within 6 cells of a human rolls at 50% of the base chance (flea transfer still happens, just less efficiently than flea-to-flea). An infected human within range of livestock similarly spreads at 50%.

---

## Infectivity

### Active infectivity curve

Plague peaks at high severity and tapers near death (too sick to move and shed).

| Severity | Multiplier |
|---|---|
| 0.00 | 0.2 |
| 0.20 | 0.8 |
| 0.60 | 1.0 |
| 0.90 | 0.4 |
| 1.00 | 0.0 |

### Incubation infectivity

Plague spreads during incubation at above-average rates because infected fleas on the carrier jump to nearby pawns independent of host symptom stage. Unlike flu (which ramps steeply only near symptom onset), plague's incubation curve is flat-and-meaningful from day one.

| Incubation progress | Multiplier |
|---|---|
| 0.0 (just infected) | 0.3 |
| 0.5 (mid-incubation) | 0.45 |
| 1.0 (onset) | 0.6 |

The 2.5-day incubation window means an infected pawn can spread plague silently for up to 2.5 days before showing symptoms — longer than flu (1.5 d) or malaria (2.0 d). Combined with a meaningful early infectivity, this gives plague a distinctive "hidden spreader" character that rewards early isolation.

### Source infectivity factors

| Factor | Condition | Effect |
|---|---|---|
| SourceFactor_Trait | Trait `Immunity` degree −1 (Sickly) | ×0.5 source infectivity |

### Seasonal variation

None configured. Flea populations are temperature-dependent in reality, but plague is not currently season-weighted. A summer peak or a cold-kills-fleas winter dip could be added.

---

## Suppression and Caps

| Field | Value | Notes |
|---|---|---|
| maxActiveCases | 6 | Covers both species combined (Plague + Animal_Plague both counted) |
| spreadSuppressionScale | 1.0 | Normal colony-fraction suppression applies |
| outbreakNotification | FirstCase (default) | |

---

## Special Mechanics

### Corpse contagiousness (`corpseContagious true`)

When a plague-infected animal dies, its corpse spawns immediately in the `Rotting` state. Rotten corpses are not butcherable and are auto-hauled as garbage. This prevents players from accidentally processing an infected carcass without deliberate action.

**Gizmos on colony animals:**
- *Slaughter and dispose* — forces rotten corpse regardless of disease state.
- *Slaughter and butcher anyway* — overrides the rotten default; corpse spawns fresh. The butchering chain still fires contamination checks.

**Butchering contamination** (`Patch_Corpse_ButcherProducts`):
1. Roll Animals skill / 15 as notice chance.
2. **Notice:** all products discarded, corpse forbidden, alert sent.
3. **Miss:** raw meat receives full contamination (`Plague` stamped into `Comp_ContaminatedFood`).

Contaminated meat follows the normal foodborne chain: cooking reduces contamination by recipe factor (survival meal 0.05×, simple meal 0.35×, raw 1.0×). A human eating contaminated meat rolls for `Plague` infection via `Vector_Foodborne`.

### Sick signal (`showsSickSignal true`)

Animals incubating plague (hidden `Hediff_ContagionIncubation` with `TargetDiseaseDef Animal_Plague`) can be detected before symptoms appear.

**Detection path 1 — handler interaction:** When a colonist performs an `AnimalChat` interaction (training, tending, feeding), they roll `Animals skill / 20`. On success, `Contagion_AnimalSick` is applied — a visible signal in the animal's health bar. Works for colony animals only (wild animals receive no `AnimalChat` interactions from player colonists).

**Detection path 2 — passive symptom presentation:** Any animal carrying a hidden active disease (`Hediff_ContagionAnimalHiddenDisease`, diagnosed or not) rolls a per-disease curve every half game-day. On success, `Contagion_AnimalSick` is applied and a message fires. This covers wild animals and colony animals whose handlers never noticed. Cumulative probability over a typical untreated course is approximately **25%**.

| Severity | Per-check chance |
|---|---|
| 0.0 | 0% |
| 0.3 | 1% |
| 0.6 | 3% |
| 1.0 | 5% |

**Diagnosis:** When a doctor tends `Contagion_AnimalSick`, roll `Medicine skill / 15`:
- True positive, skill passes: incubation collapses to mild active disease (severity 0.1). Player sees the disease.
- True positive, skill fails: sick signal cleared, disease stays hidden. Animal can be re-detected on next handler interaction or next passive roll.
- False positive (3% rate on healthy animals): sick signal cleared, "nothing concerning" message.

**Auto-slaughter exclusion:** Animals with `Contagion_AnimalSick` are excluded from auto-slaughter queues until the signal is resolved.

---

## Counterplay

- **Handler isolation** — keeping plague-infected animals away from human colonists (separate barn or pasture zone) cuts proximity transmission. The 6-cell maxRange means a wall between pens is enough.
- **Area restrictions** — sick colonists in a dedicated medical area prevent proximity spread to healthy colonists.
- **Cleaning** — filthy areas amplify proximity transmission (`cleanlinessImpact 1.0`). Clean floors reduce outbreak spread meaningfully.
- **Penoxycyline** — reduces contract chance via vanilla `DiseaseContractChanceFactor`.
- **Vets** — diagnosed animals go into active disease at low severity (0.1) and can be treated early. A skilled vet with the 48 h window can save most animals.
- **Butcher gizmos** — "Slaughter and dispose" is zero-risk removal when a handler notices sick behaviour before the sick signal fires.
- **No mask benefit for airway** — unlike flu, masks do not provide airway immunity against plague. Physical barrier (gas mask reducing skin contact) helps slightly (`maskTargetEffectiveness 0.4`) but breathless gene does nothing.

---

## Tuning Notes

- `crossSpeciesTransmissionFactor 0.5` is a first-pass value. Plague in real life crosses species very readily via flea vectors — 0.5 may be too conservative. Consider 0.6–0.7 after playtesting.
- The animal handler bias (`handlerBias 2.0`) for AnimalLinked seeding means handlers are 3× more likely than average colonists to be the initial human case (base 1.0 + bias 2.0 × normalized Animals skill). This is intentional: the narrative is "handler caught it from an infected animal."
- `maxActiveCases 6` was bumped from 4 (human-only) to account for the combined human+animal count. With a colony of 10 pawns and 8 animals, 6 active cases might still clear quickly. May need to go higher (8) or be split into separate human/animal caps in a future profile enhancement.
- No seasonal variation. A summer amplification (fleas active) and winter suppression (frozen fleas) would add realism and make winter plague a genuine choice — "safe to butcher in deep freeze?" — but adds design complexity.
- Incubation infectivity is set to a flat-ish curve (0.3 → 0.6). If playtesting shows plague outbreaks feel too fast or uncontainable, dial the starting value down toward 0.15–0.2 first; the 2.5-day window amplifies even moderate incubation infectivity.
- Passive symptom presentation peaks at 5% per half-day at severity 1.0, giving ~25% cumulative over a typical untreated course. If wild animal plague feels too invisible, raise the peak toward 0.08; if messages are too noisy, lower it or raise the severity threshold.
- Posthumous symptom chance (10%) is the probability an animal that died with hidden plague shows as an infected corpse. Raise toward 0.3 for diseases with obvious post-mortem lesions; lower toward 0 for truly occult infections.
