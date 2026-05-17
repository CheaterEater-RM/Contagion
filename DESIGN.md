# Contagion Mod Design

## Summary

Contagion changes disease acquisition in RimWorld from opaque random outbreaks into understandable cause-and-effect. Vanilla diseases, treatment, immunity races, beds, area restrictions, cleanliness, and penoxycyline stay relevant. The mod adds transmission logic, incubation, and clearer seeding so players can infer why an outbreak started and how to stop it.

This is a behavior mod, not a content mod. It should not add new zones, buildings, research, tabs, or player-only quarantine systems.

## Problem Statement

Vanilla RimWorld mostly starts diseases through storyteller incidents. The storyteller picks a disease based on biome, then immediately applies the final disease hediff to a random slice of colonists or animals. That creates three problems:

- disease origin is abstract and hard to read in play
- once the incident fires, transmission inside the colony does not matter
- player counterplay is mostly treatment after the fact, not prevention through layout, hygiene, or isolation

Contagion keeps the same disease defs and treatment game, but changes the acquisition model from "the storyteller infected these pawns" to "a source seeded an infection, and the colony either contained it or failed to."

## Design Goals

- Make disease origins legible in normal play without adding a new tutorial system.
- Keep vanilla `HediffDef` diseases and vanilla treatment behavior intact whenever possible.
- Reuse existing player tools: medical beds, rest, room layout, area restrictions, work priorities, cleaning, and penoxycyline.
- Support future contagious diseases through XML-first configuration.
- Preserve species separation by default. Human disease and animal disease should remain separate unless a profile opts into crossover.

## Non-Goals

- No new items, buildings, zones, or custom quarantine UI.
- No rewrite of wound infection, mechanites, organ decay, or other non-target diseases.
- No broad replacement of the vanilla health system.
- No first-release caravan contagion simulation. Vanilla caravans can keep existing disease behavior until a world-scope design is justified.

## Vanilla Baseline To Preserve

- Disease severity and immunity races come from the existing disease hediff.
- Tending, bed rest, and life-threatening stages remain vanilla.
- Penoxycyline and any future prophylactic hediffs keep working through vanilla immunity checks.
- Food poisoning, rotten food, and filthy kitchens should still feel like RimWorld, not a separate subsystem.

## Core Design Principles

### Mechanisms, Not Content

The mod should add rules to existing systems rather than inventing parallel objects. A player quarantines by assigning a hospital area, forbidding cooking, or isolating bedrooms, not by learning a new Contagion widget.

### Vanilla-First Disease Model

The real disease remains the vanilla hediff. Contagion adds a lightweight incubation layer and transmission rules around it, then hands control back to vanilla once the disease is active.

### Clear Sources

Every outbreak should start from a plausible source:

- an incoming pawn
- a biome or season
- dirty food handling
- contaminated vomit
- animal-linked plague seeding
- a single storyteller-seeded carrier where no better in-world source exists

The mod should not silently recreate vanilla's "five random colonists are sick now" behavior.

### Species Separation By Default

Vanilla already distinguishes human and animal disease incidents for flu and plague. Contagion should build on that pattern, not fight it. Human flu and animal flu are separate infection pools unless a profile explicitly opts into cross-species spread.

### Save-Conscious State

Persistent state should live in places RimWorld already saves safely:

- static data in `DefModExtension`
- map-scoped runtime state in `MapComponent`
- meal and filth contamination in `ThingComp`
- temporary per-pawn disease state in hidden hediffs only where necessary

## How Outbreaks Begin

### Storyteller Seeding

Vanilla storyteller disease events remain useful as a source selector, but not as the final outcome. For contagious diseases, storyteller selection should seed one carrier or one incubation case instead of immediately applying the active disease to a large random group.

### Visitor And Arrival Seeding

Visitors, traders, refugees, and similar arrivals can bring incubation with them. This keeps flu-like disease introduction readable without forcing the player to inspect every pawn manually.

### Environmental Seeding

Malaria and sleeping sickness should come from biome, temperature, and exposure conditions. The map is the source. A pawn gets infected because they were exposed in a risky environment, not because the storyteller chose them arbitrarily.

### Food System Seeding

Gut worms and contagious food poisoning should arise from meals, kitchens, and ingestion. Sick cooks, dirty kitchens, and contaminated food are the cause.

### Animal-Linked Seeding

Plague should still feel associated with animals without requiring general animal-to-human contagion. Animal presence can authorize or bias the first human seed event, after which human-to-human spread uses the normal transmission rules.

## Core Data Model

### TransmissionProfile

Every contagious disease gets a `TransmissionProfile` `DefModExtension` on the real `HediffDef`.

The profile owns:

- contagious window information
- incubation duration
- temporary post-recovery immunity duration
- species scope
- a list of transmission vectors
- a list of seeders
- optional part-target metadata for localized diseases

That last field is required in practice. Some vanilla diseases are not self-contained on the hediff def. Gut worms, for example, is applied to the stomach by the incident definition. A contagion profile must either carry `partsToAffect` itself or derive it from linked incident data, otherwise a future contagion application would not know where to place the disease.

### Incubation Wrapper

Contagious diseases gain a hidden incubation phase before the real disease appears. The wrapper exists to do two things:

- delay symptom onset so quarantine has a meaningful window
- support contagiousness before the vanilla disease is visible, when the profile wants that behavior

Incubation is intentionally thin. It should not replace the real disease's severity curve, treatment rules, or immunity race.

User input: the incubation should be best done with a hidden hediff, as this is a natural fit.

### Temporary Post-Recovery Immunity

