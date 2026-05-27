# Contagion — TransmissionProfile Schema Redesign
*Design document — May 2026*
*Status: Final draft. Ready for implementation.*

---

## Purpose

This document defines the `TransmissionProfile` `DefModExtension` schema — the XML contract that modders use to make any hediff contagious. The goal is to maximize expressive power for modders while keeping the simple case simple. It supersedes the field layout in `INFECTION_DESIGN.md` and the current `TransmissionProfile.cs` implementation.

The current implementation has a working skeleton: `TransmissionProfile`, seven vector types, five seeder types, incubation and temporary immunity hediffs, profile XML for all shipped diseases, and the `MapComponent` transmission engine stub. This redesign refines the data model before the engine logic is fleshed out. It does not change the architecture.

---

## Summary of Changes

### Fields removed (subsumed by infectivity curves)

- `contagiousMinSeverity` — replaced by the curve evaluating to zero
- `contagiousMaxSeverity` — replaced by the curve evaluating to zero
- `contagiousDuringIncubation` — replaced by `incubationInfectivityCurve` being nonzero

### Fields added to TransmissionProfile

- `incubationInfectivityCurve` (SimpleCurve, optional)
- `activeInfectivityCurve` (SimpleCurve, optional)
- `seasonalInfectivity` (SeasonalInfectivity, optional — six per-season weights)
- `susceptibilityFactors` (polymorphic list, optional)
- `sourceInfectivityFactors` (polymorphic list, optional)
- `maxActiveCases` (int, optional)
- `outbreakNotification` (enum, optional)
- `corpseContagious` (bool, optional)
- `corpseInfectivityDecayPerDay` (float, optional)
- `crossSpeciesTransmissionFactor` (float, replaces bool `crossSpeciesTransmission`)
- `immunityHediffDef` (HediffDef, optional — custom immunity hediff override)
- `carrierChance` (float, reserved)
- `carrierHediffDef` (HediffDef, reserved)
- `spreadsDuringCaravan` (bool, reserved)
- `spreadSuppressionScale` (float, default 1.0 — per-disease scaling of colony spread suppression; see Spread Suppression)

### Fields added to vectors

- `obstructedFactor` on `Vector_Airborne` (LOS-blocked transmission multiplier)
- `outdoorFilthRadius` on `Vector_Proximity` (local filth check when outdoors)
- Optional per-vector `activeInfectivityCurveOverride` and `incubationInfectivityCurveOverride`
- `maskTargetEffectiveness`, `maskSourceEffectiveness`, `airwayImmunityFactor` on the shared `RespiratoryVector` base (`Vector_Airborne`, `Vector_Social`, `Vector_Proximity`); see Respiratory Protection

### Fields added to seeders

- `cooldownDays` (minimum gap between seed events)
- `maxActiveCases` (per-seeder override; profile-level field is the default)

### Architectural shift: pawn-local context, not room membership

Airborne and proximity vectors stop using room identity as a binary gate. They use distance, LOS, and roofing at the pawn's cell instead. Room stats are used only where they're the genuinely correct abstraction (kitchen cleanliness for foodborne, room cleanliness as a convenience when a pawn is indoors for proximity).

---

## Infectivity Model

### Problem with current design

The current schema uses a binary contagious window: `contagiousMinSeverity` to `contagiousMaxSeverity`, optionally extended to incubation with a boolean. Every pawn in the window is equally contagious. This doesn't model real disease dynamics — flu is slightly contagious during incubation (before the player sees symptoms), ramps to peak infectivity mid-illness, and tapers during recovery. A flat on/off window can't express this, and modders can't tune it.

### Two-curve solution

Two `SimpleCurve` fields replace the three removed fields:

**`incubationInfectivityCurve`** — X axis is incubation progress from 0.0 (just contracted) to 1.0 (about to show symptoms). Y axis is an infectivity multiplier applied to all vector base chances. The incubation hediff already tracks progress as severity (0.01 to 1.0), so the lookup is trivial.

**`activeInfectivityCurve`** — X axis is severity of the real vanilla hediff (0.0 to 1.0). Y axis is the same infectivity multiplier.

Example for flu:

```xml
<incubationInfectivityCurve>
  <points>
    <li>(0.0, 0.0)</li>     <!-- just contracted -->
    <li>(0.3, 0.05)</li>    <!-- early incubation: barely infectious -->
    <li>(0.7, 0.15)</li>    <!-- late incubation: starting to shed -->
    <li>(1.0, 0.3)</li>     <!-- about to show symptoms -->
  </points>
</incubationInfectivityCurve>

<activeInfectivityCurve>
  <points>
    <li>(0.00, 0.4)</li>    <!-- just appeared -->
    <li>(0.25, 1.0)</li>    <!-- ramping up -->
    <li>(0.55, 1.0)</li>    <!-- peak plateau -->
    <li>(0.80, 0.3)</li>    <!-- declining -->
    <li>(1.00, 0.0)</li>    <!-- near-death -->
  </points>
</activeInfectivityCurve>
```

### Defaults when omitted

- `incubationInfectivityCurve` omitted → flat 0.0 (not contagious during incubation). Safe default; opt-in for pre-symptomatic spread.
- `activeInfectivityCurve` omitted → default bell: `(0.0, 0.3), (0.15, 0.7), (0.35, 1.0), (0.65, 1.0), (0.85, 0.3), (1.0, 0.0)`. Ramp-up, sustained peak through mid-severity, taper to zero. Exact shape is playtest-dependent — the default provides a functional generic profile that can be tuned later.

