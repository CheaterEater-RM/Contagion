# Contagion Implementation Notes

## Purpose

This document records the vanilla RimWorld 1.6 code paths relevant to disease acquisition, immunity, vomiting, food poisoning, and candidate transmission hooks. It is the engineering companion to `DESIGN.md`, not the player-facing feature description.

## Vanilla Disease Acquisition Paths

### Storyteller Disease Selection

`RimWorld.StorytellerComp_Disease.MakeIntervalIncidents(IIncidentTarget target)` is the main storyteller path for random disease events.

Observed behavior:

- gets the current biome from the map or caravan tile
- uses `BiomeDef.diseaseMtbDays`, scaled by storyteller difficulty
- multiplies caravan MTB by 4, which makes caravans less disease-prone than settled maps
- picks a disease incident by `BiomeDef.CommonalityOfDisease(IncidentDef)`

Implication for Contagion:

- vanilla already has a biome-aware disease selector
- malaria and sleeping sickness do not need custom biome gating logic from scratch
- a map-only contagion release must decide what to do with caravan disease incidents explicitly, because vanilla targets caravans too

### Biome Disease Weighting

`RimWorld.BiomeDef.CommonalityOfDisease(IncidentDef diseaseInc)` reads disease commonality from biome data.

Important examples from vanilla data:

- `TemperateForest`: `diseaseMtbDays = 50`, includes flu, plague, malaria, gut worms, animal flu, animal plague
- `TropicalRainforest`: `diseaseMtbDays = 35`, heavily weights malaria and sleeping sickness, also includes flu, plague, gut worms, animal flu, animal plague
- `BorealForest`: `diseaseMtbDays = 60`, includes flu and plague but not malaria or sleeping sickness

Implication for Contagion:

- biome prevalence is already data-driven and should be reused
- environmental disease can use vanilla biome presence as the first gate, then add local map conditions on top

### Human Disease Incident Application

`RimWorld.IncidentWorker_DiseaseHuman` inherits from `IncidentWorker_Disease`.

Observed behavior:

- candidate pool is free colonists and prisoners on maps, or colony pawns on caravans
- actual victims are a random 20-50 percent slice of the candidate count, clamped by `diseaseMaxVictims`
- `IncidentWorker_Disease.ApplyToPawns(...)` calls `pawn.health.immunity.DiseaseContractChanceFactor(...)`
- if the chance roll passes, the worker calls `HediffGiverUtility.TryApply(pawn, def.diseaseIncident, def.diseasePartsToAffect)`

Implication for Contagion:

- vanilla incidents apply the final disease hediff immediately
- a shared interception point exists in `IncidentWorker_Disease.ApplyToPawns`, which is used by both human and animal incidents
- if the mod wants to change victim count or letter text, `ApplyToPawns` alone is not enough; `TryExecuteWorker` behavior also matters

### Animal Disease Incident Application

`RimWorld.IncidentWorker_DiseaseAnimal` also inherits from `IncidentWorker_Disease`, but its victim selection is different.

Observed behavior:

- candidate pool is player-owned non-humanlike animals
- it first picks one target race, weighted by the total body size of animals of that race
- it then infects 30-70 percent of that one species only

Implication for Contagion:

- vanilla already models animal disease as species-isolated outbreaks
- that aligns directly with the design goal of separate animal contagion pools
- human and animal plague or flu should stay separate unless the profile explicitly opts into crossover

### Trait-Driven Disease Seeding

`Verse.Pawn_HealthTracker.HealthTickInterval(int delta)` has an additional disease path unrelated to storyteller intervals.

Observed behavior:

- traits with `randomDiseaseMtbDays` can trigger a random disease human incident
- the code picks a disease human incident weighted by the current biome
- it then calls `((IncidentWorker_Disease)incidentDef.Worker).ApplyToPawns(Gen.YieldSingle(pawn), out blockedInfo)`

Implication for Contagion:

- patching only storyteller selection misses this path
- patching the shared `IncidentWorker_Disease.ApplyToPawns` path catches both normal storyteller disease incidents and trait-based single-pawn disease events

### Food Poisoning Paths

Food poisoning does not use storyteller disease incidents.

Relevant code paths:

