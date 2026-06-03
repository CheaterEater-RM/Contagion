# Plague — Contagion Profile

Flea-borne bacterial disease that crosses freely between humans and animals. One unified disease cluster: `Plague` (human variant, 12 h tend) and `Animal_Plague` (animal variant, 48 h tend) share all transmission logic. Infected animal corpses are dangerous. Handlers are primary initial targets.

---

## Identity

| Field | Value |
|---|---|
| Primary HediffDef | `Plague` |
| Animal variant HediffDef | `Animal_Plague` |
| Species | Human + Animal |
| Vanilla incidents | `Disease_Plague` (human), `Disease_AnimalPlague` (animal) |
| Vanilla lethal severity | 1.0 (both) |
| Human tend cycle | 12 h (`severityPerDayTended −0.3628`) |
| Animal tend cycle | 48 h (`severityPerDayTended −0.4254`) |
| Human immunity | `immunityPerDaySick 0.5224`, `severityPerDayNotImmune 0.666` |
| Animal immunity | `immunityPerDaySick 0.6092`, `severityPerDayNotImmune 0.666` |

### Why two hediffs, one cluster

Animals need a 48 h tend cycle because they rarely receive perfect care. Using the human hediff on animals would force vet attention every 12 h — punishing and unrealistic. The animal variant has the same disease stages and severity progression; only the tending parameters differ. The Contagion engine selects `Animal_Plague` when seeding non-humanlike targets, and `Plague` for humanlike targets, automatically.

---

## Vanilla Disease Characteristics

Plague is serious. Without tending the immunity race is tight: `severityPerDayNotImmune 0.666` against `immunityPerDaySick 0.5224` means severity climbs ~0.14/day even while immune. A pawn hitting the life-threatening stage (0.9+) loses Breathing capacity. Untreated plague kills in roughly 7 days. Tended plague recovers in roughly 5 days.

For animals: the 48 h tend window means a vet must act within 2 days of noticing sickness. Missed tending windows compound quickly. Animals without an attentive vet loop (skilled handler + doctor role) face high mortality.

---

## How It Enters the Colony

### Mode 1 (Storyteller-driven)
Storyteller fires `Disease_Plague` or `Disease_AnimalPlague` → pending event created with a **5-day window** (deliberately tight — see decisions log in DESIGN.md).

**Fulfillment chain:**
1. **Arrival** — next qualifying arriving group carries a capped carrier payload. Incoming humans and incoming animals are both valid carriers now that plague is unified.
2. **Acausal** — if 5 days pass unfulfilled, silent incubation on a random eligible human or animal.

The 5-day window keeps the storyteller's event-spacing meaningful. A long window would let plague collide with raids the storyteller deliberately spaced apart.

### Mode 2 (Contagion-driven)
- **Arrival** — `arrivalChance 0.01` per qualifying group, `cooldownDays 3`. Human caravans, visitors, joiners, raids, and farm-animal wander-ins can all carry plague if their spawned pawns are eligible.
- No acausal backstop. Colonies that avoid infected arrivals and prevent onward cross-species spread are rewarded.
- Storyteller incidents for plague are cancelled in Mode 2; the disease director and arrival seeder own pacing.

**Storyteller seeder cooldown:** 15 days.

### Unified animal/human seeding path

Vanilla `Disease_Plague` and `Disease_AnimalPlague` are both interpreted as unified plague scheduler events. Storyteller mode intercepts either incident into the same pending plague event, then resolves it through incoming carriers or final fallback. Contagion mode cancels both vanilla incidents and relies on Contagion's arrival pipeline.

### Arrival seeding

Raiders, caravan members, and wanderers may arrive already carrying `Plague`. This is the main path for plague arriving on maps with no animals (arctic, toxic fallout, etc.).

---

## Spread Vectors

### Vector_CorpseFlea

Fresh plague corpses get a hidden `Contagion_CorpseFleas` hediff on the inner pawn. The hediff stores flea viability and exposes current flea severity for corpse handling logic.

This vector starts low at death, ramps over the first hours as fleas abandon the cooling host, then fades rapidly. Freezing the corpse kills the flea vector by draining flea viability; thawing does not revive dead fleas.

