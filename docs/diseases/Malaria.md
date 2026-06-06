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
| Contagion incubation | 1 day |
| Vanilla lethal severity | 1.0 |
| Vanilla tend cycle | 10 h |
| Vanilla immunity | `immunityPerDaySick 0.5`, `severityPerDayNotImmune 0.5` — close race |
| Contagion immunity | 30 days post-recovery (`immunityDurationDays 30`) |

---

## Vanilla Disease Characteristics

Malaria is a recurring immunity-race disease. The vanilla immunity race is tight — `severityPerDayNotImmune 0.5` vs. `immunityPerDaySick 0.5`. Tended pawns typically survive but the margin is slim. Recurring malaria (vanilla `immunityPerDayNotSick −0.02`) means vanilla immunity slowly decays after recovery. On top of that, Contagion grants a 30-day post-recovery immunity (`immunityDurationDays 30`), so a recovered pawn is protected from re-acquisition for ~half a year before the environment can reinfect them. Penoxycyline is the primary prevention tool.

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
| baseChancePerCheck | 0.0040 | Per 2500-tick pass |
| minTemperature | 16°C | Mosquitoes inactive below 16°C |
| peakTemperature | 30°C | Tropical peak |
| waterProximityRadius | 10 | Water bodies within 10 cells drive risk |
| waterProximityWeight | 0.02 | Moderate water effect |
| indoorReductionPerCellFromEdge | 0.15 | Shelter as bed-net substitute; ~0.6x in a typical 5x5 room, full protection ~7 cells deep |
| coolRoomThreshold | 22°C | Air-conditioned rooms reduce mosquito activity |
| timeOfDayActivityCurve | night-peaked | Anopheles bite dusk→dawn; midday is the low. Averages ~1.0/day, so it redistributes risk across the clock rather than changing the daily total |

**Time of day.** Mosquitoes peak from dusk through pre-dawn (curve ≈1.5x at night, ≈0.3x at midday). Because the curve averages ~1.0 over 24 hours, an always-outdoors pawn is unchanged, but **when** a pawn is outside now matters: a day-worker who sleeps under a roof at night dodges the peak, while a night-shift worker or an unsheltered sleeper takes the brunt. Sim (7-day window, 10 pawns): always-outdoors ≈3.6 infections, day-out/sheltered-at-night ≈2.6, night-shift ≈3.4.

---

## Infectivity

There is no source infectivity — malaria does not spread person-to-person. No active infectivity curve, no source factors, no incubation infectivity.

### Seasonal environmental pressure

Malaria's Contagion-mode environmental seeding pressure is strongly summer-weighted. There is no person-to-person spread, and the seasonal multiplier is not part of any ongoing transmission equation.

| Season | Multiplier |
|---|---|
| Summer | 1.2 |
| Fall | 1.0 |
| Spring | 0.8 |
| Winter | 0.3 |
| Permanent summer | 1.0 |
| Permanent winter | 0.1 |

The 0.3 winter multiplier makes Contagion-mode malaria introduction possible but uncommon in cold winters. The 0.1 permanent winter multiplier means arctic colonies see essentially no organic malaria pressure. Tropical colonies (permanent summer) have sustained malaria risk year-round.

---

## Suppression and Caps

| Field | Value | Notes |
|---|---|---|
| useScaledActiveCaseCap | **true** | Enables the colony-fraction balance cap (~30–50% of affectable colonists) |
| spreadSuppressionScale | **1.0** | On — a game-balance guarantee, not a transmission model |
| outbreakNotification | **FirstCase** | Red environmental-source letter on the first visible case; later visible cases update a yellow cluster letter while the outbreak remains active |

The first visible malaria case identifies the local environment as the likely source. Subsequent visible cases update the active outbreak's cluster letter rather than remaining silent.

Suppression is on as a **balance** guarantee, not because malaria spreads colonist-to-colonist (it doesn't — the source is the map). As the colony nears the scaled active-case cap (~half), environmental acquisition is dampened via the same suppression term (applied to the environmental vector centrally in `BuildSeederChance`), so a bad location — riverside colony in summer — can no longer creep toward infecting *everyone*. The per-event `infectionBudget` (2–5 in Mode 1) still bounds each window on top of this. `suppressionMode` *Let 'er rip* disables the cap.

---

## Counterplay

- **Penoxycyline** — the primary tool. Reduces `DiseaseContractChanceFactor` significantly.
- **Indoor work** — pawns who spend most time indoors have dramatically reduced exposure. Outdoor workers (miners, farmers, hunters) are at highest risk.
- **Sleep under a roof** — the night-peaked activity curve means a roofed bedroom (ideally deep / mountain interior) covers the hours of highest mosquito activity. A day-worker who sleeps inside is meaningfully safer than a night-shift worker; avoid scheduling outdoor labor into the dusk-to-dawn window.
- **Air conditioning / cool rooms** — rooms cooled below `coolRoomThreshold 22°C` suppress the vector.
- **Distance from water** — map placement matters. A river delta base has continuous pressure; an arid highland base has nearly none.
- **No transmission counterplay** — there is no person-to-person spread to contain. Isolation, masks, and cleaning have no effect.

---

## Tuning Notes

- `baseChancePerCheck 0.0040` is meaningfully higher than gut worms or muscle parasites. In a tropical riverside location, malaria should feel like a persistent seasonal threat. At peak heat + wet tiles, an always-outdoors window saturates the budget (sim: 100% of trials hit the cap); a sheltered day-worker is held to ~1.3 of ~3.9 budget — real but survivable risk.
- The 16°C minimum temperature gate is the primary climate filter. For tiles that never reach 16°C outdoors, malaria simply never fires. This creates a clean geographic distinction without biome-gating logic.
- `indoorReductionPerCellFromEdge 0.15` is the lower of the two flying-insect vectors (sleeping sickness is 0.18). Malaria mosquitoes are most active at dawn/dusk/night and actively seek humans, so a roof helps but does not seal them out the way it does the daytime tsetse fly. A typical 5x5 room (≈2–3 cells deep) lands near 0.6x outdoor risk; full protection needs ≈7 cells of depth from the nearest unroofed cell. Shelter is our stand-in for bed nets, which the engine cannot model directly. (Was 0.08 — too weak; a normal roofed colony sat at ~0.7x and only a 13-cell-deep interior reached immunity.)
- `coolRoomThreshold 22°C` means even a moderately cooled room (under 22°C) counts as cool. A lightly climate-controlled bedroom provides meaningful protection. This may be too generous; consider raising to 18°C to require actual AC investment.
- `Seeder_Acausal mtbDays 180 / cooldownDays 10` exists only as the Storyteller-window expiry fallback. It does not run as an independent Mode 2 seeder.
- Arctic and arid colonies rarely get malaria organically in Contagion-driven mode because the environmental source is absent or suppressed. That is intentional; prevention through climate, distance from water, and indoor cooling should stand.
