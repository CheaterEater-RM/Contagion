# Animal Flu — Contagion Profile

Respiratory flu strain adapted to non-human animals. Mirrors human Flu in mechanics but is completely species-isolated — no crossover in either direction. Safe to butcher infected animals.

---

## Identity

| Field | Value |
|---|---|
| HediffDef | `Animal_Flu` |
| Human counterpart | `Flu` (separate, no shared cluster) |
| Species | Animal only |
| Vanilla incident | `Disease_AnimalFlu` |
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
- Acausal backstop at MTB 180 days (`cooldownDays 10`).
- Storyteller `Disease_AnimalFlu` incident cancelled; Mode 2 owns pacing.

**Storyteller seeder cooldown:** 10 days.

---

## Spread Vectors

### Vector_Airborne

Animals spread flu to each other through shared airspace. The same LOS + roofing rules apply: indoor barn air concentrates spread; outdoor pastures disperse it.

| Parameter | Value | Notes |
|---|---|---|
| baseChancePerCheck | 0.03 | |
| maxRange | 15 | |
| distanceFalloffRate | 0.25 | |
| outdoorFactor | 0.15 | Outdoor pastures dramatically reduce risk |
| obstructedFactor | 0.0 | Walls block |
| maskTargetEffectiveness | 0.0 (default) | Animals don't wear masks |
| maskSourceEffectiveness | 0.0 (default) | |
| airwayImmunityFactor | 1.0 (default) | |

### Vector_Fomite

Vomit contamination from sick animals spreads the disease if other animals step on it. Same curve as human flu — only high-severity cases produce infectious vomit.

| Parameter | Value |
|---|---|
| contaminatesVomit | true |
| baseChancePerContact | 0.03 |
| potencyDecayPerHour | 0.1 |
| activeInfectivityCurveOverride | same as Flu: peak at severity 0.80–1.00 |

No fomite infectivity curve override is defined; the animal flu fomite uses the profile's active curve directly (not the flu-specific override). This means animal flu vomit is somewhat infectious throughout illness rather than only at high severity. This may be intentional (animals are messier) or a minor oversight — see tuning notes.

---

## Infectivity

### Active infectivity curve

Identical to human Flu.

| Severity | Multiplier |
|---|---|
| 0.00 | 0.4 |
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
| 1.0 | 0.8 |

### Seasonal variation

None configured. Animal flu is not currently season-weighted; it runs at flat infectivity year-round. This differs from human flu and may need revisiting.

### Source/susceptibility factors

None configured.

---

## Suppression and Caps

| Field | Value | Notes |
|---|---|---|
| maxActiveCases | 5 | |
| spreadSuppressionScale | 1.0 (default) | |
| outbreakNotification | FirstCase (default) | |

---

## Species Isolation

`affectsHumans false`, `affectsAnimals true`. No `crossSpeciesTransmissionFactor` set. Animal flu cannot infect humans under any circumstances, and human flu cannot infect animals. Butchering a flu-infected animal produces clean meat (`corpseContagious false`).

---

## Counterplay

- **Barn separation** — keeping sick animals in a separate roofed area away from healthy animals contains airborne spread.
- **Vet tending** — a dedicated doctor or handler with high Medicine skill keeping the 48 h tend window reduces severity progression.
- **Cleaning** — removing vomit filth from shared animal areas cuts fomite spread.
- There is no sick-signal mechanic for animal flu (`showsSickSignal false`). Detection is player-initiated via health tab inspection.

---

## Tuning Notes

- No seasonal variation is currently configured. A mild winter peak or a flat year-round profile may both be defensible (animal flu outbreaks in real farming are year-round), but it is worth making a deliberate choice.
- The fomite vector does not use an override curve, so animal flu vomit is somewhat infectious from severity 0.0. Whether this is the intended behavior (messier animals) or an oversight (should match the human flu pattern of high-severity-only vomit) should be decided.
- `maxActiveCases 5` means the entire herd could theoretically be infected before seeding suppresses. Since herds vary in size widely, this number may need to be relative or biased by animal count rather than a flat cap.
