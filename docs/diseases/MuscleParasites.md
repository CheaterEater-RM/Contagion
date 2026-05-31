# Muscle Parasites — Contagion Profile

Trichinella-type larvae contracted from raw or undercooked infected meat, contaminated outdoor soil, fecal contamination, grazing environments, and unsafe food handling. No person-to-person airborne or proximity spread, no vomiting. The main colony chain is still environment → outdoor animal → butchered meat → human, but humans can also catch it directly from sustained outdoor environmental exposure or contaminated food. Longer incubation than gut worms, longer immunity.

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
1. **Environmental window** — `Vector_Environmental` runs for up to 14 days. Infection budget 1–3 cases. Outdoor grazing animals and humans working through contaminated soil accumulate exposure.
2. **Acausal fallback** — if the 14-day window closes with unfulfilled budget, remaining cases resolve via silent incubation on eligible humans or animals. The fallback preserves the human hygiene reduction instead of forcing humans as equal targets.

Storyteller fulfillment stays environmental. Incoming groups do not resolve a storyteller muscle-parasites event, even though arrivals can carry muscle parasites in Contagion-driven mode.

### Mode 2 (Contagion-driven)
- **Environmental exposure** runs continuously.
- **Arrival exposure** can seed incoming carriers, especially farm animals and other animal-heavy groups.
- **Successful environmental seeds apply a short environmental cooldown** (`cooldownDays 4`) so one contaminated-soil check does not make everyone sick at once.
- No acausal backstop. Colonies that keep humans and animals out of contaminated outdoor exposure can avoid muscle parasite introductions.
- Storyteller incident cancelled; Mode 2 owns pacing.

**Storyteller seeder cooldown:** 30 days (the longest of any shipped disease).

### What drives the environmental risk

- Soil contamination, not water. Parasite eggs survive in animal faeces deposited on the ground.
- Temperature: moderate cold tolerance (`minTemperature −5°C`) — eggs survive mild frost but die in sustained arctic conditions. Peak in mild weather (`peakTemperature 18°C`).
- Lower water dependency than gut worms (`waterProximityRadius 6`, `waterProximityWeight 0.02`).
- Very strong indoor protection: `indoorReductionPerCellFromEdge 0.20` — animals in roofed barns are almost fully protected. This disease is specifically a grazing-animal disease.
- Human hygiene: humanlike pawns use `humanExposureFactor 0.45`, reflecting better hygiene and less direct contact with contaminated soil and feces. This is a reduction, not immunity.

---

## Spread Vectors

### Vector_Foodborne (primary contaminated-food path)

Humans most commonly get muscle parasites by eating contaminated meat, but sustained outdoor environmental exposure can also seed direct cases. Active human cases can contaminate prepared food through unsafe food handling, just like gut worms, though there is no vomiting vector and no proximity or airborne spread. At ingestion, contaminated-food risk is multiplied by the pawn's Contagion food-safety factor: the strong-stomach gene reduces the roll to 10% of normal, while protective artificial/mutated stomachs eliminate this roll.

| Parameter | Value | Notes |
|---|---|---|
| baseChancePerMeal | 0.85 | Slightly higher than gut worms — larvae in meat are dense |
| cleanlinessImpact | 0.5 | Kitchen cleanliness has half the impact compared to gut worms |
| contaminationExpiryDays | 45 | Longer expiry — larvae survive in preserved meat for 45 days |

**Why lower cleanlinessImpact than gut worms?** Muscle parasite larvae are embedded in muscle tissue, not on surfaces. Kitchen cleanliness matters less than whether the meat itself is contaminated.

**Why 45-day expiry?** Trichinella-type cysts are robust. Preserved meat (pemmican, dried meat) can carry live larvae much longer than gut worm eggs. This means stockpiled meat from an infected animal remains a hazard for weeks.

Cooking contaminated ingredients propagates surviving contamination to the meal using the recipe factor and Cooking skill. Ordinary simple/fine/lavish meals share a 0.20 recipe factor; higher-tier meals are safer because they require better cooks. Cooking skill applies an asymptotic exponential multiplier: `0.25 + (1.5 - 0.25) * exp(-0.18 * Cooking)`.

### Vector_CorpseFluid (low butchery exposure)

Muscle parasites are mainly an ingestion hazard, but butchering infected tissue is not perfectly safe. Normal hauling has no direct exposure.

| Parameter | Value | Notes |
|---|---|---|
| pickupChance | 0 | No hauling risk |
| putdownChance | 0 | No hauling risk |
| carriedChancePerCheck | 0 | No transport risk |
| butcherChance | 0.008 | Low direct exposure while butchering |