This means a minimal profile with no curves specified still works — it uses the default active curve and no incubation infectivity. Only modders who want fine control need to specify curves.

### Per-vector overrides

Vectors can optionally override the profile-level curves. If a vector has an `activeInfectivityCurveOverride` field set, it uses that instead of the profile's. This handles cases where fomite transmission should peak at different severity than airborne transmission for the same disease.

```xml
<li Class="Contagion.Vector_Fomite">
  <baseChancePerContact>0.04</baseChancePerContact>
  <!-- Only contagious through vomit at high severity -->
  <activeInfectivityCurveOverride>
    <points>
      <li>(0.50, 0.0)</li>
      <li>(0.70, 1.0)</li>
      <li>(1.00, 0.5)</li>
    </points>
  </activeInfectivityCurveOverride>
</li>
```

---

## Seasonal Infectivity

### Problem

The original design proposed a `SimpleCurve` keyed on `YearPercent` (0.0–1.0 over the calendar year). This doesn't account for hemisphere inversion, equatorial permanent summer, or polar permanent winter. A raw calendar curve would mean a disease tuned to "peak in winter" peaks at the same calendar date for both hemispheres.

### Solution: per-season weight blending

Vanilla `SeasonUtility.GetSeason(yearPct, latitude, ...)` outputs six continuous float weights — one for each of spring, summer, fall, winter, permanentSummer, permanentWinter — that sum to 1.0. These weights handle hemisphere inversion, equatorial/polar blending, and smooth cross-fading at season transitions (each transition spans roughly 5 in-game days via `SeasonYearPctLerpDistance = 0.085`). The modder defines a multiplier for each season, and the engine computes a weighted sum.

```xml
<seasonalInfectivity>
  <spring>0.6</spring>
  <summer>0.3</summer>
  <fall>0.8</fall>
  <winter>1.0</winter>
  <permanentSummer>0.4</permanentSummer>
  <permanentWinter>0.7</permanentWinter>
</seasonalInfectivity>
```

Engine evaluation:

```csharp
float yearPct = GenDate.YearPercent(absTicks, longitude);
SeasonUtility.GetSeason(yearPct, latitude,
    out float spring, out float summer, out float fall, out float winter,
    out float permSummer, out float permWinter);

float multiplier = spring  * profile.seasonalInfectivity.spring
                 + summer  * profile.seasonalInfectivity.summer
                 + fall    * profile.seasonalInfectivity.fall
                 + winter  * profile.seasonalInfectivity.winter
                 + permSummer * profile.seasonalInfectivity.permanentSummer
                 + permWinter * profile.seasonalInfectivity.permanentWinter;
```

During a season transition — say mid-spring trending into summer — vanilla might output `spring=0.7, summer=0.3` with all others at zero. For flu with `spring=0.6, summer=0.3`, the multiplier is `0.7 × 0.6 + 0.3 × 0.3 = 0.51`. The taper between seasons is automatic and smooth.

### Defaults when omitted

All six weights default to 1.0 (no seasonal variation). The `SeasonalInfectivity` class initializes all fields to 1.0f in its default constructor.

### Why this works better than a YearPercent curve

- **Hemisphere**: vanilla's season weights already flip for southern latitudes. No manual offset needed.
- **Equatorial/polar**: modders get explicit `permanentSummer` and `permanentWinter` controls instead of a fallback.
- **Transition taper**: built into vanilla's blending, not something we have to implement.
- **Simplicity**: six intuitive numbers instead of a multi-point curve that requires understanding RimWorld's calendar system.

---

## Susceptibility Factors (Target Side)

### Problem

All pawns currently have identical vulnerability. Vanilla has genes, traits, hediffs, and age that should affect disease resistance. Penoxycyline prevention is conceptually a susceptibility modifier but would need to be hardcoded separately without this system.

### Solution: polymorphic factor list

```xml
<susceptibilityFactors>
  <li Class="Contagion.Factor_Hediff">
    <hediff>PenoxycylineHigh</hediff>
    <factor>0.0</factor>
  </li>
  <li Class="Contagion.Factor_Gene">
    <gene>Robust</gene>
    <factor>0.5</factor>
  </li>
  <li Class="Contagion.Factor_AgeRange">
    <minAge>0</minAge>
    <maxAge>7</maxAge>
    <factor>1.5</factor>
  </li>
  <li Class="Contagion.Factor_Stat">
    <stat>ImmunityGainSpeed</stat>
    <curve>
      <points>
        <li>(0.5, 1.5)</li>   <!-- low immunity gain → more susceptible -->
        <li>(1.0, 1.0)</li>   <!-- baseline -->
        <li>(1.5, 0.67)</li>  <!-- high immunity gain → less susceptible -->
        <li>(2.0, 0.5)</li>
      </points>
    </curve>
  </li>
</susceptibilityFactors>
```

Stacking: multiplicative. Robust gene (0.5) on a child (1.5) = 0.75 effective susceptibility. Penoxycyline (0.0) dominates — any zero factor zeroes the product.

Omitted = empty list, all pawns equally susceptible.

### Penoxycyline integration

Rather than hardcoding penoxycyline checks in the engine, each profile that should respect penoxycyline includes a `Factor_Hediff` entry. This means modders adding prophylactic drugs for custom diseases just add another factor entry — no C# required.

