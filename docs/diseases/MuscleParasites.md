# Muscle Parasites — Contagion Profile

Trichinella-type larvae contracted from raw or undercooked infected meat. Purely a meat-chain disease — no person-to-person spread, no vomiting. The full chain is: environment → outdoor animal → butchered meat → human. Longer incubation than gut worms, longer immunity. No crossover between humans in any direction.

---

## Identity

| Field | Value |
|---|---|
| HediffDef | `MuscleParasites` |
| Animal variant HediffDef | `Animal_MuscleParasites` |
| Species | Human + Animal |
| Vanilla incident | `Disease_MuscleParasites` (vanilla Core) |
| Vanilla lethal severity | **None** — muscle parasites cannot kill directly |
| Vanilla removal | Accumulate 300% total tend quality (`disappearsAtTotalTendQuality 3`); no immunity race |
| Vanilla tend window | 48 h (`baseTendDurationHours 48`); ~3 skilled tends over ~4–6 days clears the disease |
| Contagion immunity | 20 days post-recovery (`immunityDurationDays 20`) |

### Why no `selfSchedules`

The vanilla Core `Disease_MuscleParasites` incident already exists and is picked by the storyteller. Contagion intercepts it in Mode 1 (creating a pending environmental window) or cancels it in Mode 2. No `selfSchedules` is needed; no new incident def is created. The animal-variant def (`Animal_MuscleParasites`) is added by Contagion and uses `Hediff_ContagionAnimalHiddenDisease` so the disease stays hidden in animals until a vet diagnoses it.

---

## Vanilla Disease Characteristics

Muscle parasites reduce movement capacity significantly. Larvae embed in muscle tissue — the primary gameplay hit is locomotion and manipulation penalties. There is no severity progression and no immunity race; the disease has no lethal threshold and cannot kill. The only removal mechanism is accumulating 300% total tend quality over roughly three vet visits. Without treatment the disease persists indefinitely.

For animals: same as gut worms — neither lethal nor self-clearing in vanilla. `Animal_MuscleParasites` adds `HediffComp_AnimalNaturalRecovery` so wild animals self-clear in ~15 days and domestic animals in ~25 days. Active vet tending remains far faster (~4–6 days).

---

## How It Enters the Colony

### Mode 1 (Storyteller-driven)
Storyteller fires `Disease_MuscleParasites` → pending event created. Resolves via an **environmental window** (same pattern as Gut Worms).

**Fulfillment chain:**
1. **Environmental window** — `Vector_Environmental` runs for up to 14 days. Infection budget 1–3 animals. Outdoor grazing animals in contaminated soil accumulate exposure.
2. **Acausal fallback** — MTB 300 days if the environmental window closes with unfulfilled budget.

No arrival fulfillment. No animal-linked seeder. Muscle parasites enter exclusively through the soil → animal → meat chain.

### Mode 2 (Contagion-driven)
- **Environmental exposure** runs continuously.
- **Acausal backstop** — MTB 300 days, `cooldownDays 30`. Very long backstop — this disease should feel like a real environmental hazard, not a constant background drumbeat.
- Storyteller incident cancelled; Mode 2 owns pacing.

**Storyteller seeder cooldown:** 30 days (the longest of any shipped disease).

### What drives the environmental risk

- Soil contamination, not water. Parasite eggs survive in animal faeces deposited on the ground.
- Temperature: moderate cold tolerance (`minTemperature −5°C`) — eggs survive mild frost but die in sustained arctic conditions. Peak in mild weather (`peakTemperature 18°C`).
- Lower water dependency than gut worms (`waterProximityRadius 6`, `waterProximityWeight 0.02`).
- Very strong indoor protection: `indoorReductionPerCellFromEdge 0.20` — animals in roofed barns are almost fully protected. This disease is specifically a grazing-animal disease.

---

## Spread Vectors

### Vector_Foodborne (only spread path)

The only way humans get muscle parasites is eating contaminated meat. No vomiting vector. No proximity or airborne spread.

| Parameter | Value | Notes |
|---|---|---|
| baseChancePerMeal | 0.10 | Slightly higher than gut worms — larvae in meat are dense |
| cleanlinessImpact | 0.5 | Kitchen cleanliness has half the impact compared to gut worms |
| contaminationExpiryDays | 45 | Longer expiry — larvae survive in preserved meat for 45 days |

**Why lower cleanlinessImpact than gut worms?** Muscle parasite larvae are embedded in muscle tissue, not on surfaces. Kitchen cleanliness matters less than whether the meat itself is contaminated.

**Why 45-day expiry?** Trichinella-type cysts are robust. Preserved meat (pemmican, dried meat) can carry live larvae much longer than gut worm eggs. This means stockpiled meat from an infected animal remains a hazard for weeks.

### Vector_Environmental (animal acquisition only)

Parasites exist in contaminated outdoor soil. Grazing animals ingest eggs.