The mod owns short-term reinfection protection after recovery. This cannot rely only on vanilla `HediffComp_Immunizable`, because some target diseases such as gut worms and food poisoning do not use the natural immunity system at all.

The rule is simple: once a pawn recovers from a contagious disease, they gain a hidden temporary immunity source for that disease for `immunityDurationDays`.

### Transmission Engine

A `MapComponent` runs transmission on a fixed interval. Its job is to:

1. collect contagious sources on the current map
2. ask each vector to produce candidates and chances
3. run one central infection gate
4. apply incubation when a roll succeeds

The central infection gate should reject targets that:

- already have the disease
- are already incubating it
- already have temporary immunity
- are fully blocked by vanilla immunity, genes, or penoxycyline-style prophylaxis
- are outside the profile's species rules

## Disease Lifecycle

Each contagious disease follows the same five-phase model:

1. Seeded: a source authorizes the disease to begin on a specific pawn.
2. Incubating: the pawn carries a hidden wrapper hediff.
3. Active: the real vanilla disease hediff is applied and progresses normally.
4. Recovering: vanilla handles the tail end of the disease.
5. Temporarily immune: the pawn cannot be immediately re-seeded.

The key design rule is that phases 3 and 4 are still vanilla. Contagion does not create a parallel disease simulation once symptoms begin.

## Transmission Vectors

### Airborne

Used for flu-like respiratory disease. Chance falls off with distance and is strongest when source and target share an enclosed room. Outdoors and edge-open spaces heavily reduce transmission.

### Social

An interaction-triggered boost layered on top of close respiratory spread. This covers face-to-face talk without inventing a second social system.

### Proximity

Short-range contact spread modulated by room cleanliness. This is a better fit for plague-like close-contact disease than pure airborne spread.

### Fomite

Visible contaminated surfaces, scoped to vomit filth for the initial implementation. This makes contamination legible and cleanable.

### Environmental

Ambient exposure based on biome, temperature, water, and outdoor access. Used for malaria and sleeping sickness, which are seeded by the environment and do not need person-to-person contagion.

### Foodborne

Transmission through prepared meals and ingestion. This uses the existing cooking and food systems instead of a separate disease subsystem.

### Lovin

Reserved as an extensibility vector. It is not needed for the shipped disease list, but the design should not prevent future STD-style mods from using the same framework.

## Seeders

### Storyteller Seeder

Uses the storyteller's existing disease choice as a source of narrative pressure, but converts the selected disease into one or a few incubation cases rather than a colony-wide outbreak.

### Arrival Seeder

Applies incubation to appropriate incoming pawns so visitors and traders can plausibly introduce respiratory disease.

### Environmental Seeder

Continuously exposes pawns to malaria or sleeping sickness based on biome and local conditions. The environment is both the source and the vector.

### Animal-Linked Seeder

Allows plague to begin in humans when animal presence or animal work makes that believable, without turning the whole mod into a general cross-species infection system.

### Food Seeder

Tags meals or ingestion results as disease sources when a contagious cook or dirty kitchen should matter.

## Shipped Disease Plan

| Disease | How It Starts | How It Spreads | Notes |
|---|---|---|---|
| Flu | Arrival seeding and storyteller seeding | Airborne, social, vomit fomite | Human and animal variants stay separate |
| Plague | Animal-linked or storyteller seeding | Proximity with cleanliness pressure | Respect vanilla penoxycyline blocking |
| Malaria | Environmental seeding | Environmental only | No person-to-person spread |
| Sleeping sickness | Environmental seeding | Environmental only | Tropical-heavy environmental disease |
| Gut worms | Storyteller or food handling seeding | Foodborne | Must preserve stomach targeting |
| Food poisoning variant | Existing food poisoning paths | Foodborne and vomit fomite | Built on ingestion, not storyteller incidents |

## Counterplay

The mod's counterplay is intentionally vanilla:

- separate hospitals
- room doors and bedroom layout
- area restrictions for sick pawns
- stopping sick cooks from working
- cleaning vomit and kitchens quickly
- keeping vulnerable pawns on penoxycyline where vanilla already supports it

Players who already use sensible RimWorld colony design should perform better without learning a new ruleset.

## Scope Boundary For First Implementation

The first implementation is map-scoped.

- colony maps are fully supported
- visitors and other spawned pawns on maps are valid sources or targets
- caravans keep vanilla disease behavior unless a later world component is added

This boundary keeps the first version aligned with the actual transmission mechanics the player can observe and influence through map layout.

## Compatibility And Extensibility

To make future diseases easy to support:

- the profile lives on the real disease hediff
- vectors are composable classes
- central immunity and duplicate checks are not vector-specific
- meal contamination and filth contamination use standard RimWorld comp patterns

The mod should prefer XML-only disease onboarding whenever possible. C# should be required only for genuinely new vector logic, not for every new contagious disease.

## Key Design Constraints

- Part-targeted diseases require explicit metadata outside the vanilla incident path.
- Temporary reinfection immunity must be mod-owned for non-immunizable diseases.
- Penoxycyline should be respected through vanilla immunity checks, not reimplemented as a separate hardcoded list.
- Animal disease should remain species-isolated by default.
- Caravan contagion is deferred, not accidentally half-supported.

## Success Criteria

The design succeeds if:

- outbreaks usually begin from a readable source
- players can contain disease with normal RimWorld tools
- the real disease behavior still feels vanilla once symptoms begin
- future diseases can be made contagious mostly through XML
- the implementation avoids broad conflict-heavy Harmony cancellations