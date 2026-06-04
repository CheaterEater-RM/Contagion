# Sleeping Sickness — Contagion Profile

Tsetse-fly-borne protozoan disease. Hotter and wetter than malaria — this is a deep-tropics disease that requires high temperatures. Slower environmental exposure rate than malaria but more insidious: sleeping sickness degrades consciousness dramatically, incapacitating pawns before it kills them. No person-to-person spread.

---

## Identity

| Field | Value |
|---|---|
| HediffDef | `SleepingSickness` |
| Animal variant | none |
| Species | Human only |
| Vanilla incident | `Disease_SleepingSickness` |
| Vanilla lethal severity | 1.0 |
| Vanilla tend cycle | 10 h |
| Vanilla immunity | `immunityPerDaySick 0.4`, `severityPerDayNotImmune 0.45` — slower than malaria |

---

## Vanilla Disease Characteristics

Sleeping sickness is slower and more debilitating than malaria. The immunity race is slightly less aggressive (`severityPerDayNotImmune 0.45`) but the consciousness penalties are severe — advanced cases render pawns effectively comatose. The 10 h tend window means regular medical attention is required. Vanilla immunity decay means prior exposure provides minimal long-term protection.

---

## How It Enters the Colony

### Mode 1 (Storyteller-driven)
Storyteller fires `Disease_SleepingSickness` → pending event with 14-day window, infection budget 2–5. Resolves via environmental window only (same pattern as malaria).

No arrival fulfillment. If the environmental window expires with budget still unspent, Storyteller mode uses `Seeder_Acausal` as a final fallback. This keeps the storyteller contract intact: when the storyteller schedules sleeping sickness, someone gets sick somehow, even if the map never produced a good tsetse exposure during the window.

### Mode 2 (Contagion-driven)
- Continuous environmental exposure. No Storyteller seeder and no acausal MTB.
- Storyteller incident cancelled.

**Seeder multiplier:** `baseChanceMultiplier 0.75` — sleeping sickness fires slightly less aggressively than malaria's 1.0 even in ideal conditions. It should feel rarer and more severe when it arrives.

**Environmental seeder cooldown:** 7 days. Sleeping sickness backs off more than malaria after a successful environmental seed because it is meant to be rarer and more severe.

---

## Spread Vectors

### Vector_Environmental (only vector)

Sleeping sickness represents tsetse habitat pressure rather than generic mosquito pressure. It is hotter, more river/wetland dependent, and more strongly blocked by deep indoor shelter than malaria.

| Parameter | Value | Notes |
|---|---|---|
| baseChancePerCheck | 0.0022 | Per 2500-tick pass — lower than malaria (0.0035) |
| minTemperature | **20°C** | Higher floor than malaria (16°C) — tsetse requires genuine warmth |
| peakTemperature | **32°C** | Peak at tropical heat |
| waterProximityRadius | 12 | Wide range — tsetse breeds near rivers and wetlands |
| waterProximityWeight | 0.035 | Stronger water dependency than malaria |
| indoorReductionPerCellFromEdge | 0.15 | Strong indoor shelter; deep interior rooms are much safer |
| coolRoomThreshold | **24°C** | Higher threshold — requires actual tropical cooling to suppress |

---

## Infectivity

No source infectivity. No person-to-person spread. No infectivity curves or source factors.

### Seasonal variation

Sleeping sickness is the most tropics-locked disease in the profile.

| Season | Multiplier |
|---|---|
| Summer | 1.2 |
| Fall | 1.0 |
| Spring | 0.9 |
| Winter | 0.2 |
| Permanent summer | **1.1** |
| Permanent winter | 0.1 |

The `permanentSummer 1.1` multiplier (above the 1.0 summer multiplier) reflects that constant tropical heat creates ideal conditions year-round, even slightly better than a temperate summer. The 0.2 winter multiplier means sleeping sickness is extremely rare in cold seasons at any latitude.

---

## Suppression and Caps

| Field | Value | Notes |
|---|---|---|
| useScaledActiveCaseCap | **true** | Enables the colony-fraction balance cap (~30–50% of affectable colonists) |
| spreadSuppressionScale | **1.0** | On — a game-balance guarantee, not a transmission model |
| outbreakNotification | **FirstCase** | Red environmental-source letter on the first visible case; later visible cases update a yellow cluster letter while the outbreak remains active |

The first visible sleeping-sickness case identifies the local environment as the likely source. Subsequent visible cases update the active outbreak's cluster letter rather than remaining silent.

Suppression is on as a **balance** guarantee (see [Malaria.md](Malaria.md) → Suppression and Caps): even though sleeping sickness is map-seeded with no person-to-person spread, the colony-fraction cap dampens environmental acquisition as the colony nears ~half infected, applied to the environmental vector centrally in `BuildSeederChance`. The per-event `infectionBudget` still bounds each window. `suppressionMode` *Let 'er rip* disables the cap.

---

## Comparison: Malaria vs. Sleeping Sickness

| Parameter | Malaria | Sleeping Sickness |
|---|---|---|
| Base check chance | 0.0035 | 0.0022 |
| Min temperature | 16°C | 20°C |
| Peak temperature | 30°C | 32°C |
| Water proximity radius | 10 | 12 |
| Water proximity weight | 0.02 | 0.035 |
| Indoor reduction | 0.08/cell | 0.15/cell |
| Cool room threshold | 22°C | 24°C |
| Permanent summer multiplier | 1.0 | 1.1 |
| Seeder multiplier | 1.0 | 0.75 |
| Overall feel | Common warm-climate hazard | Rare deep-tropics hazard |

---

## Counterplay

Similar to malaria, but with stronger rewards for staying out of wet tropical habitat.

- **Penoxycyline** — primary prevention tool.
- **Indoor work** — standard shelter provides more protection against sleeping sickness than malaria (0.15 vs. 0.08 per cell). Deeper indoor positioning helps a lot.
- **Climate control** — the 24°C cool room threshold means AC must bring rooms below 24°C to help. A lightly cooled room may not qualify.
- **Wetland avoidance** — riverside farming, fishing-style work zones, and outdoor paths through wet tropical areas should feel riskier than dry, enclosed movement.
- **Biome selection** — sleeping sickness is effectively absent from temperate and cold biomes. It is a deliberate hazard of colonising tropical tiles.

---

## Tuning Notes

- `baseChancePerCheck 0.0022` is lower than malaria. Sleeping sickness should feel like something that occasionally appears in a tropical colony — serious when it hits but not a constant pressure. If it never fires in practice, consider 0.0025-0.003.
- The 20°C minimum and 32°C peak create a very narrow climate window compared to malaria. Most temperate biomes never hit the minimum long enough for sustained risk. This is intentional — sleeping sickness is a premium on tropical biome colonisation — but may be over-tuned if it effectively never appears outside equatorial biomes.
- `permanentSummer 1.1` gives equatorial tiles slightly higher sleeping sickness than standard summer. This is a deliberate distinction: equatorial colonies should feel uniquely pressured by this disease in ways that northern/southern temperate colonies are not, even in summer.
- Seeder `baseChanceMultiplier 0.75` reduces sleeping sickness frequency compared to malaria even when conditions are otherwise identical. Combined with the higher temperature floor, sleeping sickness events should be roughly half as frequent as malaria in the same biome. If it feels too rare, consider raising to 0.85–0.9.
- `Seeder_Acausal mtbDays 240 / cooldownDays 15` exists only as the Storyteller-window expiry fallback. It does not run as an independent Mode 2 seeder.
