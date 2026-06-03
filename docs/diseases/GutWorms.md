# Gut Worms — Contagion Profile

Water-borne intestinal parasite entering through contaminated water, fecal contamination, infected animal meat, and unsafe food handling. Animals are usually more exposed because they spend more time outdoors and near water, but humans can catch it directly from the environment too. Chronic low-severity disease; rarely lethal but drains productivity. No person-to-person airborne or proximity spread.

---

## Identity

| Field | Value |
|---|---|
| HediffDef | `GutWorms` |
| Animal variant HediffDef | `Animal_GutWorms` |
| Species | Human + Animal |
| Vanilla incident | `Disease_GutWorms` |
| Target body part | Stomach (human only; animals skip part targeting) |
| Vanilla lethal severity | **None** — gut worms cannot kill directly |
| Vanilla removal | Accumulate 300% total tend quality (`disappearsAtTotalTendQuality 3`); no immunity race |
| Vanilla tend window | 48 h (`baseTendDurationHours 48`); ~3 skilled tends over ~4–6 days clears the disease |
| Contagion immunity | 15 days post-recovery (`immunityDurationDays 15`) |

---

## Vanilla Disease Characteristics

Gut worms is chronic: there is no severity progression and no immunity race. The disease has no lethal threshold — it cannot kill. The only removal mechanism is accumulating 300% total tend quality, which requires a doctor or vet tending the pawn roughly three times over ~4–6 days. Without treatment the disease persists indefinitely.

For animals specifically: they neither die from it nor clear it on their own. Untreated infected animals are permanent reservoirs. `Animal_GutWorms` adds `HediffComp_AnimalNaturalRecovery` so wild animals self-clear in ~15 days and domestic animals in ~25 days without vet attention — but active vet tending remains far faster.

---

## How It Enters the Colony

### Mode 1 (Storyteller-driven)
Storyteller fires `Disease_GutWorms` → pending event created. Unlike most diseases, gut worms resolves via an **environmental window** rather than a carrier seed.

**Fulfillment chain:**
1. **Environmental window** — `Vector_Environmental` runs continuously for up to 14 days (`windowDays 14`) with an infection budget of 2–4 cases. Pawns and animals with outdoor access near water accumulate exposure until the budget is spent or the window closes.
2. **Acausal fallback** — if the 14-day window closes without spending the full budget, any remaining unfulfilled cases resolve via silent incubation on eligible humans or animals. The fallback preserves the human hygiene reduction instead of forcing humans as equal targets.

Storyteller fulfillment stays environmental. Incoming groups do not resolve a storyteller gut-worms event, even though arrivals can carry gut worms in Contagion-driven mode.

### Mode 2 (Contagion-driven)
- **Environmental exposure** runs continuously (same `Vector_Environmental` as Mode 1, just always on rather than window-bounded).
- **Arrival exposure** can seed incoming carriers, especially farm animals and other animal-heavy groups.
- **Successful environmental seeds apply a short environmental cooldown** (`cooldownDays 3`) so one contaminated river check does not make everyone sick at once.
- No acausal backstop. Colonies fully sheltered from environmental exposure can avoid gut worm introductions.
- Storyteller incident cancelled; Mode 2 owns pacing.

**Storyteller seeder cooldown:** 20 days.

### What drives the environmental risk

- Water proximity: bodies of water within `waterProximityRadius 14` cells increase exposure dramatically (`waterProximityWeight 0.08` — the highest of any disease).
- Temperature: eggs require above-freezing water to remain viable (`minTemperature 0°C`) — frozen or icy water suppresses transmission. Peak in moderate warmth (`peakTemperature 22°C`).
- Outdoor vs. indoor: indoor pawns and animals receive `indoorReductionPerCellFromEdge 0.15` per cell of depth from the nearest unroofed cell. A pawn or animal in the centre of a large roofed structure has near-zero exposure; open pastures, outdoor work, and waterside paths have full exposure.
- Human hygiene: humanlike pawns use `humanExposureFactor 0.50`, reflecting better hygiene and less direct contact with contaminated water and feces. This is a reduction, not immunity.

---

## Spread Vectors

### Vector_Foodborne (primary human infection path)

The main way colonists get gut worms is eating contaminated food — either cooked by an infected colonist or made from infected animal meat. At ingestion, this risk is multiplied by the pawn's Contagion food-safety factor: the strong-stomach gene reduces the roll to 10% of normal, while bionic, sterilizing, nuclear, and fleshmass stomachs prevent this contaminated-food roll.

| Parameter | Value | Notes |
|---|---|---|
| baseChancePerMeal | 0.80 | Raw contaminated meat is extremely dangerous |
| cleanlinessImpact | 1.0 | Dirty kitchen amplifies contamination at cooking time |
| contaminationExpiryDays | 30 | Old preserved food becomes safe after 30 days |