### Interaction with vanilla DiseaseContractChanceFactor

The engine calls `ImmunityHandler.DiseaseContractChanceFactor` as a final multiplicative gate. This handles gene immunity, `makeImmuneTo`, existing-hediff checks, mutant immunity, and non-flesh pawns. Susceptibility factors are a Contagion-layer multiplier applied *before* the vanilla gate.

`DiseaseContractChanceFactor` returns a float: 0.0 for full immunity, partial values for partial immunity from the immunityList (`Mathf.Lerp(1f, 0f, immunity / 0.6f)`), and 1.0 when no immunity applies. The engine multiplies it in, so vanilla immunity genes that partially reduce contraction chance stack proportionally with Contagion's susceptibility factors.

### Seeders respect susceptibility factors

Seeders apply susceptibility factors when selecting targets. The factor represents overall biological resistance, including innate ability to fight off initial exposure. A Robust-gene pawn should be less likely to be patient zero, and Penoxycyline (factor 0.0) must block seeding.

`maxActiveCases` is checked *before* factor evaluation — it's a population-level gate, not a per-pawn check.

### Factor_Stat: curve mode only

`Factor_Stat` accepts only a `curve` field (a `SimpleCurve` mapping stat value to multiplier). Documented example curves ship with the mod for common stats like `ImmunityGainSpeed`.

### Planned factor types

| Class | Fields | Purpose |
|---|---|---|
| `Factor_Hediff` | `hediff`, `factor` | Presence of a hediff modifies susceptibility (penoxycyline, immunosuppressants, etc.) |
| `Factor_Gene` | `gene`, `factor` | Gene presence modifies susceptibility |
| `Factor_Trait` | `trait`, `factor` | Trait presence modifies susceptibility |
| `Factor_AgeRange` | `minAge`, `maxAge`, `factor` | Age bracket modifies susceptibility |
| `Factor_Stat` | `stat`, `curve` | Pawn stat value scales susceptibility via a modder-provided curve |
| `Factor_HasInjury` | `factor` | Pawn has any open wound — enables MRSA-type scenarios |

All are XML-only for modders. New factor types require C# (subclass `SusceptibilityFactor`).

---

## Source Infectivity Factors

### Problem

Contagion models target-side resistance (susceptibility factors), but has no system for source-side modifiers. A pawn taking a cough suppressant drug should be less contagious. A pawn wearing a face covering should shed fewer particles. Without source-side factors, the only way to represent this is through the infectivity curves — which are keyed on severity, not on what the source pawn is wearing or medicated with.

### Solution: sourceInfectivityFactors

A polymorphic factor list on the source side, structurally identical to `susceptibilityFactors` but evaluated against the contagious pawn rather than the target. The same base class and factor types apply.

```xml
<sourceInfectivityFactors>
  <li Class="Contagion.SourceFactor_Hediff">
    <hediff>SymptomSuppressant</hediff>
    <factor>0.3</factor>
  </li>
  <li Class="Contagion.SourceFactor_Hediff">
    <hediff>FaceCovering</hediff>
    <factor>0.5</factor>
  </li>
</sourceInfectivityFactors>
```

Stacking: multiplicative, same as susceptibility factors.

Omitted = empty list, no source-side modifiers.

### Why this matters now

This field enables the planned sister mod (see Architecture section below) to affect transmission without any compile-time dependency on Contagion. The sister mod applies a hediff (e.g. `SymptomSuppressant`); Contagion's profile references it via XML. If the sister mod isn't installed, the hediff never appears, and the factor entry is inert.

Even without the sister mod, `sourceInfectivityFactors` is useful for modders who want masking or containment mechanics — any mod that applies a hediff can reduce a pawn's contagiousness.

### Source factor types

The source factor type hierarchy mirrors susceptibility factors. Initial shipped types:

| Class | Fields | Purpose |
|---|---|---|
| `SourceFactor_Hediff` | `hediff`, `factor` | Hediff on source pawn modifies infectivity |
| `SourceFactor_Gene` | `gene`, `factor` | Gene on source pawn modifies infectivity |
| `SourceFactor_Stat` | `stat`, `curve` | Source pawn stat value scales infectivity |

---

## Pawn-Local Context Model (Rooms → LOS + Roofing)

### Problem

The current design uses RimWorld room identity as a binary gate: same enclosed room = full transmission, different room = zero. This creates discontinuities. A 3-wide, 50-cell hallway is one room but shouldn't transmit across its length. Two pawns on opposite sides of an open doorway are in different rooms but 2 cells apart. Room identity adds a hard edge that doesn't match physical intuition.

Distance falloff already partially solves this (far-apart pawns in a big room get low chances), but the room binary still causes the doorway problem and creates a misleading mental model.

### Solution: LOS + roofing at endpoints

Replace room-based checks with per-pair evaluation:

**Roofing** — `map.roofGrid.Roofed(cell)` at both source and target cells. Both roofed = enclosed environment, aerosols concentrated. Either unroofed = outdoor dispersal rules apply. This is a per-cell check, not a room query. Handles roofed courtyards, partially-roofed structures, and mountain bases correctly.

**Line of sight** — `GenSight.LineOfSight(source.Position, target.Position, map)`. If LOS is clear, air path exists. If blocked by walls or closed doors, transmission is blocked or heavily reduced. Piggybacks on a battle-tested vanilla system used constantly for combat.

