# Flu — Contagion Profile

Seasonally introduced respiratory illness. Spreads person-to-person through the air and via contaminated vomit. Winter raises Contagion-mode arrival pressure, but ongoing spread is driven by indoor crowding, exposure routes, and counterplay rather than a calendar multiplier. No animal crossover.

---

## Identity

| Field | Value |
|---|---|
| HediffDef | `Flu` |
| Animal variant | none (see [Animal_Flu](Animal_Flu.md)) |
| Species | Human only |
| Vanilla incident | `Disease_Flu` |
| Contagion incubation | 1 day |
| Vanilla lethal severity | 1.0 |
| Vanilla tend cycle | 12 h |
| Immunity race | close but survivable with care — `immunityPerDaySick 0.2388`, `severityPerDayNotImmune 0.2488` |
| Contagion immunity | 30 days post-recovery (`immunityDurationDays 30`) |

---

## Vanilla Disease Characteristics

Flu is usually survivable with treatment, but the untended immunity race is close enough to matter. The main threat is colony throughput — multiple simultaneous cases cut into food production, construction, and fighting capacity. Untended, immunosuppressed, or badly managed pawns can die.

---

## How It Enters the Colony

### Mode 1 (Storyteller-driven)
Storyteller fires `Disease_Flu` → pending event created with a 15-day window.

**Fulfillment chain:**
1. **Arrival** — the next qualifying arriving group carries a capped carrier payload. Low per-group chance (`arrivalChance 0.015`); in practice most flu events resolve via arrival within the window.
2. **Acausal fallback** — if 15 days pass with no arrival, a silent incubation is stamped on a random eligible pawn.

### Mode 2 (Contagion-driven)
- Arrivals roll continuous exposure (`arrivalChance 0.015` per qualifying group, `cooldownDays 3`), season-weighted by the profile's winter/fall-peaking introduction multiplier, and scaled by the player's Contagion-mode **disease incidence** slider (x0.1–x3.0).
- No acausal backstop. Isolated colonies with no infected arrivals can avoid flu introductions.
- Storyteller `Disease_Flu` incident is cancelled outright; Mode 2 owns pacing.

**Storyteller seeder cooldown:** 10 days (prevents the storyteller from queueing flu events back-to-back before the pending window resolves).

---

## Spread Vectors

### Vector_Airborne (primary)

Flu is primarily airborne. It has two airborne components: a direct cough/sneeze plume that requires line of sight, and weaker shared-room aerosol exposure that can affect nearby pawns around corners in the same enclosed room.

| Parameter | Value | Notes |
|---|---|---|
| baseChancePerCheck | 0.02 | Per transmission pass (default 500 ticks; global cadence, see `ContagionTransmissionTuningDef`) |
| maxRange | 10 | Direct plume range, in cells |
| distanceFalloffRate | 0.25 | Exponential; moderate falloff |
| outdoorFactor | 0.5 | Outdoor dispersal cuts risk, but not so sharply that outdoor caravans/visitors can't seed the colony |
| obstructedFactor | 0.0 | Walls and closed doors fully block the direct plume |
| roomAirBaseChanceFactor | 0.25 | Same-room aerosol base chance is 25% of direct plume |
| roomAirMaxRange | 10 | Same-room aerosol does not affect pawns farther than 10 cells apart |
| roomAirMaxCells | 100 | Larger rooms are too ventilated/dilute for this component |
| airwayImmunityFactor | 1.0 (default) | Breathless gene and airway barriers fully apply |

### Vector_Social (secondary)

Face-to-face conversations are a contact booster on top of airborne, regardless of distance.

| Parameter | Value | Notes |
|---|---|---|
| baseChancePerInteraction | 0.1 | Per social interaction |
| outdoorFactor | 0.8 | Barely reduced — face-to-face trading and chatting happen at conversational range regardless of a roof |
| airwayImmunityFactor | 1.0 (default) | |

### Vector_Fomite (escalation)

Contaminated vomit. Flu only reaches peak fomite infectivity at high severity (0.80–1.00), so vomit spread signals an already-bad case. Pawns stepping on tagged vomit filth roll for exposure. The vomit stores its fomite-specific potency when it is created, then decays over time.