- `RimWorld.CompFoodPoisonable.Notify_RecipeProduced(Pawn pawn)` tags food as poisoned due to filthy kitchen or incompetent cook
- `RimWorld.CompFoodPoisonable.PostIngested(Pawn ingester)` applies `FoodPoisoning`
- `RimWorld.CompRottable.PostIngested(Pawn ingester)` applies `FoodPoisoning` for rotten food
- `Verse.Thing.Ingested(Pawn ingester, float nutritionWanted)` can also apply food poisoning for dangerous food types through `FoodPoisonChanceFixedHuman`
- all of those routes funnel into `RimWorld.FoodUtility.AddFoodPoisoningHediff(...)`

Implication for Contagion:

- contagious food poisoning must hook the food system, not storyteller disease incidents
- the cleanest vanilla-compatible pattern is a meal comp that tags food on production and reacts on ingestion

### Wound Infection

`Verse.HediffComp_Infecter.CheckMakeInfection()` is the wound infection path.

Observed behavior:

- infection chance depends on wound severity, tend quality, room infection factor, and storyteller difficulty
- on success it directly adds `WoundInfection` or `ScariaInfection`

Implication for Contagion:

- wound infection is already a separate localized infection system
- it should remain out of scope unless the mod intentionally expands beyond contagious disease

## Vanilla Disease Progression And Prevention

### Immunity And Severity Races

Vanilla disease progression is mostly driven by `HediffComp_Immunizable` and `HediffComp_SeverityPerDay`.

Relevant code:

- `Verse.HediffComp_Immunizable`
- `Verse.ImmunityHandler`
- `Verse.HediffComp_SeverityModifierBase.CompPostTickInterval(...)`

Observed behavior:

- `ImmunityHandler.ImmunityHandlerTickInterval(delta)` advances immunity records every interval tick
- `HediffComp_Immunizable` changes disease severity based on whether immunity is complete
- `HediffDef.PossibleToDevelopImmunityNaturally()` is true only when the hediff has an immunizable comp with positive immunity gain

Implication for Contagion:

- flu, plague, malaria, sleeping sickness, animal flu, and animal plague already have usable vanilla immunity races
- gut worms and food poisoning do not, so temporary reinfection immunity must be mod-owned if those diseases should not loop immediately

### Contract Chance Gating

`Verse.ImmunityHandler.DiseaseContractChanceFactor(HediffDef diseaseDef, out HediffDef immunityCause, BodyPartRecord part = null)` is the central vanilla gate for "can this pawn contract this disease now?"

Observed behavior:

- returns 0 for non-flesh or infection-immune pawns
- returns 0 when a hediff stage or gene grants full immunity through `makeImmuneTo`
- returns 0 when the pawn already has the disease on the relevant part
- returns a partial factor when an immunity record exists

Implication for Contagion:

- contagion rolls should call this method against the real disease def rather than rebuilding penoxy, gene immunity, and duplicate-heddiff checks manually
- this is the best way to stay compatible with vanilla and other mods that use `makeImmuneTo`

### Penoxycyline

Penoxycyline prevention is not encoded as a disease-side boolean.

Relevant data:

- `ThingDef Penoxycyline` applies `HediffDef PenoxycylineHigh`
- `HediffDef PenoxycylineHigh` uses stage `makeImmuneTo = Malaria, SleepingSickness, Plague`

Implication for Contagion:

- the mod should respect penoxycyline through `DiseaseContractChanceFactor(realDiseaseDef)`
- do not create a second hardcoded penoxy list if the real goal is vanilla-compatible prevention

### Vomiting

Vomiting is a generic hediff stage effect.

Relevant code:

- `Verse.Hediff.TickInterval(int delta)` checks `CurStage.vomitMtbDays`
- on trigger it starts `JobDefOf.Vomit`
- `RimWorld.JobDriver_Vomit` spawns `Filth_Vomit` every 150 ticks while the job runs

Implication for Contagion:

- vomit-based fomites can be built on a generic hook
- the mod does not need disease-specific vomit code for every disease that sets `vomitMtbDays`

## Disease-Specific Findings