**New airborne field: `obstructedFactor`** — multiplier when LOS is blocked. Default 0.0 (walls fully block airborne). A modder could set 0.05 for a disease that seeps through door cracks.

**Open doors**: Vanilla LOS passes through open doors and blocks on closed doors. This is correct for airborne — an open doorway is an air path, a closed door is a barrier. The mod accepts vanilla LOS behavior as-is.

**Vents**: Vanilla `Vent` is `Impassable` with `fillPercent=1` and `blockLight=true`. `GenSight.LineOfSight` treats vents as solid walls, so airborne transmission does not pass through vents. This is physically incorrect (vents are air paths) but conservative (less transmission). Accepted as a v1 limitation. A future `ventPassthrough` field on `Vector_Airborne` could add a custom raycast checking for `Building_Vent` or `Building_TempControl` at blocking cells.

Revised airborne equation:

```
chance = baseChancePerCheck
       × infectivityMultiplier(source)                  ← incubation or active curve
       × sourceInfectivityProduct(source)                ← from source factor list
       × susceptibilityProduct(target)                   ← from target factor list
       × distanceFalloff(distance)
       × enclosureModifier(sourceRoofed, targetRoofed)   ← both roofed = 1.0, either unroofed = outdoorFactor
       × obstructionModifier(hasLOS)                     ← clear = 1.0, blocked = obstructedFactor
       × seasonalMultiplier                              ← seasonal weight blend
       × vanillaContractFactor                           ← DiseaseContractChanceFactor
       × globalSettingsMultiplier                        ← player's transmission rate slider
```

No room ID check anywhere.

### Where rooms still apply

**Kitchen cleanliness** for `Vector_Foodborne` — the room stat is the correct abstraction. A kitchen is a room; its aggregate cleanliness matters for food safety.

**Indoor cleanliness** for `Vector_Proximity` when the pawn is in a room — `Room.GetStat(RoomStatDefOf.Cleanliness)` is a reasonable proxy. But outdoors (no room, or `PsychologicallyOutdoors`), fall back to counting filth within `outdoorFilthRadius` cells around the pawn. This handles barnyards, open pens, and unroofed work areas.

### Impact on each vector

| Vector | Old | New |
|---|---|---|
| Airborne | Same room = 1.0, different = 0.0 | LOS + roofing at endpoints |
| Social | Inherited airborne room logic | LOS + roofing (usually moot — interactions are face-to-face) |
| Proximity | Room cleanliness | Room cleanliness indoors, local filth count outdoors |
| Environmental | Distance from room edge | Distance from nearest unroofed cell |
| Fomite | No room dependency | No change (per-filth-thing already) |
| Foodborne | Kitchen cleanliness | No change (room stat is correct here) |
| Lovin | No room dependency | No change |

### Performance

LOS checks run only on candidates that survive distance and roofing filters (cheapest first). Worst case: 5 contagious × 20 in-range candidates = 100 LOS checks per transmission tick (every 250 game ticks). `GenSight.LineOfSight` is a simple cell-walk that vanilla runs hundreds of times per frame for combat. Negligible cost.

### TransmissionContext (C# API for custom vectors)

The engine pre-computes per-pawn local data once per transmission tick:

```csharp
public class PawnTransmissionContext
{
    public bool isRoofed;
    public float localCleanliness;       // room stat if indoors, filth-count derived if outdoors
    public int cellsFromUnroofed;        // 0 = at edge, high = deep in mountain
    public float localTemperature;       // cell or room temperature
    public Room room;                    // available but vectors shouldn't default to it
}
```

Custom vectors receive this context. The design steers toward local data by making it the primary interface, with `room` as an escape hatch for genuinely room-scoped logic.

---

## Spread Suppression

A colony-scoped balancing term layered on top of the per-candidate equation, controlling how completely an outbreak can saturate the colony.

### Mechanic

For a contagious roll toward a colonist, the chance is multiplied by:

```
suppression = (1 - infectedColonyFraction) ^ effectiveStrength
```

- `infectedColonyFraction` = (player-faction pawns the profile can affect that already carry the disease, active or incubating) ÷ (all player-faction pawns the profile can affect). Computed once per disease per transmission pass.
- `effectiveStrength` = the difficulty setting's suppression strength × the profile's `spreadSuppressionScale`. A strength of 0 yields a factor of 1 (no suppression).

### Scope rules (important for correctness)

- **Target-gated**: applied only when the transmission target is a player-faction pawn, matching the population the fraction is measured over. Visitors/prisoners-of-other-factions/raiders are unaffected and uncounted. Without this gate, a fully-infected colony would wrongly throttle spread among unrelated pawns.
- **Vectors covered**: airborne, social, proximity, fomite — contagious spread shed by infected colonists into shared space.
- **Vectors excluded**: foodborne (a contaminated-food source, not herd transmission) and environmental seeding (sourced by the map). These never apply suppression.

### `spreadSuppressionScale` (per disease)

`1.0` = normal, `0` = this disease ignores suppression (used by environmental diseases, which have no person-to-person vectors anyway), `>1` = suppresses faster than other diseases. Default `1.0`.

### Difficulty coupling

The suppression strength is supplied by the player's difficulty setting (Easier = strong, Normal = moderate, Harder = 0/disabled), and difficulty also scales the global transmission multiplier. These are engine/settings concerns, not schema fields, but they determine `effectiveStrength`.

