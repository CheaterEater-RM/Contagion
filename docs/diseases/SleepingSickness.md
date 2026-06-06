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
| Contagion incubation | 1 day |
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
| baseChancePerCheck | 0.0030 | Per 2500-tick pass — lower than malaria (0.0040) |
| minTemperature | **20°C** | Higher floor than malaria (16°C) — tsetse requires genuine warmth |
| peakTemperature | **32°C** | Peak at tropical heat |
| waterProximityRadius | 12 | Wide range — tsetse breeds near rivers and wetlands |
| waterProximityWeight | 0.035 | Stronger water dependency than malaria |
| indoorReductionPerCellFromEdge | 0.18 | Strongest of the two flying-insect vectors — daytime tsetse are well blocked by a roof (full protection ≈6 cells deep) |
| coolRoomThreshold | **24°C** | Higher threshold — requires actual tropical cooling to suppress |
| timeOfDayActivityCurve | day-peaked | Tsetse bite by day (≈2x at midday, ≈0.3x at night). Averages ~1.0/day — the inverse of malaria |

**Time of day.** Tsetse are daytime biters: risk peaks at midday and is near-zero after dark. The curve averages ~1.0 over 24 hours, so an always-outdoors pawn is unchanged, but scheduling outdoor work to the night — or simply being indoors during the day — is a real defense. Sim (7-day window, 10 pawns): always-outdoors ≈2.9 infections, day-shift ≈2.4, **night-shift ≈1.2** (about 0.4x of always — meaningful protection, but no longer a near-perfect dodge). This is the mirror image of malaria, where night sleep is the defense.

---

## Infectivity

No source infectivity. No person-to-person spread. No infectivity curves or source factors.

### Seasonal environmental pressure

Sleeping sickness has the most tropics-locked Contagion-mode environmental seeding pressure in the profile. There is no person-to-person spread, and the seasonal multiplier is not part of any ongoing transmission equation.

| Season | Multiplier |
|---|---|
| Summer | 1.2 |
| Fall | 1.0 |
| Spring | 0.9 |
| Winter | 0.2 |
| Permanent summer | **1.1** |
| Permanent winter | 0.1 |

The `permanentSummer 1.1` multiplier (above the 1.0 summer multiplier) reflects that constant tropical heat creates ideal introduction pressure year-round, even slightly better than a temperate summer. The 0.2 winter multiplier means sleeping sickness is extremely rare in cold seasons at any latitude.

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
| Base check chance | 0.0040 | 0.0030 |
| Min temperature | 16°C | 20°C |
| Peak temperature | 30°C | 32°C |
| Water proximity radius | 10 | 12 |
| Water proximity weight | 0.02 | 0.035 |
| Indoor reduction | 0.15/cell | 0.18/cell |
| Cool room threshold | 22°C | 24°C |
| Time of day | night-peaked (sleep inside) | day-peaked (work at night) |
| Permanent summer multiplier | 1.0 | 1.1 |
| Seeder multiplier | 1.0 | 0.75 |
| Overall feel | Common warm-climate hazard | Rare deep-tropics hazard |

---

## Counterplay

Similar to malaria, but with stronger rewards for staying out of wet tropical habitat.

- **Penoxycyline** — primary prevention tool.
- **Indoor work** — standard shelter provides more protection against sleeping sickness than malaria (0.18 vs. 0.15 per cell). Tsetse bite outdoors during the day and rest in vegetation, so a roof over the colonist's head is a stronger shield than against night-biting mosquitoes. A typical 5x5 room cuts risk to ~0.5x; ≈6 cells of depth gives full protection.
- **Climate control** — the 24°C cool room threshold means AC must bring rooms below 24°C to help. A lightly cooled room may not qualify.
- **Night-shift outdoor work** — the day-peaked activity curve makes scheduling outdoor labor (hunting, hauling, farming) into the night a strong, free defense: night-shift outdoor exposure runs roughly half of day-shift (≈0.4x of always-outdoors). The mirror image of malaria, where the defense is sleeping inside at night.
- **Wetland avoidance** — riverside farming, fishing-style work zones, and outdoor paths through wet tropical areas should feel riskier than dry, enclosed movement.
- **Biome selection** — sleeping sickness is effectively absent from temperate and cold biomes. It is a deliberate hazard of colonising tropical tiles.

---

## Tuning Notes

- `baseChancePerCheck 0.0030` is lower than malaria (0.0040). Sleeping sickness should feel like something that occasionally appears in a tropical colony — serious when it hits but not a constant pressure. At peak heat + wet tiles, an always-outdoors window reaches ~92% of the budget; a night-shift schedule holds it to ~0.7 of ~3.7 budget.
- The 20°C minimum and 32°C peak create a very narrow climate window compared to malaria. Most temperate biomes never hit the minimum long enough for sustained risk. This is intentional — sleeping sickness is a premium on tropical biome colonisation — but may be over-tuned if it effectively never appears outside equatorial biomes.
- `permanentSummer 1.1` gives equatorial tiles slightly higher sleeping sickness than standard summer. This is a deliberate distinction: equatorial colonies should feel uniquely pressured by this disease in ways that northern/southern temperate colonies are not, even in summer.
- Seeder `baseChanceMultiplier 0.75` reduces sleeping sickness frequency compared to malaria even when conditions are otherwise identical. Combined with the higher temperature floor, sleeping sickness events should be roughly half as frequent as malaria in the same biome. If it feels too rare, consider raising to 0.85–0.9.
- `Seeder_Acausal mtbDays 240 / cooldownDays 15` exists only as the Storyteller-window expiry fallback. It does not run as an independent Mode 2 seeder.