| Disease | Vanilla source | Natural immunity | Vomit behavior | Implementation note |
|---|---|---|---|---|
| Flu | `Disease_Flu` storyteller incident, `Animal_Flu` for animals | Yes | Major at 0.666, extreme at 0.833 | Human and animal use separate hediff defs; good fit for species-isolated airborne spread |
| Plague | `Disease_Plague` storyteller incident, `Animal_Plague` for animals | Yes | None | Vanilla penoxycyline blocks it; good fit for proximity or cleanliness-driven spread |
| Malaria | `Disease_Malaria` storyteller incident | Yes | Major at 0.78, extreme at 0.91 | Biome-weighted, penoxy-protected, best treated as environmental rather than person-to-person |
| Sleeping sickness | `Disease_SleepingSickness` storyteller incident | Yes | Major at 0.625, extreme at 0.875 and 0.9375 | Tropical-weighted and penoxy-protected; also environmental-first |
| Gut worms | `Disease_GutWorms` storyteller incident | No | Always vomits with MTB 1.0 | Applied to `Stomach` through `IncidentDef.diseasePartsToAffect`; profile must preserve part targeting |
| Food poisoning | Ingestion only through food systems | No | All stages vomit | Not an incident disease; contagious variant must piggyback on ingestion and meal tagging |
| Animal flu | `Disease_AnimalFlu` storyteller incident | Yes | Matches flu stages | Separate hediff def with animal-specific tuning |
| Animal plague | `Disease_AnimalPlague` storyteller incident | Yes | None | Separate hediff def with animal-specific tuning |

Current repo status: the shipped profile XML patches `Flu`, `Animal_Flu`, `Plague`, `Animal_Plague`, `GutWorms`, `Malaria`, and `SleepingSickness` in [Contagion_Profiles.xml](1.6/Patches/Contagion_Profiles.xml).

Additional note:

- the vanilla `DiseaseHuman` category also contains fibrous mechanites, sensory mechanites, muscle parasites, organ decay, blood rot, and paralytic abasia in DLCs
- not every disease incident in that category is a good candidate for contagion

## Vanilla Helpers Worth Reusing

### Hediff Replacement For Incubation

Vanilla already contains a useful pair for incubation wrappers:

- `Verse.HediffComp_SeverityPerDay`
- `Verse.HediffComp_ReplaceHediff`

Why this matters:

- a hidden incubation hediff can advance over time through `SeverityPerDay`
- once a threshold is reached, `ReplaceHediff` can apply the real disease
- `ReplaceHediff` already supports `partsToAffect`, which helps with localized diseases such as gut worms

Design implication:

- a custom `Hediff_Incubation` class may not be necessary for the first implementation
- a mostly data-driven wrapper is viable unless the mod needs extra runtime metadata that XML cannot express cleanly

### Part-Aware Disease Application

`Verse.HediffGiverUtility.TryApply(...)` is the correct helper for applying disease in a way that respects body parts and duplicates.

Why this matters:

- it handles whole-body and part-specific disease application
- it avoids adding duplicate disease to the same part
- it is the same helper used by vanilla disease incidents and hediff replacement

Design implication:

- the contagion system should apply the real disease through this helper, not direct `AddHediff` calls, whenever part targeting might matter

### Recipe Production And Meal Tagging

`Verse.GenRecipe.MakeRecipeProducts(...)` calls `thing.Notify_RecipeProduced(worker)` for each product.

Why this matters:

- a custom meal comp can capture the worker pawn at production time
- this is exactly how `CompFoodPoisonable` injects filthy-kitchen and incompetent-cook poisoning

Design implication:

- contagious meals should use a comp on meal defs and the standard recipe-produced callback
- this avoids job driver rewrites and keeps the design close to vanilla food poisoning

### Filth Creation

`RimWorld.FilthMaker.TryMakeFilth(...)` has overloads that return the created `Filth` via `out Filth outFilth`.

Why this matters:

- contaminated vomit does not need a broad map scan
- a narrow hook can tag the exact filth instance created by vomiting

Design implication:

- contaminated vomit is a reasonable first-pass fomite mechanic

### Room Queries

Relevant room APIs:

- `Room.GetStat(RoomStatDef roomStat)`
- `Room.TouchesMapEdge`
- `Room.PsychologicallyOutdoors`
- `Room.UsesOutdoorTemperature`

Why this matters:

- same-room logic and outdoor logic are not the same thing in vanilla
- `PsychologicallyOutdoors` and `TouchesMapEdge` are both meaningful and should not be collapsed into one boolean
- room cleanliness is already queryable through room stats and already used by vanilla food poisoning logic

Design implication:

- airborne and proximity vectors should use room APIs rather than ad hoc roof checks whenever possible

## Recommended Hook Map