| Parameter | Value | Notes |
|---|---|---|
| baseChancePerCheck | 0.006 | Ground corpse aura, per 250-tick pass |
| carriedBaseChancePerCheck | 0.025 | Close-contact risk to the pawn carrying the corpse |
| butcherBaseChance | 0.600 | Major close-contact flea roll while cutting the corpse |
| maxRange | 12 | Fresh corpse flea migration path range |
| carriedRange | 4 | Moving path range around a carried corpse |
| distanceFalloffRate | 0.25 | Applied to reachable path distance, not straight-line distance |
| frozenTemperature | 0 C | At or below this, flea viability is destroyed |
| frozenViabilityLossPerDay | 4.0 | About 6 in-game hours to kill a full flea load |
| apparelProtection | skin 0.85, hands/feet/airway 0.05 each; unsealedEffectiveness 0.20 | Target-side. Clothing barely stops a flea (low floor 0.20) — a sealed suit does the work, riding the seal term. A fresh vacsuit/full sealed set is immune (capstone); a ratty incidental suit drops to strong-partial. See `docs/Apparel_Protection_Design.md`. |

| Corpse age (days) | Flea potency |
|---|---|
| 0.00 | 0.10 |
| 0.08 | 0.40 |
| 0.25 | 2.50 |
| 0.75 | 1.50 |
| 1.50 | 0.25 |
| 2.00 | 0.00 |

### Vector_CorpseFluid

Corpse-fluid risk is event/handling based. Pickup and putdown are the main danger moments; carrying has a smaller continuous risk. Unlike fleas, fluid risk does not die just because the corpse is frozen, but freezing slows the normal rot progression that drives the curve.

| Parameter | Value | Notes |
|---|---|---|
| pickupChance | 0.015 | Applied when a pawn starts carrying the corpse |
| putdownChance | 0.015 | Applied when a pawn drops/places the corpse |
| carriedChancePerCheck | 0.003 | Small per-pass risk while carrying |
| butcherChance | 1.000 | Very high fluid exposure while cutting the corpse open |
| apparelProtection | hands 0.45, skin 0.45, airway 0.10; unsealedEffectiveness 0.40 | Target-side. Fabric catches a splash (floor 0.40); bare hands are a real route, so gloves matter most. A sealed suit (with assumed gauntlets) cuts fluid exposure sharply; a full sealed set is immune. See `docs/Apparel_Protection_Design.md`. |

| Corpse age (days) | Fluid potency |
|---|---|
| 0.00 | 0.10 |
| 0.15 | 0.20 |
| 0.50 | 0.45 |
| 1.50 | 0.90 |
| 3.00 | 1.30 |
| 7.00 | 1.50 |
| 12.00 | 0.00 |

Dessicated corpses have no fluid exposure.

### Vector_CookingExposure

Cooking infected meat has a low direct handling risk: splashes, contaminated tools, and bad hygiene around raw meat. This is separate from eating the final meal.

| Parameter | Value | Notes |
|---|---|---|
| baseChancePerRecipe | 0.004 | Rolled once from the worst contaminated ingredient |
| lowSkillFactor | 2.0 | Cooking 0 doubles exposure risk |
| highSkillFactor | 0.5 | Cooking 20 halves exposure risk |

### Vector_Foodborne

Plague can survive into unsafe meat and meals, but ingestion is secondary to butchery and corpse handling. Raw infected meat is dangerous; properly cooked meals are lower risk, especially with a skilled cook. At ingestion, contaminated-food risk is multiplied by the pawn's Contagion food-safety factor, so strong stomachs reduce and protective artificial/mutated stomachs eliminate this meal-based path without affecting corpse-handling flea or fluid exposure.

| Parameter | Value | Notes |
|---|---|---|
| baseChancePerMeal | 0.25 | Raw contaminated meat is a substantial but not parasite-level ingestion risk |
| cleanlinessImpact | 0.5 | Kitchen cleanliness affects contamination from infected cooks |
| contaminationExpiryDays | 15 | Plague contamination in preserved food expires faster than parasites |

Finished meal contamination is reduced by recipe factor and Cooking skill using an asymptotic exponential multiplier.

### Vector_Proximity

Plague's live-host `Vector_Proximity` is flea/contact transfer from an infected carrier. It uses the generic proximity mechanics type, but biologically this is not random near-person spread and not airborne. Airway barriers (breathless gene, gas masks' airway component) have no effect on transmission.

| Parameter | Value | Notes |
|---|---|---|
| baseChancePerCheck | 0.025 | Per 250-tick pass |
| maxRange | 6 | Short reachable path range; requires close contact |
| distanceFalloffRate | 0.35 | Steeper falloff than airborne; matters a lot inside 3 path cells |
| cleanlinessImpact | 1.0 | Filthy areas increase transmission — fleas thrive in debris |
| outdoorFactor | 0.75 | Outdoor spread is still significant (fleas outdoors) |
| outdoorFilthRadius | 4 | Outdoor filth within 4 cells increases transmission |
| maskTargetEffectiveness | 0.4 | Physical barrier (gas mask) reduces flea contact somewhat |
| maskSourceEffectiveness | 0.3 | |
| airwayImmunityFactor | **0** | Gene-based airway immunity (breathless) does nothing — fleas are not inhaled |

