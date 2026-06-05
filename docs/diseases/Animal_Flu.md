# Animal Flu — Contagion Profile

Respiratory flu strain adapted to non-human animals. Mirrors human Flu in mechanics, cannot infect humans, and uses colony species-count suppression for jumps between animal races. Infected animals are hidden until diagnosed and surface through the sick-signal + vet diagnosis chain, like the other animal diseases. Meat from a flu-infected animal is safe — butchering carries no infection risk.

---

## Identity

| Field | Value |
|---|---|
| HediffDef | `Animal_Flu` |
| Human counterpart | `Flu` (separate, no shared cluster) |
| Species | Animal only |
| Vanilla incident | `Disease_AnimalFlu` |
| Contagion incubation | 1 day |
| Vanilla lethal severity | 1.0 |
| Vanilla tend cycle | 48 h (animals tend less frequently) |

---

## Vanilla Disease Characteristics

Same severity stages as human flu but slower tend cycle. Animals in well-managed barns with an attentive vet recover; wild animals or neglected livestock are at higher risk. The 48 h tend cycle means 4× fewer tending events — taming a vet loop matters.

---

## How It Enters the Colony

### Mode 1 (Storyteller-driven)
Storyteller fires `Disease_AnimalFlu` → pending event created with a 15-day window.

**Fulfillment chain:**
1. **Arrival** — next qualifying arriving group (farm animal wander-in, caravan animals) carries a capped carrier payload.
2. **Acausal fallback** — if 15 days pass with no qualifying arrival, silent incubation on a random eligible animal.

### Mode 2 (Contagion-driven)
- Arrivals roll exposure at `arrivalChance 0.01` per qualifying group, `cooldownDays 3`.
- No acausal backstop. Colonies with no infected animal arrivals can avoid animal flu introductions.
- Storyteller `Disease_AnimalFlu` incident cancelled; Mode 2 owns pacing.

**Storyteller seeder cooldown:** 10 days.

---

## Spread Vectors

### Vector_Airborne

Animals spread flu to each other through direct respiratory plumes and shared barn air. Direct plume exposure requires line of sight; weaker same-room aerosol exposure can reach nearby animals around corners in the same enclosed room.

| Parameter | Value | Notes |
|---|---|---|
| baseChancePerCheck | 0.03 | |
| maxRange | 10 | Direct plume range, in cells |
| distanceFalloffRate | 0.25 | |
| outdoorFactor | 0.15 | Outdoor pastures dramatically reduce risk |
| obstructedFactor | 0.0 | Walls block the direct plume |
| roomAirBaseChanceFactor | 0.25 | Same-room aerosol base chance is 25% of direct plume |
| roomAirMaxRange | 10 | Same-room aerosol does not affect animals farther than 10 cells apart |
| roomAirMaxCells | 100 | Larger barns are too ventilated/dilute for this component |
| airwayImmunityFactor | 1.0 (default) | |

### Vector_Fomite

Vomit contamination from sick animals spreads the disease if other animals step on it. Animal flu uses the profile's normal active infectivity curve for vomit potency rather than the human flu high-severity fomite override.

| Parameter | Value |
|---|---|
| contaminatesVomit | true |
| baseChancePerContact | 0.03 |
| potencyDecayPerHour | 0.1 |
| activeInfectivityCurveOverride | none; uses the profile active infectivity curve |

The vomit stores its fomite-specific potency when it is created, then decays over time. Mixed-animal-species fomite exposure uses the same species-count suppression curve as airborne spread.

---

## Infectivity

### Active infectivity curve

Identical to human Flu.

| Severity | Multiplier |
|---|---|
| 0.00 | 0.5 |
| 0.25 | 1.0 |
| 0.55 | 1.0 |
| 0.80 | 0.3 |
| 1.00 | 0.0 |

### Incubation infectivity curve

Pre-symptomatic spread mirrors human Flu.

| Incubation progress | Multiplier |
|---|---|
| 0.0 | 0.0 |
| 0.3 | 0.15 |
| 0.7 | 0.45 |
| 1.0 | 0.5 |

