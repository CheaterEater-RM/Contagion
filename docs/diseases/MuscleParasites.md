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
| contaminationExpiryDays | 30 | Contaminated food becomes safe after 30 days, matching gut worms |

**Why lower cleanlinessImpact than gut worms?** Muscle parasite larvae are embedded in muscle tissue, not on surfaces. Kitchen cleanliness matters less than whether the meat itself is contaminated.

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

### Vector_FecalOralLiving (animal-only indoor exposure)

**This is the sole indoor fecal-oral route.** Infected animals contaminate vanilla `Filth_AnimalFilth` in **enclosed (psychologically-indoor) rooms** — a bare roof over an open shed does *not* count. Other animals sharing that dirty room roll low ambient exposure; colonists are excluded from this route. All indoor risk flows through this cleanable filth, so **a clean enclosed room prevents infection entirely**; the abstract eating route below does not operate indoors.

| Parameter | Value | Notes |
|---|---|---|
| baseChancePerCheck | 0.0010 | Per 250-tick transmission pass |
| potencyDecayPerDay | 0.09 | Longer-lived than gut worm barn contamination |
| roomCleanlinessImpact | 0.5 | Dirty rooms amplify exposure |

### Vector_FecalOralEating (animal-only outdoor grazing exposure)

**This is the outdoor-only route.** Infected animals create hidden contamination nodes (the abstract "soil-mixing" model) wherever they feed **outdoors or in roofed-but-open sheds** — no node is minted in an enclosed indoor room (that risk is carried by cleanable filth via the living route above). Animals eating *near* a node (within `hotspotRadius` = 2 cells, steep exponential falloff — not cell-exact) can pick up muscle parasites, with **food-type** weighting (not roof-based): grazing live plants is highest risk, raw food on the ground lower, prepared feed (kibble/hay) cleaner still, and food in a storage building (shelf/feeding item) near-zero. The route is **point-blank** — the animal essentially has to feed on the droppings. Cross-cell merging is **off** (`hotspotMergeRadius` 0, kept as a re-enable knob); repeat sheds on the **same cell** instead accumulate additively (decay-to-now + add, capped at `MaxNodePotency` 4) so a fouled latrine builds up — see [GutWorms.md](GutWorms.md) → Vector_FecalOralEating → *Same-cell accumulation* for the shared logic.

| Parameter | Value | Notes |
|---|---|---|
| baseChancePerIngestion | 0.25 | Rolled when an animal eats near a node; cow-anchored (same as gut worms) |
| incubationInfectivityCurveOverride | (0,0.3) (1,0.6) | Eating-route only: incubating carriers already shed moderately |
| activeInfectivityCurveOverride | (0,0.8) (0.1,1) (1,1) | Eating-route only: active disease sheds near-full almost immediately |
| bodySizeDropsPerDayCurve | (0.2,4) (1.2,2) (2.4,1) (4.0,1) | Deterministic nodes/day by shedder BodySize (same curve as gut worms) |
| bodySizePotencyExponent | 1.0 | Per-node potency ×= BodySize^1.0, clamped to [0.4, 2.5] |
| hotspotRadius | 2 | Exposure reaches only two tiles from the node |
| distanceFalloffRate | 0.6931 (ln 2) | Chance halves per cell out |
| hotspotDecayPerDay | 0.5 | Fraction of potency lost per day (×0.5/day); per-disease |
| hotspotDurationDays | 5 | Hard-expiry backstop only — natural ×0.5/day decay clears nodes first |

`baseChancePerIngestion` (0.25) is **cow-anchored** exactly like gut worms: a full-potency cow node fed on point-blank reads ~60%, body size scales it within [0.4, 2.5] (small animals floored so they still shed, larger capped at the cow). Weather no longer suppresses nodes (`rain`/`freezingPotencyFactor` = 1.0). See [GutWorms.md](GutWorms.md) → Vector_FecalOralEating for the full anchoring rationale, infectivity ramp, indoor/outdoor shedding, distance-falloff, and decay/lifespan. The audit guards both diseases (`muscle parasites full cow point-blank grazing` = 0.60).

Shedding is the deterministic **waste meter** model — see [GutWorms.md](GutWorms.md) → Vector_FecalOralEating for the full description (steady body-size-driven cadence, paused while starving, drop-when-full, potency tracking infectivity).

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

Low pre-symptom shedding — larvae are passed before the host shows signs, ramping toward the active curve's opening value (0.2) and a touch lower than gut worms. Required mechanically so incubating animals drop pasture nodes (`GetContagiousProfiles` filters out zero-infectivity incubators). Profile-level, so it also gives other vectors a small incubation-phase chance.

| Severity | Multiplier |
|---|---|
| 0.0 | 0.0 |
| 0.6 | 0.04 |
| 0.9 | 0.10 |
| 1.0 | 0.18 |

### Seasonal variation

None configured. The mild-peak temperature profile means arctic-biome colonies get less environmental pressure than temperate ones, but this isn't season-gated directly.