### Cross-species transmission

| Parameter | Value | Notes |
|---|---|---|
| crossSpeciesTransmissionFactor | 0.5 | Human-animal live flea transfer at 50% of same-species rate |

An infected animal within 6 reachable path cells of a human rolls at 50% of the base chance (flea transfer still happens, just less efficiently than flea-to-flea). An infected human within reachable path range of livestock similarly spreads at 50%.

---

## Infectivity

### Active infectivity curve

Plague peaks at high severity and tapers near death (too sick to move and shed).

| Severity | Multiplier |
|---|---|
| 0.00 | 0.2 |
| 0.20 | 0.8 |
| 0.60 | 1.0 |
| 0.90 | 0.4 |
| 1.00 | 0.0 |

### Incubation infectivity

Plague spreads during incubation at above-average rates because infected fleas on the carrier jump to nearby pawns independent of host symptom stage. Unlike flu (which ramps steeply only near symptom onset), plague's incubation curve is flat-and-meaningful from day one.

| Incubation progress | Multiplier |
|---|---|
| 0.0 (just infected) | 0.3 |
| 0.5 (mid-incubation) | 0.45 |
| 1.0 (onset) | 0.6 |

The 2.5-day incubation window means an infected pawn can spread plague silently for up to 2.5 days before showing symptoms — longer than flu (1.5 d) or malaria (2.0 d). Combined with a meaningful early infectivity, this gives plague a distinctive "hidden spreader" character that rewards early isolation.

### Source infectivity factors

| Factor | Condition | Effect |
|---|---|---|
| SourceFactor_Trait | Trait `Immunity` degree −1 (Sickly) | ×0.5 source infectivity |

### Corpse-state variation

Corpse fleas are temperature-sensitive even though ordinary live-host plague has no seasonal multiplier. A frozen plague corpse becomes much safer from fleas, but not instantly safe from fluids.

### Seasonal variation

None configured for live-host spread. Corpse flea survival is handled directly from corpse temperature instead.

---

## Suppression and Caps

| Field | Value | Notes |
|---|---|---|
| useScaledActiveCaseCap | true (default) | Human and animal caps are calculated separately |
| maxActiveCaseChanceOffset | 0 | Cap chance is 30% + 1% per affected colony pawn in that track, floored, max 50% |
| spreadSuppressionScale | 1.0 | Normal colony-fraction suppression applies |
| outbreakNotification | FirstCase (default) | |

---

## Special Mechanics

### Corpse contagiousness (`corpseContagious true`)

When a plague-infected animal dies, its corpse spawns **fresh** (not rotted) and is marked by `Comp_InfectedCorpse`, showing "Infected corpse: Plague" in the inspect string and a visual overlay. This lets the player see the risk before deciding what to do.

**Butchering** is controlled entirely by the `AllowInfectedCorpses` job filter on the `ButcherCorpseFlesh` recipe (disabled by default). If the player enables that filter, their pawns will butcher infected corpses.

**Corpse handling**:
- Fleas: spawned infected corpses expose nearby pawns. Carried infected corpses expose the carrier at elevated close-contact risk and create a smaller moving aura.
- Fluids: pickup and putdown roll direct handler exposure. Carrying rolls a smaller continuous exposure.
- Butchering: the butcher gets both a high fluid roll and an extra close flea roll before any discovery/discard outcome.
- Butchery skill mitigation: Cooking is primary, Medicine helps at 25%, and Animals helps at 25% for animal corpses. At high skill this can reduce butchery exposure to 45% of base, but never remove it.
- Early window: both vectors start low at death. Players can haul or freeze fresh bodies quickly before the major flea burst and before fluids become highly infectious.
- Freezing: kills flea viability rapidly. It does not directly sterilize fluids, but it slows rot-driven fluid potency.

**Butchering contamination** (`Patch_Corpse_ButcherProducts`) also stamps plague into raw meat. Eating the corpse raw remains the most dangerous food path because it combines direct corpse ingestion with no cooking reduction.

### Sick signal (`showsSickSignal true`)

Animals incubating plague (hidden `Hediff_ContagionIncubation` with `TargetDiseaseDef Animal_Plague`) can be detected before symptoms appear.

**Detection path 1 — handler interaction:** When a colonist performs an `AnimalChat` interaction (training, tending, feeding), they roll `Animals skill / 20`. On success, `Contagion_AnimalSick` is applied — a visible signal in the animal's health bar. Works for colony animals only (wild animals receive no `AnimalChat` interactions from player colonists).