---

## Respiratory Protection (Masks, Lungs, Genes)

Respiratory vectors share a `RespiratoryVector` base that reduces transmission based on protection the source and target are actually wearing or carrying, keyed on the vanilla `ToxicEnvironmentResistance` stat. The contribution is decomposed by source so that equipment and biology are treated correctly.

### Two protection terms

Per side (source and target), the vector chance is multiplied by:

```
sideFactor = (1 - airwayBarrierResistance × maskEffectiveness)   ← physical barrier (apparel + body parts)
           × (1 - geneAirwayImmunity      × airwayImmunityFactor) ← whitelisted gene immunity
```

- `airwayBarrierResistance` = sum of `ToxicEnvironmentResistance` from **worn apparel** (`equippedStatOffsets`) plus **body-part / implant hediffs** (`CurStage.statOffsets`, restricted to `Hediff_AddedPart` / `countsAsAddedPartOrImplant` / `addedPartProps`), clamped 0–1. Genes are deliberately excluded here — most genetic toxic tolerance is metabolic, not an airway barrier. A transient drug/disease hediff that happens to offset the stat is also excluded.
- `geneAirwayImmunity` = highest protection among the pawn's active genes that are explicitly whitelisted (see below).

### Vector fields

- `maskTargetEffectiveness` (default 0.7) — fraction of the target's barrier resistance applied (inhalation side).
- `maskSourceEffectiveness` (default 0.5) — fraction of the source's barrier resistance applied (emission side).
- `airwayImmunityFactor` (default 1.0) — how airway-dependent this vector is, gating gene immunity. `1.0` for airborne/social; set `0` for contact/flea vectors like plague proximity so breathless does not wrongly confer plague immunity.

A whole respiratory protection layer can be disabled by the player via the "masks reduce spread" setting.

### Gene whitelist: `RespiratoryImmunityDef`

A standalone, fully patchable `Def` (shipped as `Contagion_RespiratoryImmunity`) lists genes that grant airway immunity, since genes are off by default:

```xml
<Contagion.RespiratoryImmunityDef>
  <defName>Contagion_RespiratoryImmunity</defName>
  <geneProtections>
    <li><gene>VacuumResistance_Total</gene><protection>1.0</protection></li> <!-- breathless (Odyssey) -->
    <li><gene>ToxicEnvironmentResistance_Total</gene><protection>1.0</protection></li>
    <li><gene>ToxicEnvironmentResistance_Partial</gene><protection>0.5</protection></li>
  </geneProtections>
</Contagion.RespiratoryImmunityDef>
```

Genes are referenced by `defName` **as plain text**, not as a `GeneDef` cross-reference, so listing a Biotech/Odyssey gene is harmless when that DLC is absent — the entry is silently skipped at resolve time. Multiple defs are merged; players can `PatchOperation` the shipped def to add or remove entries. `protection` is clamped 0–1, where 1 is effectively immune to airway-based transmission (still scaled per-vector by `airwayImmunityFactor`).

---

## Transmission Equation

The complete per-candidate probability for any vector:

```
effectiveChance = vectorBaseChance
                × infectivityMultiplier(source)           ← incubation or active curve (or per-vector override)
                × sourceInfectivityProduct(source)        ← source factor list
                × seasonalMultiplier(map tile)            ← seasonal weight blend
                × susceptibilityProduct(target)           ← target factor list
                × vanillaContractFactor(target)           ← DiseaseContractChanceFactor
                × vectorContextModifiers(...)             ← distance, LOS, cleanliness, etc.
                × respiratoryMaskFactor(source, target)   ← respiratory vectors only (apparel/lung barrier + gene immunity)
                × spreadSuppression(disease, target)      ← colonist targets only, contagious vectors only
                × globalSettingsMultiplier                ← player's transmission rate slider × difficulty scale
```

Each term is independently tunable via XML. The engine multiplies them. A zero in any term blocks transmission.

---

## Seeder Improvements

### Cooldown and outbreak limiting

Current seeders have no protection against pile-on — `Seeder_Acausal` with `mtbDays=60` can theoretically fire twice in quick succession.

New seeder base fields:

```xml
<li Class="Contagion.Seeder_Acausal">
  <mtbDays>60</mtbDays>
  <cooldownDays>15</cooldownDays>         <!-- minimum gap between seed events -->
</li>
```

Profile-level outbreak limiting:

```xml
<maxActiveCases>3</maxActiveCases>        <!-- suppress ALL seeding when 3+ active cases exist -->
```

`maxActiveCases` counts pawns with the active disease hediff or incubation for this disease. When at or above the limit, all seeders for this profile are suppressed. This prevents the storyteller from piling infections onto an already-struggling colony.

Seeders can optionally override with their own `maxActiveCases` if one seeder should be more aggressive than another.

---

## Outbreak Notification

```xml
<outbreakNotification>FirstCase</outbreakNotification>
```

Options:

- `None` — silent. For environmental diseases where "outbreak" is just the biome being dangerous.
- `FirstCase` — letter on the first transmission event for this disease on this map. Resets when no active cases remain. **Default.**
- `EveryCase` — letter on every new transmission. Only useful for extremely lethal modded diseases.

---

## Corpse Contagion

```xml
<corpseContagious>true</corpseContagious>
<corpseInfectivityDecayPerDay>0.3</corpseInfectivityDecayPerDay>
```

