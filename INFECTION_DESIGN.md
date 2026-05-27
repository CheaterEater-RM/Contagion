# Infection — Disease Contagion Mod for RimWorld 1.6
*Design Document v3 — May 2026*

---

## Design Philosophy

**Mechanisms, not content.** No new zones, items, buildings, research projects, or UI panels. The mod adds *behavior* to existing systems: diseases spread through existing social/proximity/room mechanics, quarantine happens through existing area restrictions and medical rest, and counterplay uses existing vanilla tools.

**Vanilla-compatible disease definitions.** Diseases remain `HediffDef`s with vanilla severity/immunity races. The mod adds a `DefModExtension` (`TransmissionProfile`) that tags a disease with its transmission behavior. This is the only modder-facing API — any modder can make any hediff contagious via XML alone.

---

## Goals

- Diseases spread through believable mechanisms using existing game systems
- Players learn to quarantine through natural consequences, not tutorials or new UI
- Existing vanilla tools (area restrictions, medical beds, cleaning, penoxycyline) become the counterplay
- Colony layout decisions (hospital placement, bedroom separation, kitchen location) gain new weight
- Modder-extensible: adding a new contagious disease requires only XML, no C#

## Anti-Goals

- No new zones, items, buildings, research projects, or UI elements
- No new player-facing concepts to learn
- No animal↔human cross-species transmission
- No changes to wound infection, non-infectious conditions, or mechanites
- No micromanagement — a player who never learns about the mod should survive most outbreaks through normal play (sick pawn goes to bed, doctor tends them, colony carries on)

---

## Diseases In Scope

### Modified (contagion added)

| Disease | Primary Vector | Secondary Vector | Seeding |
|---|---|---|---|
| Flu | Airborne | Social (boosted airborne) | Visitors / acausal |
| Plague | Proximity + cleanliness | — | Animal seeding |
| Malaria | Environmental (mosquito) | — | Biome-persistent |
| Sleeping Sickness | Environmental (mosquito) | — | Biome-persistent |
| Gut Worms | Foodborne | — | Acausal / dirty kitchens |
| Food Poisoning | Foodborne / Fomite | — | Existing vanilla causes + contagious variant |

### Untouched (stays vanilla)