### Seasonal variation

None configured. Animal flu is not currently season-weighted; it runs at flat infectivity year-round. This differs from human flu and may need revisiting.

### Source/susceptibility factors

None configured.

---

## Suppression and Caps

| Field | Value | Notes |
|---|---|---|
| useScaledActiveCaseCap | true (default) | Animal cap scales from player animal count |
| maxActiveCaseChanceOffset | 0 | Cap chance is 30% + 1% per animal, floored, max 50% |
| spreadSuppressionScale | 1.0 (default) | |
| outbreakNotification | FirstCase (default) | Pure-animal exception suppresses outbreak and cluster letters; the player is notified through the sick-signal + diagnosis path instead |
| showsSickSignal | true | Hidden until diagnosed; handlers notice the sick signal, doctors diagnose |

Although the profile inherits `FirstCase`, Animal Flu has `affectsHumans false`. The notifier deliberately suppresses the red/yellow outbreak letters for pure-animal diseases. Player notification instead comes through the animal-disease chain shared with plague, gut worms, and muscle parasites: a handler noticing the sick signal, a vet diagnosis letter, and the floating disease-activation message.

---

## Species Isolation

`affectsHumans false`, `affectsAnimals true`. Animal flu cannot infect humans under any circumstances, and human flu cannot infect animals. Butchering a flu-infected animal produces clean meat (`corpseContagious false`) — no posthumous infected-corpse marker is ever applied. The one nuance: if an animal is showing the sick signal at the moment it dies, its corpse is flagged *suspected infected* (the generic sick-signal-at-death rule), which a post-mortem inspection clears as a harmless false positive. This matches the other animal diseases and reinforces that a sick signal is uncertainty, not proof of danger.

### Inter-animal cross-species suppression

`animalCrossSpeciesFactorCurve` scales transmission into a new colony animal race when the source and target are animals of **different races** (e.g. chicken -> pig, duck -> cow). The curve is keyed by the number of player-colony animal races already carrying Animal Flu on the map; visiting, caravan, wild, or other non-colony carriers do not count.

| Infected colony animal races | New-race cross-species factor |
|---|---:|
| 0 | 1.00 |
| 1 | 0.25 |
| 2+ | 0.05 |

This makes the first spillover into a colony's animal population free if the disease arrives on an animal race the colony does not keep. Once one colony animal race is involved, jumps into another colony animal race are strongly suppressed, and broader multi-species spread becomes rare. Same-race transmission (chicken -> chicken) and transmission into a colony animal race that is already involved are unaffected.

---

## Counterplay

- **Barn separation** — keeping sick animals in a separate roofed area away from healthy animals contains airborne spread.
- **Vet tending** — a dedicated doctor or handler with high Medicine skill keeping the 48 h tend window reduces severity progression.
- **Cleaning** — removing vomit filth from shared animal areas cuts fomite spread.
- **Sick signal + diagnosis** — animal flu uses the shared animal-disease chain (`showsSickSignal true`). Infected animals stay hidden until a handler notices the sick signal and a vet diagnoses them. A diagnosis letter fires for colony animals, so an outbreak no longer requires the player to inspect every animal's health tab. The standard generic diagnosis advice ("do not butcher until it recovers") still appears, though flu meat is in fact safe.

---

## Tuning Notes

- No seasonal variation is currently configured. A mild winter peak or a flat year-round profile may both be defensible (animal flu outbreaks in real farming are year-round), but it is worth making a deliberate choice.
- The fomite vector intentionally does not use the human flu high-severity override. Animal flu progresses and tends differently, and animals sharing dirty barns should remain a plausible fomite risk.
- Scaled caps replace the old flat herd cap. A 10-animal herd has an animal-flu cap of 4 active+incubating cases, and a 20-animal herd caps at 10.
- `animalCrossSpeciesFactorCurve` is a first-pass tuning pass. If Animal Flu still spreads too widely through mixed herds, lower the 1-race and 2-race factors; if it fails to establish from absent-species carriers, keep the 0-race factor at 1.0.