When enabled, corpses of pawns who died with this disease are treated as contagious sources by proximity and fomite vectors. Infectivity decays daily. Burying or cremating removes them from the transmission pool.

Not used by any shipped disease in v1. Exists as an extensibility hook for modders making plague-pit or zombie-style diseases.

Default: false.

Hauling priority is not modified. Vanilla already handles corpse disposal through colonist needs and work priorities. The corpse inspect string should include contagion status for player awareness. If players don't bury plague corpses fast enough, that's a consequence of their prioritization — not something the mod should override.

---

## Cross-Species Transmission

The boolean `crossSpeciesTransmission` is replaced with a float `crossSpeciesTransmissionFactor`:

```xml
<crossSpeciesTransmissionFactor>0.3</crossSpeciesTransmissionFactor>
```

This multiplier applies whenever transmission crosses the human/animal species boundary. A value of 0.0 (default) means no cross-species transmission. A value of 1.0 means equal transmission rates across species. Intermediate values model the reduced efficiency of zoonotic jumps.

This is strictly more expressive than a boolean while keeping the common case simple — omit the field and species stay isolated.

---

## Immunity Hediff Override

```xml
<immunityHediffDef>Pathology_WaningFluImmunity</immunityHediffDef>
```

When specified, the engine applies this hediff on recovery instead of the default `Hediff_ContagionTemporaryImmunity`. If omitted, the built-in timer hediff is used (controlled by `immunityDurationDays`).

This field enables the planned sister mod to plug in complex immunity behavior — waning immunity curves, partial immunity from prior exposure, immune memory — without any Harmony patches on Contagion. The sister mod defines its own hediff class; the profile just points to it.

When `immunityHediffDef` is set, `immunityDurationDays` is ignored (the custom hediff manages its own duration).

---

## Carrier State (Reserved)

```xml
<carrierChance>0.05</carrierChance>
<carrierHediffDef>Contagion_FluCarrier</carrierHediffDef>
```

Reserved fields with no engine implementation in v1.

When implemented: on recovery from a disease with a `TransmissionProfile`, the engine rolls `carrierChance`. On success, instead of (or in addition to) applying the immunity hediff, it applies `carrierHediffDef` — a hidden hediff that makes the pawn an asymptomatic contagious source. The carrier hediff would have its own infectivity curve (likely flat and low).

This models Typhoid Mary dynamics: recovered pawns who remain contagious without symptoms. The schema is present so modders know the intent and can plan for it.

Default: `carrierChance = 0` (no carrier state).

---

## Caravan Placeholder

```xml
<spreadsDuringCaravan>false</spreadsDuringCaravan>
```

Reserved field. No implementation in v1. Default false. Present in the schema so modders know it exists and can set it for future compatibility.

---

## Incubation Design Notes

The current `Hediff_ContagionIncubation` uses severity to track progress (0.01 to 1.0 over incubation duration). The `incubationInfectivityCurve` evaluates against this severity value. Since the hediff is hidden (`Visible => false`), the severity-as-progress approach is invisible to the player and works cleanly.

If incubation visibility is added later (e.g. "??? disease" at low medical skill), severity display would confusingly show "progress" not "how sick they are." This could be solved by using a separate internal progress field and keeping severity at a fixed display value. Not needed for v1.

---

## Transmission Directionality (C# API Note)

Current `EvaluateCandidates(Pawn source, Map map)` is symmetric — any contagious pawn is a source, any eligible pawn is a target. Some future disease concepts want asymmetry: animals as permanent reservoirs, environmental sources that don't receive transmission.

Not needed for shipped diseases. The C# API should pass the profile to `EvaluateCandidates` so future vectors can check source/target roles:

```csharp
public abstract IEnumerable<(Pawn target, float chance)>
    EvaluateCandidates(Pawn source, Map map, TransmissionProfile profile,
                       PawnTransmissionContext sourceContext);
```

This is a C# API concern, not an XML schema field.

---

## Sister Mod Architecture

### Scope boundary

Contagion answers "how does a pawn get sick?" A planned sister mod (working title: Pathology) would answer "what happens after they're sick?" — disease progression, treatment mechanics, symptoms, complications, recovery dynamics. Vanilla handles all of that currently through the hediff severity/stage system, immunity gain, and tending. The sister mod adds granularity.

### Two-extension model

Both mods attach `DefModExtension` instances to the same `HediffDef`. They are independent — either works alone, both work together, modders can use one or both.

```xml
<HediffDef>
  <defName>Flu</defName>
  <!-- vanilla fields -->
  <modExtensions>
    <li Class="Contagion.TransmissionProfile">
      <!-- how you catch it — Contagion reads this -->
    </li>
    <li Class="Pathology.DiseaseProfile">
      <!-- what it does to you — sister mod reads this -->
    </li>
  </modExtensions>
</HediffDef>
```

No compile-time dependency between the mods. Communication is through hediffs on pawns and XML cross-references.

### Boundary between the two mods

