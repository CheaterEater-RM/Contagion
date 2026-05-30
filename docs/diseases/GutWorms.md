# Gut Worms — Contagion Profile

Water-borne intestinal parasite entering through contaminated water and infected animal meat. Spreads within the colony via contaminated food and vomit. Chronic low-severity disease; rarely lethal but drains productivity. No person-to-person airborne or proximity spread.

---

## Identity

| Field | Value |
|---|---|
| HediffDef | `GutWorms` |
| Animal variant | none (same hediff applies to both, but animals skip part targeting) |
| Species | Human + Animal |
| Vanilla incident | `Disease_GutWorms` |
| Target body part | Stomach (human only; animals get generic application) |
| Vanilla lethal severity | 1.0 |
| Vanilla tend cycle | 3 days |
| Vanilla immunity | Non-immunizable — requires mod-owned post-recovery protection |
| Contagion immunity | 15 days post-recovery (`immunityDurationDays 15`) |

---

## Vanilla Disease Characteristics

Gut worms is chronic: low severity, slow progression, but it doesn't clear without treatment. Primarily a productivity drain — reduced movement, manipulation, and consciousness. Not typically lethal unless severely neglected. The 3-day tend window means infrequent treatment is sufficient. Animals carry it persistently without dying, making them the long-term reservoir.

---

## How It Enters the Colony

### Mode 1 (Storyteller-driven)
Storyteller fires `Disease_GutWorms` → pending event created. Unlike most diseases, gut worms resolves via an **environmental window** rather than a carrier seed.

**Fulfillment chain:**
1. **Environmental window** — `Vector_Environmental` runs continuously for up to 14 days (`windowDays 14`) with an infection budget of 2–4 cases. Pawns with outdoor access near water accumulate exposure until the budget is spent or the window closes.
2. **Acausal fallback** — if the 14-day window closes without spending the full budget, any remaining unfulfilled cases resolve via silent incubation.

No arrival fulfillment: gut worms does not arrive on incoming pawns. It enters through the environment.

### Mode 2 (Contagion-driven)
- **Environmental exposure** runs continuously (same `Vector_Environmental` as Mode 1, just always on rather than window-bounded).
- **Acausal backstop** — MTB 180 days, `cooldownDays 20`. For colonies fully sheltered from the environment.
- Storyteller incident cancelled; Mode 2 owns pacing.

**Storyteller seeder cooldown:** 20 days.

### What drives the environmental risk

- Water proximity: bodies of water within `waterProximityRadius 14` cells increase exposure dramatically (`waterProximityWeight 0.08` — the highest of any disease).
- Temperature: eggs require above-freezing water to remain viable (`minTemperature 0°C`) — frozen or icy water suppresses transmission. Peak in moderate warmth (`peakTemperature 22°C`).
- Outdoor vs. indoor: indoor animals (roofed barn) receive `indoorReductionPerCellFromEdge 0.15` per cell of depth from the nearest unroofed cell. An animal in the centre of a large barn has near-zero exposure. Grazing animals in open pastures have full exposure.

---

## Spread Vectors

### Vector_Foodborne (primary human infection path)

The main way colonists get gut worms is eating contaminated food — either cooked by an infected colonist or made from infected animal meat.

| Parameter | Value | Notes |
|---|---|---|
| baseChancePerMeal | 0.08 | Per meal consumed from a contaminated source |
| cleanlinessImpact | 1.0 | Dirty kitchen amplifies contamination at cooking time |
| contaminationExpiryDays | 30 | Old preserved food becomes safe after 30 days |

**Contamination sources:**
- *Infected cook:* an active gut worms patient cooking a meal stamps contamination proportional to infectivity × kitchen cleanliness.
- *Infected meat:* `Patch_Corpse_ButcherProducts` stamps raw meat from a `corpseContagious` animal (see below).
- *Ingredient propagation:* cooking contaminated raw ingredients propagates contamination to the meal, reduced by recipe factor.

### Vector_Fomite (secondary — escalation)

Gut worms causes vomiting. High-severity cases contaminate vomit filth, which other colonists can step on.

| Parameter | Value | Notes |
|---|---|---|
| contaminatesVomit | true | |
| baseChancePerContact | 0.025 | Slightly lower than flu |
| potencyDecayPerHour | 0.08 | Slower decay than flu — gut worm vomit lingers |
| activeInfectivityCurveOverride | (0.50, 0.0) → (0.65, 0.5) → (0.80, 1.0) → (1.00, 0.8) | Peak at severe cases; stays high near lethal |

### Vector_Environmental (animal acquisition only)

This vector does not infect humans directly — it exposes outdoor animals to contaminated water. See "How It Enters the Colony" above for the seeder role.