**Contamination sources:**
- *Infected cook (Typhoid Mary):* an active gut worms patient cooking a meal stamps contamination proportional to infectivity × kitchen cleanliness, then reduced by the cook's PPE via the foodborne vector's `cookSourceProtection` (airway/hands 50/50, `unsealedEffectiveness` 0.60). A masked-and-gloved cook contaminates little; a sealed-suit cook contaminates ~nothing. The eater takes **no** gear protection — control this upstream (quarantine sick cooks, PPE them, or rely on recipe/skill). See `docs/Apparel_Protection_Design.md` §5.
- *Infected meat:* `Patch_Corpse_ButcherProducts` stamps raw meat from a `corpseContagious` animal (see below).
- *Ingredient propagation:* cooking contaminated raw ingredients propagates contamination to the meal, reduced by recipe factor and Cooking skill. Ordinary simple/fine/lavish meals share a 0.20 recipe factor; higher-tier meals are safer because they require better cooks. Cooking skill applies an asymptotic exponential multiplier: `0.25 + (1.5 - 0.25) * exp(-0.18 * Cooking)`.

### Vector_Fomite (secondary — escalation)

Gut worms causes vomiting. High-severity cases contaminate vomit filth, which other colonists can step on.

| Parameter | Value | Notes |
|---|---|---|
| contaminatesVomit | true | |
| baseChancePerContact | 0.025 | Slightly lower than flu |
| potencyDecayPerHour | 0.08 | Slower decay than flu — gut worm vomit lingers |
| activeInfectivityCurveOverride | (0.50, 0.0) → (0.65, 0.5) → (0.80, 1.0) → (1.00, 0.8) | Peak at severe cases; stays high near lethal |

### Vector_CorpseFluid (very low butchery exposure)

Handling an intact carcass remains safe for gut worms, but cutting open an infected animal can expose the butcher to a small amount of contaminated gut material.

| Parameter | Value | Notes |
|---|---|---|
| pickupChance | 0 | No hauling risk |
| putdownChance | 0 | No hauling risk |
| carriedChancePerCheck | 0 | No transport risk |
| butcherChance | 0.006 | Low direct exposure while butchering |

Butchery exposure is reduced by butcher competence: Cooking is primary, Medicine helps at 25%, and Animals helps at 25% for animal corpses. The factor floors at 45% of base chance.

### Vector_CookingExposure (low cooking exposure)

Cooking contaminated meat can expose the cook through raw ingredient handling. Only Cooking skill modifies this roll: very poor cooks are riskier, while skilled cooks are cleaner and safer.

| Parameter | Value | Notes |
|---|---|---|
| baseChancePerRecipe | 0.003 | Rolled once from the worst contaminated ingredient |
| lowSkillFactor | 2.0 | Cooking 0 doubles exposure risk |
| highSkillFactor | 0.5 | Cooking 20 halves exposure risk |

### Vector_FecalOralLiving (animal-only barn exposure)

Infected animals can contaminate vanilla `Filth_AnimalFilth` in roofed or enclosed barns. Other animals sharing that dirty room roll low ambient exposure; colonists are excluded from this route. Cleaning the filth removes the hazard.

| Parameter | Value | Notes |
|---|---|---|
| baseChancePerCheck | 0.0014 | Per 250-tick transmission pass |
| potencyDecayPerDay | 0.14 | Barn contamination fades slowly unless refreshed |
| roomCleanlinessImpact | 0.6 | Dirty rooms amplify exposure |

### Vector_FecalOralEating (animal-only grazing exposure)

Infected outdoor animals create hidden pasture hotspots. Animals eating in those hotspots can pick up gut worms, with context weighting: grazing live plants is highest risk, raw outdoor ground food is lower, kibble/hay on the ground is lower still, and stored or indoor feed is near-zero risk.

| Parameter | Value | Notes |
|---|---|---|
| baseChancePerIngestion | 0.007 | Rolled when an animal eats near a hotspot |
| hotspotShedChancePerCheck | 0.018 | Per 2500-tick environmental pass from infected outdoor animals |
| hotspotDurationDays | 7 | Shorter-lived than muscle parasite pasture contamination |

### Vector_Environmental (direct outdoor exposure)

This vector can infect humans and animals directly from contaminated outdoor water: drinking from unsafe sources, working at waterside cells, or tracking viable eggs back through outdoor movement. The meat chain is still important, but it is not the only route into humans.

| Parameter | Value | Notes |
|---|---|---|
| baseChancePerCheck | 0.002 | Per 2500-tick environmental pass |
| humanExposureFactor | 0.50 | Humans get a hygiene reduction; animals rely on position/shelter |
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
| useScaledActiveCaseCap | true (default) | Human and animal caps are calculated separately |
| maxActiveCaseChanceOffset | 0 | Cap chance is 30% + 1% per affected colony pawn in that track, floored, max 50% |
| spreadSuppressionScale | **0** | Colony-fraction suppression **disabled** — gut worms is foodborne, not person-to-person herd spread |
| outbreakNotification | **None** | Silent — no letter. Discovery via health tab inspection |