| Concern | Owner | Why |
|---|---|---|
| Transmission vectors and seeding | Contagion | These are acquisition mechanics |
| Infectivity curves (source contagiousness) | Contagion | Drives the transmission engine |
| Susceptibility factors (target resistance) | Contagion | Gate on the transmission roll |
| Source infectivity factors | Contagion | Source-side modifiers for the transmission roll |
| Post-recovery immunity (simple timer) | Contagion | Prevents immediate reinfection |
| Post-recovery immunity (complex/waning) | Sister mod, via `immunityHediffDef` override | Contagion applies it; sister mod defines behavior |
| Carrier state | Contagion | Implemented by Contagion's hediff-removal hook |
| Severity progression curves | Sister mod | Changes what happens after infection, not how you get infected |
| Treatment effectiveness / drug interactions | Sister mod | Disease management, not transmission |
| Symptom decoupling / complications | Sister mod | Disease behavior, not transmission |
| Comorbidity interactions | Sister mod | Multi-disease interaction, not acquisition |

### Soft coupling via hediffs

The sister mod can affect transmission without a hard dependency:

1. Sister mod applies a hediff (e.g. `SymptomSuppressant`) to a pawn receiving treatment.
2. Contagion's `sourceInfectivityFactors` references that hediff with a reduced factor.
3. If the sister mod isn't installed, the hediff never appears, and the factor entry is inert.

This pattern works in both directions — the sister mod could also check for Contagion's incubation hediff to adjust its progression logic without importing Contagion's assembly.

### What Contagion should expose as soft API

For the sister mod to extend Contagion's behavior:

- `TransmissionProfile` and contained types remain `public`
- `DiseaseProfileCache` is accessible for profile lookups by hediff
- `Contagion_MapTransmissionComponent` exposes active case counts and transmission history queries
- The engine fires vanilla-compatible notifications or provides static event hooks for disease-contracted, disease-recovered, and carrier-state-entered

These are C# implementation concerns, not schema fields, but they inform the schema design.

---

## Revised Complete Field List

### TransmissionProfile (DefModExtension)

| Field | Type | Default | Purpose |
|---|---|---|---|
| `incubationDays` | float | 1.0 | Duration of hidden incubation phase |
| `immunityDurationDays` | float | 0 | Post-recovery reinfection protection (ignored when `immunityHediffDef` is set) |
| `immunityHediffDef` | HediffDef | null | Custom immunity hediff override; sister mod hook |
| `targetBodyParts` | List\<BodyPartDef\> | null | Part-targeted application (gut worms → stomach) |
| `incubationInfectivityCurve` | SimpleCurve | null (= flat 0.0) | Infectivity during incubation by progress |
| `activeInfectivityCurve` | SimpleCurve | null (= default bell) | Infectivity during active disease by severity |
| `seasonalInfectivity` | SeasonalInfectivity | null (= all 1.0) | Per-season transmission multiplier weights |
| `susceptibilityFactors` | List\<SusceptibilityFactor\> | null (= all equal) | Target-side resistance/vulnerability modifiers |
| `sourceInfectivityFactors` | List\<SourceInfectivityFactor\> | null (= all equal) | Source-side infectivity modifiers |
| `affectsHumans` | bool | true | Species scope |
| `affectsAnimals` | bool | false | Species scope |
| `crossSpeciesTransmissionFactor` | float | 0.0 | Multiplier when transmission crosses species boundary |
| `vectors` | List\<TransmissionVector\> | required | Spread mechanisms |
| `seeders` | List\<TransmissionSeeder\> | required | Outbreak initiation mechanisms |
| `maxActiveCases` | int | 0 (= no limit) | Suppress seeding above this count |
| `spreadSuppressionScale` | float | 1.0 | Per-disease scaling of colony spread suppression (0 = exempt) |
| `outbreakNotification` | enum | FirstCase | Player notification on transmission events |
| `corpseContagious` | bool | false | Dead pawns as contagion sources |
| `corpseInfectivityDecayPerDay` | float | 0.5 | Daily decay of corpse infectivity |
| `carrierChance` | float | 0.0 | Reserved: probability of becoming asymptomatic carrier on recovery |
| `carrierHediffDef` | HediffDef | null | Reserved: hediff applied to carriers |
| `spreadsDuringCaravan` | bool | false | Reserved for future caravan support |

### SeasonalInfectivity

| Field | Type | Default | Purpose |
|---|---|---|---|
| `spring` | float | 1.0 | Multiplier during spring |
| `summer` | float | 1.0 | Multiplier during summer |
| `fall` | float | 1.0 | Multiplier during fall |
| `winter` | float | 1.0 | Multiplier during winter |
| `permanentSummer` | float | 1.0 | Multiplier on equatorial tiles |
| `permanentWinter` | float | 1.0 | Multiplier on polar tiles |

### Removed fields

- `contagiousMinSeverity` — subsumed by `activeInfectivityCurve` evaluating to zero
- `contagiousMaxSeverity` — subsumed by `activeInfectivityCurve` evaluating to zero
- `contagiousDuringIncubation` — subsumed by `incubationInfectivityCurve` being nonzero
- `crossSpeciesTransmission` (bool) — replaced by `crossSpeciesTransmissionFactor` (float)
- `partsToAffect` — renamed to `targetBodyParts` for clarity

---

## Minimal Modder Example

A modder who just wants "this disease is contagious, airborne, seeded by visitors":

```xml
<li Class="Contagion.TransmissionProfile">
  <incubationDays>2</incubationDays>
  <immunityDurationDays>10</immunityDurationDays>
  <vectors>
    <li Class="Contagion.Vector_Airborne">
      <baseChancePerCheck>0.03</baseChancePerCheck>
    </li>
  </vectors>
  <seeders>
    <li Class="Contagion.Seeder_Arrival">
      <arrivalChance>0.01</arrivalChance>
    </li>
  </seeders>
</li>
```