### Source/susceptibility factors

None configured.

---

## Suppression and Caps

| Field | Value | Notes |
|---|---|---|
| useScaledActiveCaseCap | true (default) | Colony-human, colony-animal, and wild-animal caps are calculated separately |
| maxActiveCaseChanceOffset | 0 | Cap chance is 30% + 1% per affectable pawn in that track, floored, max 50% |
| spreadSuppressionScale | **1.0** | Cap-aware suppression **on** for every vector and every track (colony + wild) |
| outbreakNotification | **FirstCase** | Human cases use first-case and cluster letters; hidden animal cases use the sick-signal and diagnosis-letter flow |

Spread suppression is on for the same reasons as gut worms (see [GutWorms.md](GutWorms.md) → Suppression and Caps): muscle parasites spread within a population via fecal-oral pasture nodes, so as a track (colony humans, colony animals, or **wild animals**) nears its active-case cap, all transmission toward it is dampened — applied to every vector centrally in `BuildSeederChance`. The wild-animal cap scales with the wild population on the map, preventing a wild outbreak from saturating it. Set `suppressionMode` to *Let 'er rip* to disable.

`outbreakNotification FirstCase` makes the first visible human case a red, source-attributed outbreak letter; later visible human cases update a yellow cluster letter while the outbreak remains active. Animal infection is hidden until the sick-signal and diagnosis flow reveals it.

---

## Special Mechanics

### Corpse contagiousness (`corpseContagious true`)

Infected animal carcasses spawn fresh and are marked by `Comp_InfectedCorpse`. Butcher bills exclude infected corpses by default through the `AllowInfectedCorpses` special filter. If the player enables that filter, the unified notice roll (`ContagionDiagnosticSkillUtility`, `isAnimalSubject: true`, `isButchery: true`) and meat contamination fire normally.

As with gut worms, contaminated meat and prepared food remain hazardous for up to 30 days.

### Sick signal (`showsSickSignal true`)

Same detection/diagnosis chain as gut worms and plague: `AnimalChat` interaction -> Animals skill roll -> `Contagion_AnimalSick` -> vet diagnosis. The sick signal self-clears untreated by day 5 at latest, and any diagnosis attempt starts the diagnosis cooldown before the animal can present sick again. Detecting an infected animal before slaughter and either treating it or safely disposing of it is the entire counterplay loop.

### No vomiting

Unlike gut worms, muscle parasites do not cause vomiting — no `Vector_Fomite`. Once infected meat or unsafe food handling contaminates food, the spread happens at ingestion. There is no ambient fomite escalation path; the chain terminates when everyone who ate the contaminated meal is exposed.

---

## Counterplay

- **Indoor barn housing** — `indoorReductionPerCellFromEdge 0.20` is the highest of any disease. A roofed barn with 5+ cells to the nearest unroofed cell nearly eliminates soil exposure. This is the strongest single counter and requires no active management.
- **Vet inspection** — handler detection via sick signal is the key lever. A skilled handler (Animals skill 15+) has a ~75% detection chance per interaction. Routine handler routines (training, tending, feeding) will catch most infections before slaughter.
- **Corpse filtering** — leave `AllowInfectedCorpses` disabled on butcher bills unless you deliberately want to process infected carcasses.
- **Cooking quality** — ordinary cooked meals share a 0.20 recipe factor before Cooking skill. Survival meals (0.05×) are safer because they are cooked and sealed. Avoid raw meat and pemmican (0.70×) from uncertain sources.
- **Raw corpse ingestion** — eating an infected corpse raw is treated as extreme direct exposure and should almost always transmit to an eligible eater.
- **Expiry awareness** — contaminated preserved meat stays dangerous for 30 days. A stockpile built from an infected batch remains a hazard well after the animal is dead.
- **Dedicated butcher** — Medical and Cooking skill are the primary levers on the notice roll; Animals adds a small bonus. A pawn with decent Medical + Cooking catches most infected batches before they enter storage.

---

## Tuning Notes

- `baseChancePerCheck 0.0015` is lower than gut worms (0.002). Muscle parasites should feel slightly rarer but more impactful per outbreak. May need adjusting upward if playtesting shows muscle parasites are too infrequent on temperate maps.
- `contaminationExpiryDays 30` matches gut worms so both parasite food chains have the same persistence window.
- `Seeder_Arrival arrivalChance 0.004` lets incoming groups carry muscle parasites in Contagion-driven mode. Farm-animal wander-ins are the clearest carrier story, but any eligible arrival group can technically bring it.
- `Seeder_Environmental cooldownDays 4` intentionally backs off after a successful environmental seed without shutting down the environmental source for a whole season.
- `spreadSuppressionScale 1.0` keeps outbreaks within the mod's active-case caps. Suppression is now applied to every vector (centrally in `BuildSeederChance`), not just person-to-person ones, and covers a dedicated **wild-animal** track so a wild fecal-oral outbreak self-limits to the wild population's capped fraction instead of saturating the map.
