# Malaria — Contagion Profile

Mosquito-borne disease seeded by warm, wet environments. No person-to-person spread whatsoever — the map itself is the source. High water proximity and outdoor exposure dramatically increase risk. Biome-gated: cold biomes effectively never see it naturally.

---

## Identity

| Field | Value |
|---|---|
| HediffDef | `Malaria` |
| Animal variant | none |
| Species | Human only |
| Vanilla incident | `Disease_Malaria` |
| Vanilla lethal severity | 1.0 |
| Vanilla tend cycle | 10 h |
| Vanilla immunity | `immunityPerDaySick 0.5`, `severityPerDayNotImmune 0.5` — close race |

---

## Vanilla Disease Characteristics

Malaria is a recurring immunity-race disease. The vanilla immunity race is tight — `severityPerDayNotImmune 0.5` vs. `immunityPerDaySick 0.5`. Tended pawns typically survive but the margin is slim. Recurring malaria (vanilla `immunityPerDayNotSick −0.02`) means immunity slowly decays after recovery; a pawn who had malaria once is at risk again within 30 days. Penoxycyline is the primary prevention tool.

---

## How It Enters the Colony

### Mode 1 (Storyteller-driven)
Storyteller fires `Disease_Malaria` → pending event created. Resolves via an **environmental window** with a 14-day window and infection budget 2–5.

Mosquitoes bite pawns with outdoor access near standing water. Once the budget is spent or the window closes, the event clears. This matches vanilla's "some pawns get malaria then the event ends" feel.

No arrival fulfillment. If the environmental window expires with budget still unspent, Storyteller mode uses `Seeder_Acausal` as a final fallback: the storyteller said someone gets sick, so the disease lands silently on eligible pawns instead of disappearing. This fallback is deliberately isolated to Storyteller mode and does not create a continuous random disease source.

### Mode 2 (Contagion-driven)
- **Environmental exposure** runs continuously; no window-bound budget.
- No Storyteller seeder and no acausal MTB. Malaria is a pure environmental disease in Mode 2.
- Storyteller incident cancelled; continuous environmental pressure runs at biome rate.

---

## Spread Vectors

### Vector_Environmental (only vector)

The map environment is the source. Pawns with outdoor access near water accumulate risk each 2500-tick environmental pass. Malaria represents broad mosquito pressure: warm water matters most, but shallow rooms and open structures are not perfect protection.

| Parameter | Value | Notes |
|---|---|---|
| baseChancePerCheck | 0.0035 | Per 2500-tick pass |
| minTemperature | 16°C | Mosquitoes inactive below 16°C |
| peakTemperature | 30°C | Tropical peak |
| waterProximityRadius | 10 | Water bodies within 10 cells drive risk |
| waterProximityWeight | 0.02 | Moderate water effect |
| indoorReductionPerCellFromEdge | 0.08 | Indoor shelter reduces exposure; deep indoors is mostly safe |
| coolRoomThreshold | 22°C | Air-conditioned rooms reduce mosquito activity |

---

## Infectivity

There is no source infectivity — malaria does not spread person-to-person. No active infectivity curve, no source factors, no incubation infectivity.

### Seasonal variation

Malaria is strongly summer-weighted.

| Season | Multiplier |
|---|---|
| Summer | 1.2 |
| Fall | 1.0 |
| Spring | 0.8 |
| Winter | 0.3 |
| Permanent summer | 1.0 |
| Permanent winter | 0.1 |

The 0.3 winter multiplier makes malaria possible but uncommon in cold winters. The 0.1 permanent winter multiplier means arctic colonies see essentially no malaria. Tropical colonies (permanent summer) have sustained malaria risk year-round.

---

## Suppression and Caps

| Field | Value | Notes |
|---|---|---|
| maxActiveCases | 0 (none) | No active case cap — environmental exposure continues regardless |
| spreadSuppressionScale | **0** | Disabled — source is the map, not infected colonists |
| outbreakNotification | **None** | Silent. No letter. Discovery via health inspection |

No active case cap: the environment doesn't care how many colonists are already sick. A particularly bad location (riverside colony in summer) could in theory infect the entire colony. The `infectionBudget` on the seeder (2–5 in Mode 1) provides the event-level cap.

---

## Counterplay

- **Penoxycyline** — the primary tool. Reduces `DiseaseContractChanceFactor` significantly.
- **Indoor work** — pawns who spend most time indoors have dramatically reduced exposure. Outdoor workers (miners, farmers, hunters) are at highest risk.
- **Air conditioning / cool rooms** — rooms cooled below `coolRoomThreshold 22°C` suppress the vector.
- **Distance from water** — map placement matters. A river delta base has continuous pressure; an arid highland base has nearly none.
- **No transmission counterplay** — there is no person-to-person spread to contain. Isolation, masks, and cleaning have no effect.

---

## Tuning Notes

- `baseChancePerCheck 0.0035` is meaningfully higher than gut worms or muscle parasites. In a tropical riverside location, malaria should feel like a persistent seasonal threat, not a rare event. This may still be too low for classic "malaria swamp" gameplay — consider 0.004–0.005 for high-water, high-temperature tiles.
- The 16°C minimum temperature gate is the primary climate filter. For tiles that never reach 16°C outdoors, malaria simply never fires. This creates a clean geographic distinction without biome-gating logic.
- `indoorReductionPerCellFromEdge 0.08` is gentler than gut worms (0.15). Mosquitoes can penetrate slightly deeper into structures before being fully blocked. A colony where pawns work in shallow or open-roofed areas should still take some hits even indoors.
- `coolRoomThreshold 22°C` means even a moderately cooled room (under 22°C) counts as cool. A lightly climate-controlled bedroom provides meaningful protection. This may be too generous; consider raising to 18°C to require actual AC investment.
- `Seeder_Acausal mtbDays 180 / cooldownDays 10` exists only as the Storyteller-window expiry fallback. It does not run as an independent Mode 2 seeder.
- Arctic and arid colonies rarely get malaria organically in Contagion-driven mode because the environmental source is absent or suppressed. That is intentional; prevention through climate, distance from water, and indoor cooling should stand.
