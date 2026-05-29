# Flu — Contagion Profile

Seasonal respiratory illness. Spreads person-to-person through the air and via contaminated vomit. Highly contagious in winter, largely quiet in summer. No animal crossover.

---

## Identity

| Field | Value |
|---|---|
| HediffDef | `Flu` |
| Animal variant | none (see [Animal_Flu](Animal_Flu.md)) |
| Species | Human only |
| Vanilla incident | `Disease_Flu` |
| Vanilla lethal severity | 1.0 |
| Vanilla tend cycle | 8 h |
| Immunity race | fast — `immunityPerDaySick 0.65`, `severityPerDayNotImmune 0.35` |

---

## Vanilla Disease Characteristics

Flu is fast and non-lethal in most cases: a well-tended pawn typically wins the immunity race well before lethal severity. The main threat is colony throughput — multiple simultaneous cases cut into food production, construction, and fighting capacity. Untended or immunosuppressed pawns can die.

---

## How It Enters the Colony

### Mode 1 (Storyteller-driven)
Storyteller fires `Disease_Flu` → pending event created with a 15-day window.

**Fulfillment chain:**
1. **Arrival** — the next qualifying arriving group carries a capped carrier payload. Low per-group chance (`arrivalChance 0.01`); in practice most flu events resolve via arrival within the window.
2. **Acausal fallback** — if 15 days pass with no arrival, a silent incubation is stamped on a random eligible pawn.

### Mode 2 (Contagion-driven)
- Arrivals roll continuous exposure (`arrivalChance 0.01` per qualifying group, `cooldownDays 3`).
- Acausal backstop at MTB 180 days (`cooldownDays 10`) for isolated colonies with no arrivals.
- Storyteller `Disease_Flu` incident is cancelled outright; Mode 2 owns pacing.

**Storyteller seeder cooldown:** 10 days (prevents the storyteller from queueing flu events back-to-back before the pending window resolves).

---

## Spread Vectors

### Vector_Airborne (primary)

Flu is primarily airborne. Shared indoor air is the main risk.

| Parameter | Value | Notes |
|---|---|---|
| baseChancePerCheck | 0.03 | Per 250-tick pass |
| maxRange | 15 | Cells |
| distanceFalloffRate | 0.25 | Exponential; moderate falloff |
| outdoorFactor | 0.15 | Outdoor dispersal sharply cuts risk |
| obstructedFactor | 0.0 | Walls and closed doors fully block |
| maskTargetEffectiveness | 0.7 | 70% of mask ToxicResist applied to inhale side |
| maskSourceEffectiveness | 0.5 | 50% applied to emit side |
| airwayImmunityFactor | 1.0 (default) | Breathless gene and airway barriers fully apply |

### Vector_Social (secondary)

Face-to-face conversations are a contact booster on top of airborne, regardless of distance.

| Parameter | Value | Notes |
|---|---|---|
| baseChancePerInteraction | 0.02 | Per social interaction |
| outdoorFactor | 0.5 | Half effect for outdoor conversations |
| maskTargetEffectiveness | 0.6 | |
| maskSourceEffectiveness | 0.5 | |
| airwayImmunityFactor | 1.0 (default) | |

### Vector_Fomite (escalation)

Contaminated vomit. Flu only reaches peak fomite infectivity at high severity (0.80–1.00), so vomit spread signals an already-bad case. Pawns stepping on tagged vomit filth roll for exposure.

| Parameter | Value | Notes |
|---|---|---|
| contaminatesVomit | true | |
| baseChancePerContact | 0.03 | |
| potencyDecayPerHour | 0.1 | Filth loses 10% potency per hour; cleaned filth removes risk entirely |
| activeInfectivityCurveOverride | (0.50, 0.0) → (0.65, 0.5) → (0.80, 1.0) → (1.00, 0.5) | Only high-severity cases contaminate vomit |

---

## Infectivity

### Active infectivity curve

| Severity | Multiplier | Notes |
|---|---|---|
| 0.00 | 0.4 | Contagious immediately on activation |
| 0.25 | 1.0 | Peak early in illness |
| 0.55 | 1.0 | Sustained peak |
| 0.80 | 0.3 | Tapering — recovery phase |
| 1.00 | 0.0 | Near death: too sick to shed |

### Incubation infectivity curve

Pre-symptomatic spread is real but modest. A pawn with hidden incubation is mildly infectious.

| Incubation progress | Multiplier |
|---|---|
| 0.0 | 0.0 |
| 0.3 | 0.15 |
| 0.7 | 0.45 |
| 1.0 | 0.8 |

### Source infectivity factors

| Factor | Condition | Effect |
|---|---|---|
| SourceFactor_Trait | Trait `Immunity` degree −1 (Sickly) | ×0.5 source infectivity |

Sickly pawns catch flu more often (vanilla `randomDiseaseMtbDays`) but shed at half rate, keeping them from being a constant outbreak engine.

### Seasonal variation

| Season | Multiplier |
|---|---|
| Winter | 1.0 |
| Fall | 0.8 |
| Spring | 0.6 |
| Permanent winter | 0.7 |
| Summer | 0.3 |
| Permanent summer | 0.4 |

---

## Suppression and Caps

| Field | Value | Notes |
|---|---|---|
| maxActiveCases | 5 | Seeding suppressed when ≥5 active + incubating |
| spreadSuppressionScale | 1.0 (default) | Normal colony-fraction suppression applies |
| outbreakNotification | FirstCase | Letter fires on first active case |

---

## Counterplay

- **Masks** significantly reduce airborne and social transmission (breathless gene also helps).
- **Hospital isolation** — walls and closed doors block airborne LOS; keeping sick pawns out of shared spaces matters.
- **Social work priority** — cancelling social recreation for sick pawns eliminates the social vector.
- **Cleaning** — removing vomit filth quickly cuts the fomite escalation path.
- **Penoxycyline** — `DiseaseContractChanceFactor` reduces contract chance; a `Factor_Hediff` entry in the profile would make it explicit if added.

---

## Tuning Notes

- Seasonal multipliers are first-pass. Flu should feel like a winter/fall disease with near-silence in summer. The 0.3 summer multiplier may still be too high for tropical biomes — `permanentSummer 0.4` needs field testing.
- Incubation infectivity (pre-symptomatic spread) is intentional but the curve may be too steep into late incubation (0.8 at full incubation). Consider flattening to 0.5–0.6 if pre-symptomatic spread feels too punishing.
- `maxActiveCases 5` for a 10-pawn colony means roughly half the colony could theoretically be sick simultaneously before seeding suppresses new incubations. This may be too high; consider 4.


https://pubmed.ncbi.nlm.nih.gov/15172341/