| Parameter | Value | Notes |
|---|---|---|
| baseChancePerCheck | 0.002 | Per 2500-tick environmental pass |
| minTemperature | 0°C | Eggs require above-freezing water; frozen water suppresses transmission |
| peakTemperature | 22°C | Moderate warmth, not tropical |
| waterProximityRadius | 14 | Wide radius — rivers and large ponds at range |
| waterProximityWeight | 0.08 | Strongest water dependency of any disease |
| indoorReductionPerCellFromEdge | 0.15 | Barn depth matters significantly |
| coolRoomThreshold | 10°C | Refrigerated rooms reduce risk |

---

## Infectivity

### Active infectivity curve

Gut worms infectivity rises through the illness and stays high. A chronic case that never clears remains a constant food-contamination risk.

| Severity | Multiplier |
|---|---|
| 0.00 | 0.3 |
| 0.20 | 0.7 |
| 0.60 | 1.0 |
| 1.00 | 1.0 |

### Incubation infectivity

None configured. Gut worms does not spread during incubation.

### Seasonal variation

None configured (worm eggs have broad temperature tolerance).

### Source/susceptibility factors

None configured.

---

## Suppression and Caps

| Field | Value | Notes |
|---|---|---|
| maxActiveCases | 3 | Hard cap — gut worms is low-pressure and chronic |
| spreadSuppressionScale | **0** | Colony-fraction suppression **disabled** — gut worms is foodborne, not person-to-person herd spread |
| outbreakNotification | **None** | Silent — no letter. Discovery via health tab inspection |

Spread suppression is off because the foodborne vector is not herd-transmission. A dirty kitchen or infected cook can give everyone gut worms regardless of how many are already infected. The colony-fraction model does not fit.

---

## Special Mechanics

### Corpse contagiousness (`corpseContagious true`)

Animals killed while infected with gut worms spawn a rotten corpse. If butchered (via the "butcher anyway" bypass), raw meat receives full contamination. This is the primary human infection path: cook eats contaminated meat → gets gut worms → infects meals → other colonists eat them.

### Sick signal (`showsSickSignal true`)

Animals incubating gut worms can be detected by handlers via `AnimalChat` interaction (Animals skill / 20 roll). The `Contagion_AnimalSick` hediff is applied on detection. Diagnosis by a vet (Medicine skill / 15) either collapses incubation to mild active disease or produces a false negative.

This mechanic is especially important for gut worms: undetected infected animals go through the butchering chain and contaminate the meat supply. Attentive handlers and skilled vets are the first line of defense.

---

## Counterplay

- **Water management** — animals near rivers or large ponds have much higher environmental exposure. Roofed barns with no water proximity are effectively safe.
- **Indoor livestock** — the strongest single counter. An animal in the centre of a large roofed barn has near-zero gut worm exposure.
- **Vet inspection** — the sick signal lets a skilled handler catch infected animals before slaughter. High Animals skill is the key lever.
- **"Slaughter and dispose"** — guaranteed-safe removal of a suspected animal without entering the meat chain.
- **Butcher skill** — the Animals skill / 15 roll in `Patch_Corpse_ButcherProducts` lets a skilled butcher notice contamination and discard all products. A dedicated, skilled butcher significantly reduces meat-chain risk.
- **Kitchen hygiene** — infected cooks in dirty kitchens produce more contaminated food. Restricting sick pawns from cooking is the strongest single lever against the food-chain spread.
- **Cooking** — survival meals (0.05×) and lavish meals (0.10×) nearly eliminate contamination from cooking. Simple meals (0.35×) and pemmican (0.70×) are risky with contaminated meat.
- **Immunity** — 15-day post-recovery immunity prevents immediate re-infection from the same source.

---

## Tuning Notes

- `baseChancePerCheck 0.002` for environmental exposure is very low per tick but runs every 2500 ticks. The total per-day probability depends heavily on water proximity. May need field testing across different biomes — desert colonies near no water may never naturally acquire gut worms from the environment.
- Fomite `potencyDecayPerHour 0.08` gives gut-worm vomit a ~12 h half-life. This means a single vomit event from a severe case contaminates an area for half a day. If cleaning is poor, this can become a significant secondary spread path. Intentional: it rewards keeping sick pawns isolated and areas clean.
- The `outbreakNotification None` setting means players have no alert that gut worms are present. Discovery is organic (health tab, handler detection). This is a design choice — it keeps gut worms as background pressure rather than a crisis event.
- `maxActiveCases 3` may be very tight for a colony with many animals. If 3 animals all get gut worms from the environment simultaneously, this blocks further seeding — but they're still in the food chain until detected.