- Wound infection
- Sensory mechanites, fibrous mechanites, muscle parasites
- Carcinoma, bad back, dementia, cataracts, asthma, frailty, all non-infectious conditions
- Blood rot, lung rot, organ decay (DLC conditions — mechanism doesn't fit contagion)
- Paralytic abasia

### Animal diseases

Animals get flu and plague through their own species-isolated contagion pools. An animal with flu spreads flu to nearby animals; it never crosses to humans. Same mechanics, separate populations.

---

## Core Architecture

### TransmissionProfile (DefModExtension)

The entire modder-facing contract. Attached to any `HediffDef` to make it contagious:

```xml
<modExtensions>
  <li Class="Infection.TransmissionProfile">
    <!-- Contagious window (severity range of the REAL hediff, not incubation) -->
    <contagiousMinSeverity>0.05</contagiousMinSeverity>
    <contagiousMaxSeverity>0.80</contagiousMaxSeverity>

    <!-- Also contagious during incubation? -->
    <contagiousDuringIncubation>true</contagiousDuringIncubation>

    <!-- Spread vectors (composable list) -->
    <vectors>
      <li Class="Infection.Vector_Airborne">
        <baseChancePerCheck>0.03</baseChancePerCheck>
        <outdoorFactor>0.15</outdoorFactor>
        <maxRange>15</maxRange>
      </li>
    </vectors>

    <!-- Seeding mechanisms -->
    <seeders>
      <li Class="Infection.Seeder_Visitor" />
    </seeders>

    <!-- Post-recovery immunity -->
    <immunityDurationDays>15</immunityDurationDays>

    <!-- Hidden phase before vanilla hediff appears -->
    <incubationDays>1.5</incubationDays>

    <!-- Species scope -->
    <affectsHumans>true</affectsHumans>
    <affectsAnimals>true</affectsAnimals>
    <crossSpeciesTransmission>false</crossSpeciesTransmission>
  </li>
</modExtensions>
```

### Disease Lifecycle

Every contagious disease follows five phases. Phases 2–4 wrap the vanilla hediff unchanged:

1. **Incubation** — Separate lightweight hediff (`Hediff_Incubation`). Hidden from health tab (or shown as "???" at low medical skill?). May be contagious per profile. No symptoms. On completion, removes itself and applies the real vanilla disease hediff.
2. **Prodromal** — Early vanilla severity (minor stage). Contagious. Player gets the vanilla notification that the pawn is sick. This is the quarantine window.
3. **Active** — Full vanilla severity progression, symptoms, treatment, immunity race. Contagious.
4. **Recovery** — Vanilla immunity won. Severity declining. Contagiousness drops.
5. **Immune** — A hidden `Hediff_Immunity` (or tracked in a MapComponent) lasting `immunityDurationDays`. Prevents reinfection. Prevents infinite reinfection loops.

Phases 2–4 are not new states — they're the existing vanilla hediff progression. The mod only adds phase 1 (incubation wrapper) and phase 5 (immunity tracking).

### Transmission Tick

A `MapComponent` runs the transmission engine. Every N ticks (tunable, default 250):

1. Collect all contagious pawns on the map (pawns with a disease whose `TransmissionProfile` says they're currently contagious based on severity range, or pawns in incubation for profiles with `contagiousDuringIncubation`).
2. For each contagious pawn, iterate their `TransmissionProfile.vectors`.
3. Each vector evaluates its own spread logic (distance checks, context checks, etc.) and returns a set of candidate targets with per-target transmission chances.
4. Roll for each candidate. On success, apply `Hediff_Incubation` for that disease.
5. Skip targets who: already have this disease, are incubating it, have active immunity, or are on penoxycyline (for applicable diseases).

Performance note: The outer loop is O(contagious pawns), which is small. The inner loop per vector depends on the vector type — airborne does a spatial query, social piggybacks on interaction events (amortized), environmental runs per-pawn against map conditions. Colony sizes of 15–30 pawns make this trivial.

---

## Spread Vectors — Detailed Design

### Vector_Airborne

The primary respiratory disease vector. Distance-based with indoor amplification.

**Per-check logic (runs on the transmission tick):**
1. Get all pawns within `maxRange` cells of the contagious source pawn.
2. For each candidate, calculate transmission chance:
   - Start with `baseChancePerCheck`
   - Multiply by distance falloff: `1.0 / (1.0 + distance * distanceFalloffRate)` — smooth curve, not a hard cutoff. At distance 1 (adjacent), nearly full chance. At distance 10, much reduced. At `maxRange`, nearly zero but not impossible.
   - Apply indoor/outdoor modifier:
     - If source and target are **both indoors in the same enclosed room**: full chance (1.0×). Walls and doors contain aerosols.
     - If either is **outdoors** (room `TouchesMapEdge` or unroofed): multiply by `outdoorFactor` (default 0.15). Open air disperses particles.
     - If they're in **different enclosed rooms**: multiply by 0. Walls block airborne. (Doors as barriers — a closed door between rooms blocks; an open doorway technically merges rooms in vanilla's room system, which is correct behavior here.)
   - Apply severity modifier: higher severity = more contagious (linear scale from `contagiousMinSeverity` to peak at mid-severity).
3. Roll against final chance.

**Key properties:**
- `baseChancePerCheck` — base probability per transmission tick
- `outdoorFactor` — multiplier when outdoors (0.0–1.0)
- `maxRange` — maximum cell distance to consider (hard cutoff for performance)
- `distanceFalloffRate` — controls the steepness of the distance curve

**Why distance, not room size:** A 3-cell-wide, 40-cell-long hallway is a "room" but shouldn't transmit like a 40-cell bedroom. Distance handles this naturally — the pawn at one end of the hallway is 30 cells from the pawn at the other end, so transmission chance is negligible despite sharing a "room."

### Vector_Social

Not a separate transmission system — a **booster on airborne** that fires during social interactions.

**Trigger:** Hooks into the social interaction system. When two pawns perform a social interaction (chat, deep talk, slight, insult, etc.) and one is contagious with a disease that has this vector, run an additional airborne-style check with boosted parameters.

**Per-interaction logic:**
1. Check if either pawn is contagious.
2. Roll `baseChancePerInteraction`, modified by the same indoor/outdoor factor as airborne.
3. Distance is implicitly close (social interactions happen face-to-face), so no distance falloff — use the base chance directly.

**Key properties:**
- `baseChancePerInteraction` — flat chance per social interaction
- `outdoorFactor` — same concept as airborne

**Why it's a separate vector class despite being "boosted airborne":** Extensibility. A future modder might want a disease that spreads *only* through social contact (STDs via `Vector_Lovin`, contact diseases via social-only). Keeping it as a composable vector means "airborne + social" is flu, "social only" is something else, "airborne only" is yet another option. The composition is the API.

### Vector_Fomite

Contaminated filth transmits disease on contact. Scoped to **vomit only** — no invisible contamination.

**Mechanism:**
1. When a contagious pawn vomits (vanilla vomit event), tag the resulting filth as contaminated via a `ThingComp`. Track which disease and remaining "potency" (decays over time or on contact).
2. When any pawn walks over contaminated filth, roll `baseChancePerContact` modified by potency.
3. Filth cleaned normally by haulers/cleaners — cleaning removes the contamination.

**Key properties:**
- `contaminatesVomit` — whether this disease's vomit is infectious (bool)
- `baseChancePerContact` — chance per step on contaminated filth
- `potencyDecayPerHour` — how fast contamination weakens (so old vomit is less dangerous)

**Scope:** Flu and plague both cause vomiting at higher severity (confirmed in vanilla). This means fomites activate as a secondary vector during severe cases that weren't quarantined early — it's a "you let this get out of hand" escalation, not a primary spread mechanism.

**Counterplay:** Clean your base. Home zone auto-cleaning already does this. Dedicated cleaners become more valuable during outbreaks.

### Vector_Environmental

Ambient environmental transmission — mosquitoes for malaria/sleeping sickness.

**Per-check logic (runs on the transmission tick, per pawn):**
1. Skip pawns who are immune or on penoxycyline.
2. Calculate base chance from map conditions:
   - **Temperature factor:** Scales with outdoor temperature. Below a threshold (e.g. 15°C), zero. Rises to peak at ~30°C+. Mosquitoes need warmth.
   - **Time-of-day factor:** Peaks at dawn (~5–7h) and dusk (~17–19h). Reduced midday and overnight. Simple sine-derived curve.
   - **Water proximity factor:** Count marsh/shallow water cells within ~10 cells of the pawn. More water = higher factor.
3. Apply indoor modifier:
   - **Outdoors (unroofed):** Full chance.
   - **Indoors:** Reduced based on distance from the nearest outdoor/unroofed cell. A bedroom on the colony edge gets some mosquito pressure; a bedroom deep inside the base gets very little. This models mosquitoes entering through nearby openings.
   - **Temperature indoors:** If the room is actively cooled (AC), further reduction. Cool rooms repel mosquitoes. This ties into existing HVAC decisions — AC already has gameplay value, this adds another reason.
4. Roll against final chance.

**Key properties:**
- `baseChancePerCheck` — base environmental transmission rate
- `minTemperature` — below this, no transmission
- `peakTemperature` — above this, maximum temperature factor
- `waterProximityRadius` — radius to search for water cells
- `waterProximityWeight` — how much each water cell contributes
- `indoorReductionPerCellFromEdge` — how much each cell of depth into the base reduces chance
- `coolRoomThreshold` — temperature below which "AC bonus" applies

**No mosquito zones, no overlays.** Players can see water, see temperature, see the season. The mod makes those existing visible features mechanically meaningful.

**Biome gating:** Malaria and sleeping sickness already have biome restrictions in vanilla. The seeder checks biome before activating. Tropical rainforest and swamp are high-risk; temperate forest is moderate; arid/cold biomes are minimal or zero.

### Vector_Proximity

Cleanliness-gated proximity transmission — the "flea" vector for plague, generalized.

No flea hediff. Fleas (or lice, or other contact parasites) are assumed to be ambient, modulated by cleanliness. This vector checks physical proximity between pawns (or animals, within species) and rolls for transmission based on distance and environmental cleanliness.

**Per-check logic:**
1. Get all same-species pawns within `maxRange` cells of the contagious pawn.
2. For each candidate, calculate chance:
   - Start with `baseChancePerCheck`
   - Apply distance falloff (same formula as airborne)
   - Apply cleanliness modifier: the filth level of the room or area modifies transmission. A clean hospital reduces chance; a filthy barn amplifies it. Use the existing room cleanliness stat (`Room.GetStat(RoomStatDefOf.Cleanliness)`), with a fallback for outdoors/no-room situations.
   - Apply roofed modifier: unroofed areas have slightly lower flea/contact transmission (wind, UV, dispersal).
3. Roll against final chance.

**Key properties:**
- `baseChancePerCheck` — base probability
- `maxRange` — proximity radius
- `distanceFalloffRate` — curve steepness
- `cleanlinessImpact` — how strongly room cleanliness modifies the chance (a multiplier curve: very clean rooms → 0.2×, neutral → 1.0×, filthy → 2.0×)

**For plague:** Plague uses this vector with `affectsAnimals=true`, `crossSpeciesTransmission=false`. Animals spread plague to other animals via proximity in dirty conditions. Humans spread plague to humans the same way — but since plague is seeded through animals, human plague starts when a handler catches it from... wait, we said no cross-species. Let me reconsider.

**Plague seeding problem:** If animals can't transmit to humans, how does plague reach humans? Options:
- **Option A:** Plague seeds directly onto one human (acausal) and spreads human→human via proximity. Animals get their own independent plague outbreaks. Narratively weaker ("a colonist just... got plague") but mechanically clean.
- **Option B:** Allow a one-directional animal→human seeding exception for plague specifically, gated to handlers during tending/feeding. Not "cross-species transmission" in the ongoing sense — it's a seeding event, like how visitors seed flu. The animal is the *source*, but ongoing human→human spread is proximity-based.
- **Option C:** The plague seeder targets a human directly, but the narrative framing is "contracted from animal contact." Mechanically identical to Option A but with flavor text.

**Recommendation:** Option C. Cleanest implementation, no cross-species transmission system needed, narrative still works. The seeder fires when flea-prone animals are on the map and targets a handler/nearby human. From there, human→human spread is pure proximity + cleanliness.

### Vector_Lovin

Sexual transmission vector. **Not used by any shipped disease** — purely an extensibility hook for modders.

**Trigger:** Hooks into the lovin' job completion event.

**Per-act logic:**
1. If either partner is contagious, roll `baseChancePerAct`.
2. No distance/room modifiers — the context is inherently close-contact, indoors.

**Key properties:**
- `baseChancePerAct` — flat transmission chance per lovin' act

### Vector_Foodborne

Transmission through prepared meals. For gut worms and a contagious food poisoning variant.

**Mechanism:**
1. When a meal is prepared by a contagious pawn (carrying a disease with this vector), the meal is tagged (via a `ThingComp` on the meal) as contaminated.
2. When any pawn eats a contaminated meal, roll `baseChancePerMeal`.
3. Kitchen cleanliness modifies the chance (same `RoomStatDefOf.Cleanliness` check as proximity).

**Key properties:**
- `baseChancePerMeal` — chance per contaminated meal consumed
- `cleanlinessImpact` — how kitchen cleanliness modifies the chance

**Counterplay:** Don't let sick pawns cook (work restrictions — existing system). Keep the kitchen clean. Both are things players already know to do; the mod makes the consequences sharper.

---

## Seeding Mechanisms

### Seeder_Visitor

Visitors, traders, and new recruits have a chance of arriving in incubation.

**Logic:** When a pawn enters the map via visitor/trader/recruit events, roll against `arrivalChance` (per-disease, tunable). On success, apply `Hediff_Incubation`. The visitor moves through the colony during their stay, potentially spreading before they leave.

**Properties:**
- `arrivalChance` — probability that any incoming pawn carries this disease

Primary seeder for flu. Players will learn that visitors are a disease vector — which creates a natural tension (traders bring goods but also risk) without requiring the player to "inspect" arrivals.

### Seeder_Acausal

Vanilla-style "a colonist catches [disease]" but seeds ONE pawn with incubation instead of giving multiple pawns the full disease.

**Properties:**
- `mtbDays` — mean time between seeding events

Fallback for isolated colonies. Also the primary seeder for gut worms (poor hygiene → random gut worm case).

### Seeder_Animal

For plague specifically. When flea-prone animal populations exist on the map (wild or tame), periodically seed a human pawn (biased toward animal handlers) with plague incubation.

**Properties:**
- `mtbDays` — mean time between checks
- `requiresAnimalsOnMap` — bool, gates the check
- `handlerBias` — multiplier making handlers more likely targets

Narrative: "contracted plague from animal contact." Mechanically: direct human seeding gated by animal presence.

### Seeder_Environmental

The map itself is the ongoing source. No event needed — biome conditions ARE the risk.

**Logic:** Runs continuously. Every check tick, if biome/temperature/season conditions are met, all eligible outdoor pawns (or indoor pawns near the edge, per `Vector_Environmental` logic) are candidates. Essentially, the environmental vector IS the seeder — there's no separate seeding step because the environment is always "contagious."

**Properties:** Inherits from the `Vector_Environmental` configuration. The vector and seeder are the same system for environmental diseases.

Used for malaria and sleeping sickness. The "outbreak" is just "the warm season started."

---

## Quarantine — No New Systems

The mod adds no quarantine mechanics. Instead, existing systems naturally produce quarantine behavior:

**Medical rest** puts the pawn in bed, in a room. If that room is a dedicated hospital away from living/work areas, airborne transmission is contained by walls. If the player's hospital is also the barracks, the disease spreads. This drives hospital separation organically.

**Area restrictions** let players confine sick pawns to specific zones. A player who restricts a flu-ridden pawn to the hospital area is performing quarantine using existing tools.

**Work restrictions** let players stop sick pawns from cooking, preventing foodborne transmission.

**Room layout** becomes meaningful: doors between rooms block airborne transmission (different enclosed rooms = zero airborne chance). A colony with separate bedrooms contains disease better than open barracks. Players learn this through consequences.

**Masks and respiratory protection** reduce airborne, social, and close-contact transmission for both the wearer and people around them, keyed on the vanilla `ToxicEnvironmentResistance` stat. Worn apparel (gas masks) and air-filtering body parts (detoxifier/fleshmass lung) count at full effect; genes are off by default and opted in via the `Contagion_RespiratoryImmunity` config def (shipping with breathless as airway immunity). Outfitting a caregiver or a sick pawn with a mask is meaningful counterplay using existing gear, not a new system.

**The design goal:** A player who has never heard of this mod but follows RimWorld common sense (put sick pawns in medical beds, have a dedicated hospital, keep the base clean, mask up around the sick) will naturally contain most outbreaks. The mod rewards good colony design rather than requiring new learned behaviors.

---

## Penoxycyline Interaction

Penoxycyline works exactly as vanilla: it prevents the pawn from contracting applicable diseases.

**Implementation:** During all transmission rolls (any vector), check if the target pawn has the penoxycyline hediff. If yes, and the disease is in penoxycyline's prevention list (malaria, plague, sleeping sickness — vanilla defaults), skip the roll entirely.

No changes to penoxycyline's scope, duration, or availability.

---

## Mod Settings

Accessible through the standard `ModSettings` page.

### Difficulty (preset)

A single Easier / Normal / Harder control that presets the spread feel on top of the advanced sliders:

| Difficulty | Transmission scale | Spread suppression |
|---|---|---|
| Easier | 0.7× | Strong (outbreaks rarely reach the whole colony) |
| Normal | 1.0× | Moderate (a well-run colony can usually contain it) |
| Harder | 1.35× | **Disabled** (an untreated outbreak can sweep the colony) |

Difficulty multiplies the Transmission Rate slider rather than replacing it, so the two compose. **Spread suppression** dampens contagious person-to-person/fomite transmission toward colonists as more of the colony is already infected — `(1 - infectedColonyFraction) ^ strength` — preventing deterministic 100% saturation. It is measured over and applied only to player-faction pawns, and excludes foodborne and environmental seeding.

### Toggles and advanced tuning

| Setting | Range / values | Default | Effect |
|---|---|---|---|
| Masks reduce spread | on / off | on | Apparel and air-filtering body parts reduce respiratory transmission via `ToxicEnvironmentResistance` |
| Transmission Rate | 0.25× – 2.0× | 1.0× | Global multiplier on all vector base chances (composed with difficulty scale) |
| Outbreak Frequency | 0.25× – 2.0× | 1.0× | Multiplier on all seeder MTB timers |
| Incubation Length | 0.25× – 2.0× | 1.0× | Multiplier on incubation durations |

Per-disease behavior (e.g. `spreadSuppressionScale`, per-vector mask effectiveness) and the gene airway-immunity whitelist (`Contagion_RespiratoryImmunity`) live in XML so players and modders can tune or patch them directly.

---

## Extensibility — Modder Guide

### Making an existing hediff contagious (XML only)

```xml
<!-- Example: make muscle parasites spread through social contact -->
<Operation Class="PatchOperationAdd">
  <xpath>Defs/HediffDef[defName="MuscleParasites"]/modExtensions</xpath>
  <value>
    <li Class="Infection.TransmissionProfile">
      <contagiousMinSeverity>0.1</contagiousMinSeverity>
      <contagiousMaxSeverity>0.6</contagiousMaxSeverity>
      <contagiousDuringIncubation>false</contagiousDuringIncubation>
      <incubationDays>3</incubationDays>
      <immunityDurationDays>15</immunityDurationDays>
      <vectors>
        <li Class="Infection.Vector_Social">
          <baseChancePerInteraction>0.02</baseChancePerInteraction>
          <outdoorFactor>0.5</outdoorFactor>
        </li>
      </vectors>
      <seeders>
        <li Class="Infection.Seeder_Acausal">
          <mtbDays>90</mtbDays>
        </li>
      </seeders>
    </li>
  </value>
</Operation>
```

### Adding a new STD (XML only, using shipped vectors)

```xml
<HediffDef ParentName="DiseaseBase">
  <defName>MySTD</defName>
  <!-- standard hediff fields -->
  <modExtensions>
    <li Class="Infection.TransmissionProfile">
      <vectors>
        <li Class="Infection.Vector_Lovin">
          <baseChancePerAct>0.15</baseChancePerAct>
        </li>
      </vectors>
      <seeders>
        <li Class="Infection.Seeder_Visitor">
          <arrivalChance>0.01</arrivalChance>
        </li>
      </seeders>
      <immunityDurationDays>0</immunityDurationDays>
      <incubationDays>5</incubationDays>
    </li>
  </modExtensions>
</HediffDef>
```

### Adding a new vector type (requires C#)

Subclass `TransmissionVector` and implement `EvaluateCandidates()`:

```csharp
public class Vector_Custom : TransmissionVector
{
    // XML-configurable fields
    public float baseChance;

    public override IEnumerable<(Pawn target, float chance)>
        EvaluateCandidates(Pawn source, Map map)
    {
        // Custom logic here
        // Return candidate targets with per-target transmission chances
    }
}
```

The engine calls each vector's `EvaluateCandidates` during the transmission tick and handles immunity/penoxycyline/duplicate checks centrally.

---

## Shipped Disease Configurations

### Flu

```
Vectors:       Vector_Airborne (primary), Vector_Social (secondary), Vector_Fomite (vomit, minor)
Seeders:       Seeder_Visitor, Seeder_Acausal (MTB 60 days)
Incubation:    1.5 days
Contagious:    During incubation + severity 0.05–0.80
Immunity:      15 days post-recovery
Species:       Humans and animals, no cross-species
```

### Plague

```
Vectors:       Vector_Proximity (cleanliness-gated)
Seeders:       Seeder_Animal (handlers, requires animals on map), Seeder_Acausal (MTB 120 days fallback)
Incubation:    1.0 days
Contagious:    Severity 0.05–0.90
Immunity:      15 days post-recovery
Species:       Humans and animals, no cross-species
```

### Malaria

```
Vectors:       Vector_Environmental (mosquito model)
Seeders:       Seeder_Environmental (continuous, biome-gated)
Incubation:    2.0 days
Contagious:    Not contagious (no person-to-person spread)
Immunity:      15 days post-recovery
Species:       Humans only
```

### Sleeping Sickness

```
Vectors:       Vector_Environmental (same mosquito model, different biome gates)
Seeders:       Seeder_Environmental (continuous, tropical biomes only)
Incubation:    3.0 days
Contagious:    Not contagious
Immunity:      15 days post-recovery
Species:       Humans only
```

### Gut Worms

```
Vectors:       Vector_Foodborne
Seeders:       Seeder_Acausal (MTB 90 days)
Incubation:    3.0 days
Contagious:    Not contagious (spread through food only)
Immunity:      15 days post-recovery
Species:       Humans only
```

### Food Poisoning (contagious variant)

```
Vectors:       Vector_Fomite (vomit), Vector_Foodborne
Seeders:       Triggered when a vanilla food poisoning case occurs (converts to contagious variant)
Incubation:    0.5 days
Contagious:    Severity 0.10–0.70
Immunity:      5 days post-recovery
Species:       Humans only
```

---

## Decisions Log

| Decision | Rationale |
|---|---|
| Distance-based, not room-size-based | Hallways are large "rooms" but shouldn't transmit like bedrooms; distance handles this naturally |
| Social = boosted airborne, separate vector class | Same result as a modifier, but separate class enables composition (social-only diseases for modders) |
| Fomites scoped to vomit only | Solves visibility problem — vomit is already visible and cleanable; no invisible contamination |
| Mosquitoes give indoor chance too | Mosquitoes enter buildings; chance reduced by distance from outdoors and by AC temperature |
| No flea hediff | Fleas are ambient, modeled as proximity + cleanliness; no extra hediff state to track |
| No cross-species transmission | Keeps animal husbandry from becoming punishing; separate contagion pools are simpler |
| Plague seeds onto humans gated by animal presence | Option C — clean implementation, no cross-species system, narrative framing handles the rest |
| Room cleanliness does NOT affect airborne | Avoids double-penalizing dirty rooms (already give mood debuffs); cleanliness affects proximity vector only |
| No visitor inspection mechanic | Players shouldn't feel forced to examine every trader; hidden arrival incubation is an accepted risk |
| Penoxycyline unchanged | Prevents applicable diseases during all transmission rolls, same scope as vanilla |
| Sensory/fibrous mechanites untouched | Non-biological, don't fit contagion model; work fine as random events |

---

## Open Items for Implementation Phase

- Confirm vanilla vomiting behavior for each disease (which severity stages trigger vomit, frequency)
- Determine exact hooks for social interaction events (which method to postfix)
- Profile transmission tick cost at large colony sizes (20+ pawns, multiple contagious)
- Decide on incubation hediff visibility — fully hidden vs. "???" at low doctor skill vs. always visible
- Investigate whether `Room.TouchesMapEdge` is reliable for indoor/outdoor detection across all edge cases (mountain bases, mixed roofing)
- Determine cough/sneeze visual feedback approach (mote system reuse — deferred, not priority)
- Save compatibility: `Hediff_Incubation` and immunity tracking need clean removal behavior if mod is uninstalled mid-save