| Parameter | Value | Notes |
|---|---|---|
| baseChancePerCheck | 0.0015 | Per 2500-tick environmental pass |
| minTemperature | −5°C | Eggs survive mild frost; die in sustained arctic cold |
| peakTemperature | 18°C | Cool-to-moderate climate peak |
| waterProximityRadius | 6 | Low water dependency |
| waterProximityWeight | 0.02 | Minimal water effect |
| indoorReductionPerCellFromEdge | **0.20** | Strongest indoor protection — barn housing near-eliminates risk |
| coolRoomThreshold | 8°C | Refrigerated areas reduce risk |

---

## Infectivity

### Active infectivity curve

Muscle parasites peak mid-to-late illness. Food contamination is roughly proportional to parasite load.

| Severity | Multiplier |
|---|---|
| 0.00 | 0.2 |
| 0.30 | 0.6 |
| 0.70 | 1.0 |
| 1.00 | 0.8 |

### Incubation infectivity

None configured.

### Seasonal variation

None configured. The mild-peak temperature profile means arctic-biome colonies get less environmental pressure than temperate ones, but this isn't season-gated directly.

### Source/susceptibility factors

None configured.

---

## Suppression and Caps

| Field | Value | Notes |
|---|---|---|
| maxActiveCases | 4 | |
| spreadSuppressionScale | **0** | Disabled — foodborne is not herd spread |
| outbreakNotification | **FirstCase** | Letter fires on first human case (not animal acquisition) |

Spread suppression is off for the same reason as gut worms: contaminated food can infect any eater regardless of how many are already infected. The colony-fraction model doesn't apply.

`outbreakNotification FirstCase` fires when the first human gets muscle parasites — this is the key discovery moment. Animal infection is silent until the sick signal fires.

---

## Special Mechanics

### Corpse contagiousness (`corpseContagious true`)

Infected animal carcasses spawn rotten. Same mechanism as gut worms: butchering requires the "butcher anyway" override; even then the unified notice roll (`ContagionDiagnosticSkillUtility`, `isAnimalSubject: true`, `isButchery: true`) and meat contamination fire normally.

**Key difference from gut worms:** the 45-day contamination expiry means meat from a muscle-parasite animal that entered the freezer may contaminate colonists weeks after the animal was killed. Long-preserved contaminated pemmican is a delayed hazard.

### Sick signal (`showsSickSignal true`)

Same detection/diagnosis chain as gut worms and plague: `AnimalChat` interaction → Animals skill roll → `Contagion_AnimalSick` → vet diagnosis. Detecting an infected animal before slaughter and either treating it or safely disposing of it is the entire counterplay loop.

### No vomiting

Unlike gut worms, muscle parasites do not cause vomiting — no `Vector_Fomite`. Once infected meat is in the food chain, the spread happens at ingestion. There is no ambient fomite escalation path; the chain terminates when everyone who ate the contaminated meal is exposed.

---

## Counterplay

- **Indoor barn housing** — `indoorReductionPerCellFromEdge 0.20` is the highest of any disease. A roofed barn with 5+ cells to the nearest unroofed cell nearly eliminates soil exposure. This is the strongest single counter and requires no active management.
- **Vet inspection** — handler detection via sick signal is the key lever. A skilled handler (Animals skill 15+) has a ~75% detection chance per interaction. Routine handler routines (training, tending, feeding) will catch most infections before slaughter.
- **"Slaughter and dispose"** — always the safest option. Zero meat, zero risk.
- **Cooking quality** — survival meals (0.05×) and lavish meals (0.10×) reduce contamination from 1.0× to near-zero. Avoid raw meat and pemmican (0.70×) from uncertain sources.
- **Expiry awareness** — contaminated preserved meat stays dangerous for 45 days. A stockpile built from an infected batch remains a hazard well after the animal is dead.
- **Dedicated butcher** — Medical and Cooking skill are the primary levers on the notice roll; Animals adds a small bonus. A pawn with decent Medical + Cooking catches most infected batches before they enter storage.

---

## Tuning Notes

- `baseChancePerCheck 0.0015` is lower than gut worms (0.002). Muscle parasites should feel slightly rarer but more impactful per outbreak. May need adjusting upward if playtesting shows muscle parasites are too infrequent on temperate maps.
- `contaminationExpiryDays 45` is a long window that creates interesting long-memory scenarios (a stockpile from an outbreak months ago). If players find this too harsh or confusing to track, consider 30 days (same as gut worms).
- No arrival seeder. Muscle parasites cannot arrive on incoming pawns in Mode 1 or Mode 2. If narrative justification exists for travellers carrying the disease, an `Seeder_Arrival` with very low `arrivalChance` could be added.
- `spreadSuppressionScale 0` was set because there is no person-to-person spread and therefore the colony-fraction suppression has no meaning. Confirm this is correct: if an infected cook somehow contaminated food, and foodborne were treated as herd spread, suppression would make sense. Current design: no cook vector, only meat chain.
