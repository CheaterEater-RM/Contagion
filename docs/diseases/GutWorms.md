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
| Contagion incubation | 2 days |
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

Storyteller fulfillment stays environmental. Incoming groups never carry gut worms — it is a fecal-oral / contaminated-food disease, not something a visiting trader plausibly hands to the colony.

### Mode 2 (Contagion-driven)
- **Environmental exposure** runs continuously (same `Vector_Environmental` as Mode 1, just always on rather than window-bounded).
- **No arrival exposure** — gut worms has no `Seeder_Arrival`. Visitors and traders do not bring it; it enters through the environment (contaminated water / infected meat) and the animal track.
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

### Vector_FecalOralLiving (animal-only indoor exposure)

**This is the sole indoor fecal-oral route.** Infected animals contaminate vanilla `Filth_AnimalFilth` in **enclosed (psychologically-indoor) rooms** — a bare roof over an open shed does *not* count; the cell must sit in a bounded indoor room. Other animals sharing that dirty room roll low ambient exposure; colonists are excluded from this route. Because all indoor risk flows through this cleanable filth, **a clean enclosed room prevents infection entirely** — cleaning the filth removes the hazard, and the abstract eating route (below) does not operate indoors.

| Parameter | Value | Notes |
|---|---|---|
| baseChancePerCheck | 0.0014 | Per transmission pass (default 500 ticks; global cadence, see `ContagionTransmissionTuningDef`) |
| potencyDecayPerDay | 0.14 | Barn contamination fades slowly unless refreshed |
| roomCleanlinessImpact | 0.6 | Dirty rooms amplify exposure |

### Vector_FecalOralEating (animal-only outdoor grazing exposure)

**This is the outdoor-only route.** Infected animals create hidden contamination nodes (the abstract "soil-mixing" model) wherever they feed **outdoors or in roofed-but-open sheds** — no node is minted in an enclosed indoor room (that risk is carried by cleanable filth via the living route above). Animals eating *near* a node (within `hotspotRadius` = 2 cells, steep exponential falloff — not cell-exact) can pick up gut worms, with **food-type** weighting (not roof-based): grazing live plants is highest risk, raw food on the ground lower, prepared feed (kibble/hay) cleaner still, and food kept in a storage building (shelf/feeding item) near-zero — which nudges players toward feeding from a trough/shelf. The route is deliberately **point-blank**: the animal essentially has to feed on the droppings. Cross-cell merging is **off** (`hotspotMergeRadius` 0, kept as a re-enable knob); repeat sheds on the **same cell** instead accumulate (see below), so a fouled latrine builds up.

| Parameter | Value | Notes |
|---|---|---|
| baseChancePerIngestion | 0.25 | Rolled when an animal eats near a node; cow-anchored (see below) |
| incubationInfectivityCurveOverride | (0,0.3) (1,0.6) | Eating-route only: incubating carriers already shed moderately |
| activeInfectivityCurveOverride | (0,0.8) (0.1,1) (1,1) | Eating-route only: active disease sheds near-full almost immediately |
| bodySizeDropsPerDayCurve | (0.2,4) (1.2,2) (2.4,1) (4.0,1) | Deterministic nodes/day by shedder BodySize |
| bodySizePotencyExponent | 1.0 | Per-node potency ×= BodySize^1.0, clamped to [0.4, 2.5] |
| hotspotRadius | 2 | Exposure reaches only two tiles from the node |
| distanceFalloffRate | 0.6931 (ln 2) | Chance halves per cell out |
| hotspotDecayPerDay | 0.5 | Fraction of potency lost per day (×0.5/day); per-disease |
| hotspotDurationDays | 5 | Hard-expiry backstop only — natural decay clears nodes first |

**Cow-anchored point-blank chance.** `baseChancePerIngestion` (0.25) is set so a full-potency node from a cow (BodySize 2.4 → node potency 2.4) fed on point-blank (distance 0, clean map) reads **~60%** (0.25 × 2.4). Body size scales the node, clamped to **[0.4, 2.5]**: the 0.4 floor means small frequent shedders still matter (squirrel BodySize 0.2 → factor 0.4 → ~10% full point-blank, instead of being skipped); the 2.5 ceiling is the cow, so a rhino/elephant reads ~62% rather than climbing past it. The route is **point-blank**: `distanceFalloffRate` ln(2)≈0.6931 halves the chance every cell (full cow ~60% on the node tile, ~30% one cell out, ~15% at two), and `hotspotRadius` 2 cuts it off beyond two tiles. Pen-density outbreaks are held by spread suppression, not by a low per-graze chance. The audit checks these anchors (`gut worms full cow point-blank grazing` = 0.60, `…one cell out` = 0.30, `…large grazer capped at cow`, `…halves per cell`, `…range capped at two tiles`).

**Infectivity ramp.** The eating route uses its **own** infectivity curves (overrides, independent of the foodborne/fomite routes) so it ramps fast and levels out: an incubating carrier sheds at 0.3→0.6 of full, and once the disease is active the node is near-full (0.8→1.0) almost immediately. So a freshly-infected animal already drops transmissible nodes and they climb quickly to the body-size ceiling. Node potency = infectivity × bodySizeFactor; a node below `MinHotspotPotency` (0.08 = ~2% point-blank) is skipped, not spawned.