No curves, no factors, no advanced fields. Works with sensible defaults. Eight lines of XML to make any hediff contagious.

## Full-Featured Example (Flu)

```xml
<li Class="Contagion.TransmissionProfile">
  <incubationDays>1.5</incubationDays>
  <immunityDurationDays>15</immunityDurationDays>
  <affectsHumans>true</affectsHumans>

  <incubationInfectivityCurve>
    <points>
      <li>(0.0, 0.0)</li>
      <li>(0.3, 0.05)</li>
      <li>(0.7, 0.15)</li>
      <li>(1.0, 0.3)</li>
    </points>
  </incubationInfectivityCurve>

  <activeInfectivityCurve>
    <points>
      <li>(0.00, 0.4)</li>
      <li>(0.25, 1.0)</li>
      <li>(0.55, 1.0)</li>
      <li>(0.80, 0.3)</li>
      <li>(1.00, 0.0)</li>
    </points>
  </activeInfectivityCurve>

  <seasonalInfectivity>
    <spring>0.6</spring>
    <summer>0.3</summer>
    <fall>0.8</fall>
    <winter>1.0</winter>
    <permanentSummer>0.4</permanentSummer>
    <permanentWinter>0.7</permanentWinter>
  </seasonalInfectivity>

  <susceptibilityFactors>
    <li Class="Contagion.Factor_AgeRange">
      <minAge>0</minAge>
      <maxAge>7</maxAge>
      <factor>1.5</factor>
    </li>
  </susceptibilityFactors>

  <vectors>
    <li Class="Contagion.Vector_Airborne">
      <baseChancePerCheck>0.03</baseChancePerCheck>
      <outdoorFactor>0.15</outdoorFactor>
      <maxRange>15</maxRange>
      <distanceFalloffRate>0.25</distanceFalloffRate>
      <obstructedFactor>0.0</obstructedFactor>
    </li>
    <li Class="Contagion.Vector_Social">
      <baseChancePerInteraction>0.02</baseChancePerInteraction>
      <outdoorFactor>0.5</outdoorFactor>
    </li>
    <li Class="Contagion.Vector_Fomite">
      <contaminatesVomit>true</contaminatesVomit>
      <baseChancePerContact>0.03</baseChancePerContact>
      <potencyDecayPerHour>0.1</potencyDecayPerHour>
      <activeInfectivityCurveOverride>
        <points>
          <li>(0.50, 0.0)</li>
          <li>(0.65, 0.5)</li>
          <li>(0.80, 1.0)</li>
          <li>(1.00, 0.5)</li>
        </points>
      </activeInfectivityCurveOverride>
    </li>
  </vectors>

  <seeders>
    <li Class="Contagion.Seeder_Storyteller">
      <seedCountRange>1~1</seedCountRange>
    </li>
    <li Class="Contagion.Seeder_Arrival">
      <arrivalChance>0.01</arrivalChance>
    </li>
  </seeders>

  <maxActiveCases>5</maxActiveCases>
  <outbreakNotification>FirstCase</outbreakNotification>
</li>
```

---

## Future Vector Types (Not In v1)

These are documented as natural extensions of the schema, not current work:

- **`Vector_Combat` / `Vector_MeleeDamage`** — transmission via bites or melee attacks. Enables rage viruses, scaria-like contagion, zombie plagues. Needs its own design pass for hook points and interaction with the combat system.
- **`Vector_Pregnancy`** — mother-to-child transmission during pregnancy or birth. Biotech DLC dependent. Niche but a natural extension for modders adding realistic disease models.

---

## Food Poisoning: Out of Scope

Contagious food poisoning is not shipped in v1. Vanilla food poisoning is already a well-functioning consequence of dirty kitchens and unskilled cooks. Making it contagious would change the fundamental gameplay loop around food safety without clear benefit. The `Vector_Foodborne` vector handles the real use case — a flu-infected cook contaminating meals — through the existing foodborne transmission path.

Modders who want contagious food poisoning can create a custom `HediffDef` with a `TransmissionProfile` — the system fully supports it.

---

## Migration Path

The current `TransmissionProfile.cs` and `Contagion_Profiles.xml` are updated together:

1. Add new fields to `TransmissionProfile.cs` with defaults that preserve current behavior.
2. Add `SusceptibilityFactor` and `SourceInfectivityFactor` base classes and shipped factor types.
3. Add `SeasonalInfectivity` class with all-1.0 defaults.
4. Add `obstructedFactor` to `Vector_Airborne`, `outdoorFilthRadius` to `Vector_Proximity`.
5. Update `Contagion_Profiles.xml` to replace `contagiousMinSeverity`/`contagiousMaxSeverity`/`contagiousDuringIncubation` with curves for each shipped disease.
6. Rename `partsToAffect` to `targetBodyParts`.
7. Replace `crossSpeciesTransmission` (bool) with `crossSpeciesTransmissionFactor` (float).
8. Add reserved fields (`carrierChance`, `carrierHediffDef`, `spreadsDuringCaravan`, `immunityHediffDef`).
9. Remove the deprecated fields from `TransmissionProfile.cs`.
10. Update the transmission engine to use the new equation (curves × factors × LOS).

Since the mod is pre-release, there is no save compatibility concern. Do the full migration in one pass.