| Concern | Recommended surface | Why this is the best fit | Caveats |
|---|---|---|---|
| Convert storyteller disease into incubation | `IncidentWorker_Disease.ApplyToPawns` | Shared path for human incidents, animal incidents, and trait-driven disease events | If victim count or letter text must change, patching only `ApplyToPawns` is not enough |
| Rework storyteller disease frequency or disease choice | `StorytellerComp_Disease.MakeIntervalIncidents` | Vanilla already handles biome weighting and target type selection here | This is a broader patch surface than simple disease conversion |
| Airborne, proximity, and environmental spread | `MapComponent` tick | These vectors are map-state driven, not event driven | Must define caravan behavior explicitly because this only covers maps |
| Social spread | postfix on `Pawn_InteractionsTracker.TryInteractWith` when the interaction succeeds | Central successful social interaction callback | Runs often; keep logic narrow and cheap |
| Arrival seeding — neutral groups | postfix on `IncidentWorker_NeutralGroup.SpawnPawns` | Returns `List<Pawn>` covering visitors, travelers, trade caravans, skylantern wanderers, tribute collectors via the shared base | None — pawns are `Spawned` by the time the postfix runs |
| Arrival seeding — wanderer joins | postfix on `IncidentWorker_WandererJoin.SpawnJoiner(Map, Pawn)` | Clean `virtual` method; the `Pawn` is `GenSpawn.Spawn`-ed inside it, so the postfix sees a spawned pawn | None |
| Arrival seeding — quest pawns (incl. refugees) | postfix on `QuestPart_PawnsArrive.Notify_QuestSignalReceived` | In 1.6, refugees and most named guests arrive via the quest system, not dedicated incident workers; pawns live in the public `pawns` field | Drop-pod arrival mode leaves pawns inside the incoming pod (not `Spawned`); `SeedArrivals` skips them. Walk-in mode is fully covered |
| Food contamination at cook time | meal comp `Notify_RecipeProduced` | Exact vanilla pattern used by `CompFoodPoisonable` | Requires XML comp injection onto meal defs |
| Food contamination at ingest time | meal comp `PostIngested` or a narrow `Thing.Ingested` hook | Central ingestion path with clear ingester context | Broad `Thing.Ingested` patches need careful filtering |
| Vomit contamination | narrow hook around `JobDriver_Vomit` or `FilthMaker.TryMakeFilth(..., out Filth)` | Vanilla already creates visible `Filth_Vomit` here | `JobDriver_Vomit` is generic, so filter by contagious disease on the pawn |
| Lovin transmission | no clean dedicated callback; likely custom handling around `JobDriver_Lovin` completion | The owning code path is known | This is the least clean planned hook and should be deferred unless a concrete disease needs it |

## Design Constraints Confirmed By Source

### Part-Target Metadata Is Mandatory

Gut worms proves that a `TransmissionProfile` on `HediffDef` alone is insufficient. Vanilla uses `IncidentDef.diseasePartsToAffect` to place the disease on the stomach.

Required design response:

- either copy part-target data into the profile
- or derive it from a linked incident definition before applying the real disease

### Temporary Immunity Cannot Be Fully Delegated To Vanilla

Only immunizable diseases participate in the natural immunity system. Gut worms and food poisoning do not.

Required design response:

- reinfection suppression must be a mod-owned layer for diseases that lack natural immunity

### Animal Disease Should Stay Split

Vanilla does not reuse human flu and plague for animals. It has separate incident defs and separate animal hediff defs.

Required design response:

- keep species-isolated contagion pools by default
- profile authoring should happen per real disease def, including animal variants

### Caravan Support Is A Deliberate Scope Decision

Vanilla storyteller disease targets caravans as well as maps.

Required design response:

- either leave caravan disease vanilla for v1
- or design a world/caravan transmission model explicitly
- do not accidentally imply caravan support through a map-only transmission engine

## Suggested Implementation Order

1. Build the data model: `TransmissionProfile`, optional part-target metadata, and disease lookup helpers.
2. Implement hidden incubation and temporary immunity using the lightest viable hediff-based approach.
3. Intercept `IncidentWorker_Disease.ApplyToPawns` so storyteller and trait disease seeding route through incubation.
4. Add the map transmission engine for airborne, proximity, and environmental vectors.
5. Add meal and vomit contamination using comp-based hooks.
6. Add social spread through `Pawn_InteractionsTracker.TryInteractWith`.
7. Leave lovin and caravan contagion for a later pass unless a concrete requirement demands them.

## Practical Conclusions

- Vanilla already provides most of the disease simulation primitives needed for Contagion.
- The mod does not need to replace disease severity, immunity, vomiting, cooking, or filth systems.
- The real work is redirecting disease acquisition into incubation, then layering map-driven transmission on top.
- The two design details that must be handled explicitly are part-targeted diseases and temporary immunity for non-immunizable diseases.