Butchery exposure is reduced by butcher competence: Cooking is primary, Medicine helps at 25%, and Animals helps at 25% for animal corpses. The factor floors at 45% of base chance.

### Vector_CookingExposure (low cooking exposure)

Cooking contaminated meat can expose the cook through raw ingredient handling. Only Cooking skill modifies this roll: very poor cooks are riskier, while skilled cooks are cleaner and safer.

| Parameter | Value | Notes |
|---|---|---|
| baseChancePerRecipe | 0.004 | Rolled once from the worst contaminated ingredient |
| lowSkillFactor | 2.0 | Cooking 0 doubles exposure risk |
| highSkillFactor | 0.5 | Cooking 20 halves exposure risk |

### Vector_Environmental (direct outdoor exposure)

Parasites exist in contaminated outdoor soil. Grazing animals ingest eggs, and humans can be exposed by sustained outdoor work or travel through contaminated ground.

| Parameter | Value | Notes |
|---|---|---|
| baseChancePerCheck | 0.0015 | Per 2500-tick environmental pass |
| humanExposureFactor | 0.45 | Humans get a hygiene reduction; animals rely on position/shelter |
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

Infected animal carcasses spawn fresh and are marked by `Comp_InfectedCorpse`. Butcher bills exclude infected corpses by default through the `AllowInfectedCorpses` special filter. If the player enables that filter, the unified notice roll (`ContagionDiagnosticSkillUtility`, `isAnimalSubject: true`, `isButchery: true`) and meat contamination fire normally.

**Key difference from gut worms:** the 45-day contamination expiry means meat from a muscle-parasite animal that entered the freezer may contaminate colonists weeks after the animal was killed. Long-preserved contaminated pemmican is a delayed hazard.

### Sick signal (`showsSickSignal true`)

Same detection/diagnosis chain as gut worms and plague: `AnimalChat` interaction → Animals skill roll → `Contagion_AnimalSick` → vet diagnosis. Detecting an infected animal before slaughter and either treating it or safely disposing of it is the entire counterplay loop.

### No vomiting

Unlike gut worms, muscle parasites do not cause vomiting — no `Vector_Fomite`. Once infected meat or unsafe food handling contaminates food, the spread happens at ingestion. There is no ambient fomite escalation path; the chain terminates when everyone who ate the contaminated meal is exposed.

---

## Counterplay

- **Indoor barn housing** — `indoorReductionPerCellFromEdge 0.20` is the highest of any disease. A roofed barn with 5+ cells to the nearest unroofed cell nearly eliminates soil exposure. This is the strongest single counter and requires no active management.
- **Vet inspection** — handler detection via sick signal is the key lever. A skilled handler (Animals skill 15+) has a ~75% detection chance per interaction. Routine handler routines (training, tending, feeding) will catch most infections before slaughter.
- **Corpse filtering** — leave `AllowInfectedCorpses` disabled on butcher bills unless you deliberately want to process infected carcasses.
- **Cooking quality** — ordinary cooked meals share a 0.20 recipe factor before Cooking skill. Survival meals (0.05×) are safer because they are cooked and sealed. Avoid raw meat and pemmican (0.70×) from uncertain sources.
- **Raw corpse ingestion** — eating an infected corpse raw is treated as extreme direct exposure and should almost always transmit to an eligible eater.
- **Expiry awareness** — contaminated preserved meat stays dangerous for 45 days. A stockpile built from an infected batch remains a hazard well after the animal is dead.
- **Dedicated butcher** — Medical and Cooking skill are the primary levers on the notice roll; Animals adds a small bonus. A pawn with decent Medical + Cooking catches most infected batches before they enter storage.

---

## Tuning Notes

- `baseChancePerCheck 0.0015` is lower than gut worms (0.002). Muscle parasites should feel slightly rarer but more impactful per outbreak. May need adjusting upward if playtesting shows muscle parasites are too infrequent on temperate maps.
- `contaminationExpiryDays 45` is a long window that creates interesting long-memory scenarios (a stockpile from an outbreak months ago). If players find this too harsh or confusing to track, consider 30 days (same as gut worms).
- `Seeder_Arrival arrivalChance 0.004` lets incoming groups carry muscle parasites in Contagion-driven mode. Farm-animal wander-ins are the clearest carrier story, but any eligible arrival group can technically bring it.
- `Seeder_Environmental cooldownDays 4` intentionally backs off after a successful environmental seed without shutting down the environmental source for a whole season.
- `spreadSuppressionScale 0` was set because foodborne/environmental pressure is not person-to-person herd spread. A dirty kitchen, contaminated meat batch, or contaminated soil patch can expose multiple pawns regardless of colony infection fraction.