**Detection path 2 — passive symptom presentation:** Any animal carrying a hidden active disease (`Hediff_ContagionAnimalHiddenDisease`, diagnosed or not) rolls a per-disease curve every half game-day. On success, `Contagion_AnimalSick` is applied and a message fires. This covers wild animals and colony animals whose handlers never noticed. Cumulative probability over a typical untreated course is approximately **25%**.

| Severity | Per-check chance |
|---|---|
| 0.0 | 0% |
| 0.3 | 1% |
| 0.6 | 3% |
| 1.0 | 5% |

**Diagnosis:** When a doctor tends `Contagion_AnimalSick`, the unified diagnostic roll applies (`ContagionDiagnosticSkillUtility`, `isAnimalSubject: true`, `isButchery: false`). Medical primary; Animals at 0.60× (diminishing returns vs Medical); Sight-scaled; Medical Specialist 1.5× bonus. Any diagnosis attempt clears the sick signal and starts the diagnosis cooldown before the animal can present sick again.
- True positive, roll passes: incubation collapses to mild active disease (severity 0.1). Player sees the disease.
- True positive, roll fails: sick signal cleared, disease stays hidden. Animal can be re-detected by handler interaction or passive presentation after the diagnosis cooldown expires.
- False positive (3% rate on healthy animals): sick signal cleared, "nothing concerning" message.

**Auto-slaughter exclusion:** Animals with `Contagion_AnimalSick` are excluded from auto-slaughter queues until the signal is resolved.

---

## Counterplay

- **Handler isolation** — keeping plague-infected animals away from human colonists (separate barn or pasture zone) cuts live flea transfer. Walls and closed doors block the 6-cell path range; open doors allow spread while open.
- **Area restrictions** — sick colonists in a dedicated medical area prevent live flea transfer to healthy colonists.
- **Cleaning** — filthy areas amplify live flea transfer (`cleanlinessImpact 1.0`). Clean floors reduce outbreak spread meaningfully.
- **Freezing corpses** — rapidly kills the corpse-flea vector. Frozen plague bodies are still unpleasant to handle, but they stop shedding migrating fleas. Walls and closed doors block corpse-flea path spread; open doors allow it while open.
- **Penoxycyline** — reduces contract chance via vanilla `DiseaseContractChanceFactor`.
- **Vets** — diagnosed animals go into active disease at low severity (0.1) and can be treated early. A skilled vet with the 48 h window can save most animals.
- **Job filter** — disabling `AllowInfectedCorpses` on the butcher bill (default) prevents infected corpses from entering the meat chain entirely.
- **Sealed suits for corpse handling** — the strongest counterplay against the corpse-flea/fluid and butchery vectors. Their `apparelProtection` rides the *seal*, not coverage: ordinary clothing barely helps against fleas (floor 0.20), but a sealed suit (vacsuit/power armor, with assumed gauntlets) cuts exposure sharply and a full sealed loadout is immune (capstone). Combat armor degrades — keep it out of the ratty zone; the vacsuit's seal is durable. See `docs/Apparel_Protection_Design.md`.
- **No airway immunity** — unlike flu, plague's proximity vector is flea contact, not airway, so a sealed helmet does **not** make a pawn immune to it and breathless does nothing. A mask/helmet still acts as a weak physical barrier (`maskTargetEffectiveness 0.4`), reducing but never eliminating flea-proximity transfer.

---

## Tuning Notes

- `crossSpeciesTransmissionFactor 0.5` is a first-pass value. Plague in real life crosses species very readily via flea vectors — 0.5 may be too conservative. Consider 0.6–0.7 after playtesting.
- Plague no longer uses `Seeder_AnimalLinked`; incoming humans and animals are the primary introduction route. If playtesting shows too little resident animal pressure, add a new explicit animal-reservoir seeder rather than reusing the old handler-biased path.
- Plague uses separate scaled caps for humans and animals. A colony with 10 colonists and 10 animals has a human plague cap of 4 and an animal plague cap of 4, rather than one shared mixed-species pool.
- Live-host plague has no seasonal variation. Corpse fleas already have temperature-based suppression through `Vector_CorpseFlea`.
- Incubation infectivity is set to a flat-ish curve (0.3 → 0.6). If playtesting shows plague outbreaks feel too fast or uncontainable, dial the starting value down toward 0.15–0.2 first; the 2.5-day window amplifies even moderate incubation infectivity.
- Passive symptom presentation peaks at 5% per half-day at severity 1.0, giving ~25% cumulative over a typical untreated course. If wild animal plague feels too invisible, raise the peak toward 0.08; if messages are too noisy, lower it or raise the severity threshold.
- Posthumous symptom chance (10%) is the probability an animal that died with hidden plague shows as an infected corpse. Raise toward 0.3 for diseases with obvious post-mortem lesions; lower toward 0 for truly occult infections.