**Indoor/outdoor split.** The waste meter advances regardless of where the animal stands (gut fullness ignores roofs), but a node is **only minted outdoors** (or in a roofed-but-open shed). When the meter tops out inside an enclosed room, it is consumed *without* dropping a node — indoor contamination is instead represented by cleanable real filth via the living route, so cleaning that room removes the risk. An animal wandering indoor↔outdoor is handled by where it actually is when the meter fills: nodes resume outdoors with no backlog burst on exit. Food-type weighting still applies to whichever outdoor node the next animal eats near: grazing live plants is highest, a clean trough/shelf (storage building) near-zero. A node whose cell is later enclosed by the player goes dormant (eating in an enclosed room never rolls against a node) and decays out naturally.

**Deterministic "waste meter" shedding.** Shedding is **not random**. Each infected animal fills a per-animal/per-disease waste meter at a steady rate set by `bodySizeDropsPerDayCurve` (nodes/day vs `BodySize`), and drops one node when full (remainder carries over, capped so a backlog can't burst). The curve is **level** (~1/day) for large animals and rises for small ones: rat (0.2) ≈ 4/day, deer (1.2) ≈ 2/day, bison/elephant ≈ 1/day. The meter only fills **while the animal isn't starving**. Partial progress persists across save-load and is removed when the animal recovers, dies, or leaves the map.

**Same-cell accumulation.** Cross-cell merging is off, but repeat sheds on the **same cell** stack **additively**: the existing node's potency is first decayed up to the moment of the new shed, then the new shed is added, capped at `MaxNodePotency` (4 — ~100% point-blank at base 0.25). So fouling one spot repeatedly builds a genuinely hot node. Decaying-to-now first stops a tiny shed (a rat) from resetting a big, older node (an elephant pat) for free — the old potency keeps fading on its own schedule; the hard-expiry clock and attributed source species follow whichever shed currently dominates.

**Decay & lifespan.** `hotspotDecayPerDay` is the **fraction of potency lost per day** (daily multiplier = 1 − it), so 0.5 means a node **halves every day**. A node is cleaned up once its potency falls below `MinHotspotPotency` (0.08). A full cow node (2.4) self-expires on ~day 5 (2.4 → 1.2 → 0.6 → 0.3 → 0.15 → 0.075); smaller and most nodes go faster. `hotspotDurationDays` (5) is a hard backstop so even a maxed/stacked cell can't linger near a week. **Weather no longer suppresses nodes** (`rainPotencyFactor`/`freezingPotencyFactor` = 1.0). Cleanup follows time-decay only, weather-independent. A shed does **not** create a trace-graph node — that history is intentionally not retained; live nodes are shown by the selected-animal eating overlay.

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

Low pre-symptom shedding — asymptomatic carriers pass eggs before signs appear, ramping toward the active curve's opening value (0.3). This is required mechanically: `GetContagiousProfiles` filters out incubators whose profile-level infectivity is 0, so without this curve an incubating animal drops no pasture nodes at all. Because it is profile-level (not vector-scoped), it also gives the other vectors (foodborne, fomite) a small incubation-phase chance.

| Severity | Multiplier |
|---|---|
| 0.0 | 0.0 |
| 0.5 | 0.05 |
| 0.85 | 0.15 |
| 1.0 | 0.25 |

### Seasonal introduction pressure

None configured (worm eggs have broad temperature tolerance). Contagion-mode environmental pressure is flat year-round.

### Source/susceptibility factors

None configured.

---

## Suppression and Caps

| Field | Value | Notes |
|---|---|---|
| useScaledActiveCaseCap | true (default) | Colony-human, colony-animal, lord-group, and wild-animal caps are calculated separately |
| maxActiveCaseChanceOffset | 0 | Cap chance is 30% + 1% per affectable pawn in the target scope, floored, max 50% |
| spreadSuppressionScale | **1.0** | Cap-aware suppression **on** for every vector and every scope |
| outbreakNotification | **FirstCase** | Human cases use first-case and cluster letters; hidden animal cases use the sick-signal and diagnosis-letter flow |

Spread suppression is on. Gut worms does spread within a population — vomit fomite between colonists and fecal-oral pasture nodes between animals — so as a target scope (colony humans, colony animals, a non-colony lord group, or **wild animals**) nears its active-case cap, all transmission toward it is dampened. This is applied to every vector (including foodborne, centrally in `BuildSeederChance`), so a dirty kitchen can't push the colony past the cap, a visiting/raiding group self-limits against its own pawns, and a wild outbreak self-limits to ~the wild population's capped fraction rather than saturating the map. The wild-animal cap scales with the wild population currently on the map. Set `suppressionMode` to *Let 'er rip* to disable all caps.

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
- No `Seeder_Arrival`: gut worms is not carried in by visitors or traders. It enters only through the environmental window (contaminated water / infected meat) and the animal track, which keeps the arrival pool focused on the diseases that are actually contagious between people (flu, plague).
- `Seeder_Environmental cooldownDays 3` intentionally backs off after a successful environmental seed without shutting down the environmental source for a whole season.
- Fomite `potencyDecayPerHour 0.08` gives gut-worm vomit a ~12 h half-life. This means a single vomit event from a severe case contaminates an area for half a day. If cleaning is poor, this can become a significant secondary spread path. Intentional: it rewards keeping sick pawns isolated and areas clean.
- `outbreakNotification FirstCase` makes the first visible human case a red, source-attributed outbreak letter; later visible human cases update a yellow cluster letter while the outbreak remains active. Animal acquisition remains hidden until the sick-signal and diagnosis flow reveals it.
- Scaled caps avoid the old problem where a fixed cap of 3 was too tight for large herds. Colony animals and humans each use their own population-scaled cap; spawned non-colony lord groups use a mixed group cap while they remain on the map.