Spread suppression is off because the foodborne vector is not herd-transmission. A dirty kitchen or infected cook can give everyone gut worms regardless of how many are already infected. The colony-fraction model does not fit, though seeding still respects scaled active-case caps.

---

## Special Mechanics

### Corpse contagiousness (`corpseContagious true`)

Animals killed while infected with gut worms spawn a fresh corpse marked by `Comp_InfectedCorpse`. Butcher bills exclude infected corpses by default through the `AllowInfectedCorpses` special filter. If the player enables that filter, raw meat receives full contamination. This is a major human infection path: infected animal → contaminated meat → contaminated meals or raw ingestion → colonist infection.

Eating an infected corpse raw is treated as extreme direct exposure and should almost always transmit to an eligible eater. The intended safety measure is keeping infected corpses out of the food supply entirely.

### Sick signal (`showsSickSignal true`)

Animals incubating gut worms can be detected by handlers via `AnimalChat` interaction (Animals skill / 20 roll). The `Contagion_AnimalSick` hediff is applied on detection and self-clears untreated by day 5 at latest. Diagnosis by a vet uses the unified diagnostic roll (`ContagionDiagnosticSkillUtility`, `isAnimalSubject: true`, `isButchery: false`): Medical primary, Animals at 0.60×, Sight-scaled. A passing roll collapses incubation to mild active disease; a failing roll produces a false negative and starts the diagnosis cooldown before the animal can present sick again.

This mechanic is especially important for gut worms: undetected infected animals go through the butchering chain and contaminate the meat supply. Attentive handlers and skilled vets are the first line of defense.

---

## Counterplay

- **Water management** — humans and animals near rivers or large ponds have much higher environmental exposure. Roofed barns and indoor work areas with no water proximity are effectively safe.
- **Indoor livestock** — a strong counter for the meat-chain path. An animal in the centre of a large roofed barn has near-zero gut worm exposure.
- **Vet inspection** — the sick signal lets a skilled handler catch infected animals before slaughter. High Animals skill is the key lever.
- **Corpse filtering** — leave `AllowInfectedCorpses` disabled on butcher bills unless you deliberately want to process infected carcasses.
- **Butcher skill** — the notice roll in `Patch_Corpse_ButcherProducts` uses Medical as primary and Cooking at 0.60× weight; Animals adds at 0.25× for animal corpses. A skilled butcher-medic or a dedicated cook-handler significantly reduces meat-chain risk.
- **Kitchen hygiene** — infected cooks in dirty kitchens produce more contaminated food. Restricting sick pawns from cooking is the strongest single lever against the food-chain spread.
- **Cook PPE (Typhoid Mary)** — if a sick cook must keep working, food-handling gear cuts both ends: their `cookSourceProtection` reduces contamination baked into meals, and their `Vector_CookingExposure` `apparelProtection` (hands/airway-weighted) reduces the cook contracting it off raw ingredients. A sealed-suit cook is effectively a non-vector.
- **Cooking** — ordinary cooked meals use a shared 0.20 recipe factor before Cooking skill. Survival meals (0.05×) are safer because they are cooked and sealed; pemmican (0.70×) remains risky with contaminated meat.
- **Immunity** — 15-day post-recovery immunity prevents immediate re-infection from the same source.

---

## Tuning Notes

- `baseChancePerCheck 0.002` for environmental exposure is very low per tick but runs every 2500 ticks. The total per-day probability depends heavily on water proximity. May need field testing across different biomes — desert colonies near no water may never naturally acquire gut worms from the environment.
- `Seeder_Arrival arrivalChance 0.006` lets incoming groups carry gut worms in Contagion-driven mode. Farm-animal wander-ins are the clearest carrier story, but any eligible arrival group can technically bring it.
- `Seeder_Environmental cooldownDays 3` intentionally backs off after a successful environmental seed without shutting down the environmental source for a whole season.
- Fomite `potencyDecayPerHour 0.08` gives gut-worm vomit a ~12 h half-life. This means a single vomit event from a severe case contaminates an area for half a day. If cleaning is poor, this can become a significant secondary spread path. Intentional: it rewards keeping sick pawns isolated and areas clean.
- The `outbreakNotification None` setting means players have no alert that gut worms are present. Discovery is organic (health tab, handler detection). This is a design choice — it keeps gut worms as background pressure rather than a crisis event.
- Scaled caps avoid the old problem where a fixed cap of 3 was too tight for large herds. Animals and humans now each use their own population-scaled cap.