| Parameter | Value | Notes |
|---|---|---|
| contaminatesVomit | true | |
| baseChancePerContact | 0.03 | |
| potencyDecayPerHour | 0.1 | Filth loses 10% potency per hour; cleaned filth removes risk entirely |
| activeInfectivityCurveOverride | (0.50, 0.0) → (0.65, 0.5) → (0.80, 1.0) → (1.00, 0.5) | Only high-severity cases contaminate vomit |
| apparelProtection | hands 0.60, airway 0.40; unsealedEffectiveness 0.60 | Target-side. A glove is a real touch barrier; a sealed helmet blocks touching your face. Ordinary clothing helps moderately (floor 0.6); a sealed suit/glove approaches immunity. See `docs/Apparel_Protection_Design.md`. |

---

## Infectivity

### Active infectivity curve

| Severity | Multiplier | Notes |
|---|---|---|
| 0.00 | 0.5 | Smooth continuation from full incubation |
| 0.25 | 1.0 | Peak early in illness |
| 0.55 | 1.0 | Sustained peak |
| 0.80 | 0.3 | Tapering — recovery phase |
| 1.00 | 0.0 | Near death: too sick to shed |

### Incubation infectivity curve

Pre-symptomatic spread is real but modest. A pawn with hidden incubation is mildly infectious.

| Incubation progress | Multiplier |
|---|---|
| 0.0 | 0.1 |
| 0.3 | 0.2 |
| 0.7 | 0.45 |
| 1.0 | 0.5 |

The 0.1 floor at the start of incubation ensures a carrier that arrives mid-incubation (e.g. on a trade caravan) still sheds a minimal amount before symptom onset, rather than ~0.

### Source infectivity factors

| Factor | Condition | Effect |
|---|---|---|
| SourceFactor_Trait | Trait `Immunity` degree −1 (Sickly) | ×0.5 source infectivity |

Sickly pawns catch flu more often (vanilla `randomDiseaseMtbDays`) but shed at half rate, keeping them from being a constant outbreak engine.

### Seasonal introduction pressure

These weights apply to Contagion-mode arrival/seeding pressure only. They do **not** multiply airborne, social, or fomite spread after flu is present.

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
| useScaledActiveCaseCap | true (default) | Colony-human, lord-group, and other applicable human scopes calculate capacity separately |
| maxActiveCaseChanceOffset | 0 | Cap chance is 30% + 1% per affectable pawn in the target scope, floored, max 50% |
| spreadSuppressionScale | 1.0 (default) | Normal suppression applies |
| outbreakNotification | FirstCase | Red source-attributed letter on the first visible case; later visible cases update a yellow cluster letter while the outbreak remains active |

Arrival groups and other on-map non-colony lord groups self-limit against their own human carrier population. A trade caravan, visitor group, ally group, or raid with flu no longer uses the colony cap and no longer spreads unchecked through its members; transmission into a pawn is dampened by that pawn's own scope.

---

## Counterplay

- **Airway protection** significantly reduces airborne and social transmission; filtering masks reduce exposure, and a sealed combat/space helmet makes the wearer immune to airborne/social/proximity spread. Breathless gene also helps. See `docs/Apparel_Protection_Design.md`.
- **Hospital isolation** — walls and closed doors block direct airborne LOS and split room-air exposure; keeping sick pawns out of shared spaces matters.
- **Social work priority** — cancelling social recreation for sick pawns eliminates the social vector.
- **Cleaning + gloves** — removing vomit filth quickly cuts the fomite escalation path; gloves/sealed gear reduce what a pawn picks up from tagged vomit (fomite `apparelProtection`).
- **Penoxycyline** — `DiseaseContractChanceFactor` reduces contract chance; a `Factor_Hediff` entry in the profile would make it explicit if added.

---

## Tuning Notes

- Seasonal introduction multipliers are first-pass. Flu should enter colonies more often in winter/fall and much less often in summer. The 0.3 summer multiplier may still be too high for tropical biomes — `permanentSummer 0.4` needs field testing.
- Incubation infectivity (pre-symptomatic spread) runs from a 0.1 floor up to 0.5 at full incubation, so it hands off smoothly into active flu while guaranteeing an incubating arrival carrier is never effectively non-infectious.
- Scaled caps mean a 10-colonist colony has a flu cap of 4 active+incubating human cases before new seeding is suppressed. A 10-pawn visiting lord group uses the same cap for its own members while it is spawned. Strong suppression reaches zero direct spread at that cap.


https://pubmed.ncbi.nlm.nih.gov/15172341/

