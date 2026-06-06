# Apparel Protection — Design

*Contagion · RimWorld 1.6 · `net48` · Harmony 2.4.x*
*Status: design proposal, pre-implementation. v3 — adds tech-level sealing, durability/seal-integrity, and the dedicated-vs-incidental split. Supersedes the earlier two-factor draft.*

---

## 1. Why this matters

Protective gear is the main way a player can **play** Contagion instead of only reacting to it. The
storyteller throws a flu or a plague corpse at the colony; the player's answer is loadouts, masks,
sealed suits, gloves, and who handles the bodies. If that answer is legible and fair, outbreaks become
a system to master. **Giving the player good tools to fight disease is the single biggest lever on
whether the mod feels fair.**

Two goals drive every decision:

- **Player fairness/legibility.** *"If my pawn got sick wearing this, would I be upset?"* Full cataphract
  letting flu in → upset. Recon → "let me check the rate." Ideally the player predicts the broad strokes
  without reading anything (a mask helps flu, not fleas).
- **Modder extensibility.** Gloves → big fomite/fluid protection automatically. A sealed hazmat suit →
  immunity automatically. Neither touches Contagion's code; both can override our guesses.

---

## 2. Decisions locked

1. **Helmets are sole airway protection.** An enclosed combat/space helmet (recon/marine/cataphract/vac)
   alone seals the airway → immune to airborne respiratory vectors without the matching suit. Masks stay
   filtering/two-sided.
2. **Sealed-system capstone** at aggregate `VacuumResistance ≥ 0.95` **or** the DLC-independent
   "fully sealed" path (§3.3). Confirmed against defs: every spacer set lands 0.95–1.01.
3. **`Vector_Foodborne` (eater) takes no gear protection.** Supply-chain play covers it.
4. **`barrierFloor` → `unsealedEffectiveness`.** Values: flea 0.2, fomite 0.6, fluid 0.4, environmental 0.4.
5. **`Vector_FecalOralLiving` gets light, fomite-style protection** (feet-leaning).
6. **Negative-seal "filthy garment harbors fleas" → backlog.**
7. **Cook→food "Typhoid Mary" protection is core** (§5).
8. **Assumed extremity coverage** for sealed full-body suits (§3.1, §9) — disease calc only, never combat.
9. **Legibility is a designed subsystem** (§8).
10. **Spacer armor seals; tech level gates sealing** (§3.5). Recon/marine/cataphract/vacsuit all sealed in
    both paths; medieval-and-below can never seal; industrial seals only with an explicit signal.
11. **Seal values** (§6): cataphract armor 1.0, marine 0.95, recon 0.90, vacsuit 1.0 — all ≥ 0.9, so all
    hit the fully-sealed path.
12. **Durability degrades incidental seals** (§7), gated on the vanilla ratty/tattered states; dedicated
    PPE (vacsuit/hazmat) keeps its seal to 0 HP.

---

## 3. The model

### 3.1 Channels (and assumed extremity coverage)

Four protection **channels**: `airway`, `skin`, `hands`, `feet`. Each worn item contributes:

- **coverage** — *automatic*, never authored. From `bodyPartGroups` ∩ the channel's parts, weighted by
  `coverageAbs` (`cachedHumanBodyCoverage`). DLC- and CE-independent.
- **seal** — *authored or derived* (§3.5). 0 = porous fabric, 1 = hermetic.

`hands`/`feet` are **empty in vanilla** (and in the CE Armors submod) — correct, since a pawn handles a
corpse bare-handed. A modder's gloves fill the channel and protection appears automatically, no
Contagion code. CE proper and small "suits cover hands" mods *do* fill them; Contagion uses the real
`coverageAbs` when present.

**Assumed extremity coverage.** A vacsuit or power-armor suit obviously includes gauntlets/boots, yet
vanilla covers no hands/feet. So for **disease calc only**, Contagion credits the `hands`/`feet` channels
for sealed full-body suits, at the suit's `skin` seal. Never touches `ArmorRating`/combat coverage. Scope
is per-suit (a sealed suit yes; a duster no — see §9).

### 3.2 `unsealedEffectiveness` + the formula

Ceiling is **1.0** (a complete seal is genuine immunity); no separate per-vector cap. One differentiator:

- **`unsealedEffectiveness`** (per vector, 0–1): how well *mere coverage, unsealed* blocks this vector.
  (Renamed from `barrierFloor`: it is the value at `seal = 0`, *not* a minimum on total protection —
  naked is still 0.)

```
channelProtection(c, vector) = coverage_c × ( unsealedEff_v + (1 − unsealedEff_v) × seal_c )
protection_v                 = Σ_c  weight_{v,c} × channelProtection(c, vector)
factor                       = 1 − protection_v        # multiplied into the vector's target-side chance
```

> A sealed vacsuit's strength is its **seal (1.0)**, riding `(1 − unsealedEff) × seal`. The floor only
> governs *unsealed* clothing (plate, parkas). Floor and seal are different levers.

### 3.3 The sealed-system capstone (how full sets hit 100%)

A complete sealed loadout short-circuits to immunity on every clothing-protectable vector. **Two
triggers measure two different quantities — they are not the same number:**

- **Vacuum-sealed (Odyssey):** aggregate `VacuumResistance ≥ 0.95`. This is the in-game *vacuum* stat
  (summed across gear, can exceed 1): vac set 1.01, cataphract 0.98, recon 0.95. Odyssey-only.
- **Fully sealed (DLC-independent):** `airway` sealed **AND** `skinSeal ≥ 0.9` across ≥ 0.9 of body
  coverage (incl. assumed/real extremities). `skinSeal` is the per-channel *seal-quality* value (0–1,
  §3.1) — **not** `VacuumResistance`. This is what gives a Royalty-no-Odyssey player full-cataphract
  immunity. Catches cataphract (1.0), marine (0.95), recon (0.90); ordinary armor falls short.

Plus two single-item shortcuts: a head item with `immuneToToxGasExposure = true` → `airway` hermetic
(vac helmet); authored `providesSealedAtmosphere = true` → sealed by fiat (modded hazmat, any DLC).

> The two thresholds look alike but aren't comparable: **0.95 is on `VacuumResistance`** (vacuum stat,
> can exceed 1 when summed); **0.9 is on `skinSeal`** (a 0–1 seal-quality value). "Is it vacuum-rated?"
> vs "is it a fully sealed suit?". Seal integrity (§7) gates the capstone for *incidental* sealers under
> either trigger; dedicated PPE is exempt.

When sealed → all channels clamp to 1.0 → immune to airborne + all contact vectors. Ingestion and
`Vector_Lovin` are excluded by design.

### 3.4 Respiratory — two-sided, helmets sealed, masks filtering

```
airwayProtection(pawn, eff) = isAirwaySealed(pawn) ? 1.0 : filterSeal_airway(pawn) × eff
sideFactor                  = 1 − airwayProtection(pawn, eff)
maskFactor                  = sideFactor(source, sourceEff) × sideFactor(target, targetEff)
```

- **Sealed** airway (enclosed helmets; capstone): full both sides → immune. Detected by authored seal,
  `immuneToToxGasExposure`, or head `VacuumResistance ≥ 0.5` (flak/open helmets at VR 0 are excluded).
- **Filtering** airway (masks, lung implants): capped by per-vector `maskSourceEffectiveness` /
  `maskTargetEffectiveness`. Gas-mask numbers unchanged from today (filterSeal 0.8).

### 3.5 Resolution hierarchy + tech-level gate

Each item resolves per-channel `seal` by the first rule that applies:

1. **Item author's `ApparelContagionProtection` DefModExtension** — authoritative, DLC-/CE-independent.
2. **Contagion's bundled compat patch** for known third-party gear, guarded
   `not(modExtensions/li[@Class="Contagion.ApparelContagionProtection"])` so rule 1 always wins.
3. **Stat + tech-level fallback** for unknown items (below).
4. **Coverage fallback** — `bodyPartGroups`/`coverageAbs` gives the `unsealedEffectiveness` baseline.

**Tech-level gate** (the fallback's spine — `ThingDef.techLevel`). Coverage alone can't tell plate armor
from marine armor (identical `bodyPartGroups`, 290 vs 340 HP); only tech level can:

| `techLevel` | Sealing policy | Examples |
|---|---|---|
| **Spacer / Ultra / Archotech** | **Generous** — full-body suits assumed sealable; `VacuumResistance`/`ToxEnv`/`immuneToToxGas` taken at face value | recon/marine/cataphract/vacsuit (VR 0.30–0.32 suits, 0.65–0.69 helmets) |
| **Industrial** | **Tight** — seals *only* with an explicit signal (`ToxEnv`, `VacuumResistance`, `immuneToToxGas`) or authoring | gas mask (ToxEnv → filter) seals; flak vest (no signal) does **not** |
| **Medieval and below** | **Never seals** — hard cap `seal = 0`; coverage-only | plate armor → good fluid protection, never immune, no airway |

This makes most modded spacer armor "just work" as sealable, modded medieval gear correctly unsealable,
and the modder can always override.

---

## 4. Per-vector profiles (recommended)

Authored on each vector via a composed `ContactProtectionProfile` (XML). `weight` over the channels used;
`unsealedEff` is §3.2.

| Vector | Channels (weight) | `unsealedEff` | Sided | Rationale |
|---|---|---|---|---|
| Airborne / Social / Proximity | airway 1.0 | — | source×target | sealed helmet/set = immune |
| **CorpseFlea** | skin 0.85, hands 0.05, feet 0.05, airway 0.05 | **0.20** | target | clothing barely stops a flea; seal does the work |
| **CorpseFluid** | hands 0.45, skin 0.45, airway 0.10 | **0.40** | target | fabric catches a splash; bare hands a real route |
| **Fomite** | hands 0.60, airway 0.40 | **0.60** | target | a glove is a real touch barrier; a sealed helmet blocks touching your face |
| **Environmental** | feet 0.40, skin 0.45, airway 0.15 | **0.40** | target, humanlike-only | keep existing `humanExposureFactor` gate |
| **FecalOralLiving** | feet 0.50, skin 0.30, hands 0.20 | **0.60** | target | like fomite — boots + scrape-on-the-mat |
| **CookingExposure** (cook gets sick) | hands 0.55, airway 0.30, skin 0.15 | **0.60** | target (cook) | food-handling PPE protects the handler |
| **Cook→food** (Typhoid Mary, §5) | airway 0.50, hands 0.50 | **0.60** | source (cook) | infected cook's PPE cuts contamination baked into meals |
| `Foodborne` (eater) | — | — | — | **no** gear protection |
| `FecalOralEating` / `Lovin` | — | — | — | not clothing-protectable |

---

## 5. Cooking subsystem — three distinct things

PPE on the cook is one investment doing double duty:

1. **`Vector_CookingExposure` — the cook gets infected** handling contaminated ingredients. Target =
   cook. Protected by the cook's gloves/apron/mask (§4).
2. **`Vector_Foodborne` — the eater gets infected** from contaminated food. Target = eater. **No gear
   protection.**
3. **Cook→food (Typhoid Mary) — an infected cook contaminates the food they make.** Source = cook.
   **Core feature**; the upstream control on Foodborne risk.

**Typhoid Mary spec.** Contamination is baked into `Comp_ContaminatedFood` at production, scaled by
`CookingContaminationExtension.reductionFactor` (recipe) and the skill curve
(`ContagionRiskMath.CookingSurvivalFactor`). When the cook is contagious, multiply the **cook-sourced**
contamination potency by `1 − cookSourceProtection`:

```
cookSourceProtection = WeightedProtection( airway 0.5, hands 0.5 ; source-side ; unsealedEff 0.6 )
                       # hermetic bypass applies: a sealed-suit cook → ~1.0 → ~0 contamination
```

Bare infected cook poisons the larder; masked+gloved cook contaminates little; sealed-suit cook ~none.
Levers: quarantine sick cooks, PPE them, or lean on recipe/skill. The cook→food vector is currently
underspecified, so part of this is *defining* the cook-sourced potency, then applying the multiply.
Integration point: the production-time contamination path (a `JobDriver_DoBill`/`RecipeWorker` postfix
or `ContagionBillUtility`) — confirm the exact method against source at implementation.

---

## 6. Authored apparel table (recommended)

`seal` per channel; `coverage` automatic. **D** = dedicated PPE (seal valid to 0 HP, §7); **I** = incidental
sealer (seal degrades with HP). Combat helmets are sealed-class airway (decision 1).

| Apparel (defName) | DLC | techLevel | airway | skin (+ext) | D/I | sealed-system |
|---|---|---|---|---|---|---|
| Cloth mask `Apparel_ClothMask` | Core | Industrial | 0.50 (filter) | — | I | no |
| Gas mask `Apparel_GasMask` | Biotech | Industrial | 0.80 (filter) | — | I | no |
| Flak vest `Apparel_FlakVest` | Core | Industrial | — | torso/neck coverage only | — | no (no signal) |
| Plate armor `Apparel_PlateArmor` | Core | **Medieval** | — | coverage only (never seals) | — | no (tech gate) |
| Flak / simple helmet | Core | Industrial | — (open) | head ~0.3 | — | no |
| Recon helmet `Apparel_ArmorHelmetRecon` | Core | Spacer | **sealed** | head 0.80 | I | via set (VR 0.65) |
| Marine helmet `Apparel_PowerArmorHelmet` | Core | Spacer | **sealed** | head 0.85 | I | via set |
| Cataphract helmet `Apparel_ArmorHelmetCataphract` | Royalty | Spacer | **sealed** | head 0.92 | I | via set (VR 0.68) |
| Vacsuit helmet `Apparel_VacsuitHelmet` | Odyssey | Spacer | **sealed** | head 0.85 | **D** | `immuneToToxGas` (VR 0.69) |
| Recon armor `Apparel_ArmorRecon` | Core | Spacer | — | **0.90** | I | via set (VR 0.30) |
| Marine armor `Apparel_PowerArmor` | Core | Spacer | — | **0.95** | I | via set (VR 0.30) |
| Cataphract armor `Apparel_ArmorCataphract` | Royalty | Spacer | — | **1.00** | I | via set (VR 0.30) |
| Vacsuit `Apparel_Vacsuit` | Odyssey | Spacer | — | **1.00** | **D** | via set (VR 0.32) |
| Ordinary clothing | — | various | — | coverage only | — | no |

All four spacer suits land `skinSeal ≥ 0.9` → fully-sealed path. With Odyssey, all four sets also clear
`VacuumResistance ≥ 0.95`. Their disease behavior is therefore identical *when fresh* — the differences
emerge through **durability** (HP pool, §7) and their non-disease stats (armor, mobility, cost).

---

## 7. Durability & seal integrity

**The question:** what stops a player throwing everyone into full armor and forgetting disease exists?
Answer: worn gear degrades, and damage punches holes in incidental seals. No need to simulate taking
armor off to eat/sleep — durability does the work, and it ties to states the player already reads
(`ratty` < 50% HP, `tattered` < 20% HP).

**Recommendation — banded threshold at the vanilla states (a merge of the two options).** Contagion
computes a **seal integrity** multiplier from HP state and applies it to the resolved seal *before* the
capstone and channel math:

| HP state | integrity ×seal | effect |
|---|---|---|
| ≥ 50% (normal) | **1.0** | full seal — sealed suits immune (capstone) |
| 20–50% (**ratty**) | **0.85** | drops out of guaranteed immunity → strong partial; UI flips Full→Partial |
| < 20% (**tattered**) | **0.55** | seal mostly gone → moderate partial |

- **Why threshold over smooth, for the common player:** the property a casual player must never lose is
  the binary "sealed = immune." Smooth degradation turns immunity into a slope they have to *monitor* —
  they won't, and they'll eat an unexplained breakthrough (the exact feel-bad we're avoiding). Tying seal
  loss to `ratty`/`tattered` reuses a state they already learn: "keep sealed armor out of the ratty zone."
  The banding gives a soft landing (immune → ~85% → ~55%, not a cliff to 0), capturing the smooth method's
  gentleness while keeping a crisp, legible "now it's compromised" moment.
- **Emergence is preserved for free:** bands are % of MaxHP, so cataphract (400 HP) absorbs ~200 absolute
  damage before going ratty vs recon (~140). "Cataphract outlasts recon" falls out of the HP pool — no
  degradation curve needed.
- **Capstone gating is DLC-independent:** integrity gates the capstone for *incidental* sealers under
  **both** triggers (VR-sum and skinSeal), so a ratty cataphract drops out of immunity whether or not
  Odyssey is installed. (We compute integrity ourselves; we do not rely on vanilla VR being HP-scaled,
  which it isn't.)
- **Dedicated PPE is exempt:** `durableSeal = true` (vacsuit, vac helmet, modded hazmat) → integrity is
  always 1.0; the seal is binary-valid to 0 HP. Matches vanilla (`VacuumResistance` doesn't HP-scale) and
  the "don't make me juggle hazmat durability" constraint.
- **Partial (non-sealed) gear** degrades the same way (a half-destroyed mask filters less) — no binary to
  break, so the gentle erosion is fine.

**Strategic payoff:** want hassle-free disease immunity → wear purpose-built PPE (vacsuit), which never
degrades. Want combat armor doing double duty → it works, but you maintain/rotate it. That *is* the
answer to set-and-forget.

> The `ratty`/`tattered` cutoffs (50% / 20%) and the integrity values (0.85 / 0.55) are tunable; confirm
> the exact cutoffs against the vanilla `Apparel` HP-state constants at implementation. If maximum
> emergence is wanted later, linear interpolation *within* the same bands is a drop-in with no redesign.

---

## 8. Legibility & UX

Three tiers, escalating only on demand.

**Tier 1 — Intuitive by construction (no reading).** The channel model maps to body-sense: masks →
inhaled, suits → skin, gloves → touch, boots → ground. A design constraint, not a hope: category names/
icons must reinforce it, and behavior must never contradict common sense.

**Tier 2 — At-a-glance (pawn Gear tab).** Beside the Sharp/Blunt/Heat armor summary, a **Disease
protection** block of three player-facing categories:

| Category | Folds in | Reads as |
|---|---|---|
| **Airborne** | airborne / social / proximity | Full · Partial · None |
| **Contact** | corpse flea / fluid / fomite (+ cooking) | Full · Partial · None |
| **Surface** | environmental / fecal-oral-living | Full · Partial · None |

State bucketed by the category's representative protection (Full ≥ ~0.9, Partial > 0, None = 0). Three
lines, not twelve.

**Tier 3 — On hover + on the item.** Gear-tab category hover → tooltip with real per-vector percentages
and the dominant contributor ("Contact 84% — vacsuit"). Apparel **info card** (`SpecialDisplayStats`):
rows like "Airborne: sealed", "Contact: partial" — so gear self-documents and a modder's item explains
itself. A ratty sealed suit shows "Contact: partial (damaged)" — the durability state surfaced here too.

**Tier 4 — Dev mode.** The full `ContagionSpreadBreakdown` hover stays for tuning.

Implementation (vanilla-first): Tier-3 uses `StatDrawEntry`/`SpecialDisplayStats`. Tier-2 targets the
Gear-tab armor area (`ITab_Pawn_Gear`, around `TryDrawOverallArmor`) — confirm the draw hook vs source
(`curY` plumbing makes a naive postfix fragile). One `ProtectionSummary(pawn)` helper feeds all tiers.

---

## 9. The hands/feet coverage problem

Vanilla apparel covers no hands/feet; the CE Armors submod doesn't add gloves/boots; CE proper and small
mods *do* extend suits to cover them. A genuine quirk that distorts hands-weighted vectors.

Stance: **detect real coverage when present; assume it when absent — never changing combat coverage.**

- **CE / coverage mod active** → `coverageAbs` already includes hands/feet → use the real values.
- **Otherwise** → for suits that *should* cover extremities (sealed full-body suits), credit `hands`/
  `feet` at the suit's `skin` seal, for disease calc only. `ArmorRating`/combat coverage untouched.
- **Per-suit scope:** sealed/enclosing suits (vacsuit, power armor, hazmat) yes; a duster no. Drive it
  from the same authored/heuristic data as `skin` seal (high-seal full-body shell ⇒ assume extremities).

This is what lets a vacsuit protect fluid/fomite as a player expects (gauntlets), and gives a
non-Odyssey full cataphract the contact immunity the VR-capstone otherwise grants.

---

## 10. Implementation plan

Pre-release (`CLAUDE.md`): save-breaking changes are fine; no migration shims. Changes are additive (a new
DefModExtension + optional vector/profile fields + UI).

**New**
- `Source/Core/ApparelContagionProtection.cs` — `DefModExtension`: per-channel `seal` floats, `bool
  airwaySealed`, `bool coversExtremities`, `bool durableSeal`, `bool providesSealedAtmosphere`.

**Extend**
- `Source/Core/ContagionApparelProtectionUtility.cs` - channel resolver (per-`ThingDef` cache: coverage from
  `bodyPartGroups`/`coverageAbs`; seal via §3.5 incl. the **tech-level gate**; null-safe `VacuumResistance`;
  assumed extremity coverage per §9). Add `GetContactProtectionFactor`, `GetCookSourceProtectionFactor`,
  `IsSealedAtmosphere`, and a **seal-integrity** step reading the worn `Apparel.HitPoints`/`MaxHitPoints`
  (exempt `durableSeal`). Respiratory protection is sealed-vs-filtering.
- `Source/Core/ContagionRiskMath.cs` — pure, audit-callable: `ChannelProtection`, `WeightedProtection`,
  `AirwaySideFactor`, `SealIntegrity(hpFraction)`.
- `Source/Core/TransmissionProfile.cs` — composed `ContactProtectionProfile apparelProtection;` on the
  protected vectors + a cook-source profile. **Composition, not reparenting.** Rename
  `barrierFloor`→`unsealedEffectiveness`; clarify the mask-effectiveness comments.

**Apply (target-side multiply) at the existing sites**
- Fomite — `ContagionVomitFomiteTracker.RunFomiteExposurePass` (the `pawn` is target).
- Flea/fluid — `ContagionCorpseExposureUtility.TryApplyFleaExposure`/`TryApplyFluidExposure`: fold into the
  existing **`contextFactor`** parameter.
- Environmental + FecalOralLiving — their processors, beside the humanlike gate.
- CookingExposure — where `GetCookingExposureFactor` is consumed.
- **Cook→food** — source-side multiply at the production-time contamination path (§5).

Respiratory already routes through `GetRespiratoryMaskFactor` into the three pawn-to-pawn breakdowns.

**XML** (`1.6/Patches/`)
- `Contagion_ApparelProtection.xml` — flat two-step `PatchOperationAdd` of §6 modExtensions; DLC rows via
  `MayRequire` package-id guards. (Most spacer armor is also caught by the tech-level
  fallback, so authoring is a refinement, not a requirement.)
- `ModPatches/Contagion_ApparelProtection_*.xml` — known third-party gear, one guarded file per source mod.
- Profile XML — §4 `ContactProtectionProfile` blocks on the relevant `<li Class="Contagion.Vector_*">`.

**UX** (§8) — `ProtectionSummary(pawn)` aggregator; Gear-tab block (confirm hook vs `ITab_Pawn_Gear`);
`SpecialDisplayStats` rows; surface the ratty/tattered seal state.

**Audit & docs** (`CLAUDE.md`)
- `python tools\audit_infection_risk.py` — add clothed-target cases (naked / clothed / gas-mask / vacsuit-
  only / vac set / recon / cataphract), a **plate-armor** case (coverage protection, no seal), a **ratty-
  cataphract** case (dropped out of immunity), and a **cook→food** contamination case; update expected
  values & tolerances deliberately.
- `DESIGN.md` — replace the respiratory-only section with the unified Apparel Protection section; add
  `contactProtectionFactor(target)`, the cook-source factor, and seal integrity to the equations; fix
  stale mask-toggle lines.
- `docs/diseases/Flu.md` (airborne + fomite), `docs/diseases/Plague.md` (flea/fluid), foodborne disease doc
  (Typhoid Mary) — document barriers, floors, and the durability interaction.

---

## 11. Remaining confirmations (small) (with user input following)

- **Parameter name** — `unsealedEffectiveness` (vs `permeableBarrierFactor`)? ## user: use unsealedEffectiveness.
- **Durability:** banded threshold recommended; integrity values 0.85 (ratty) / 0.55 (tattered) — good, or
  tune? (Smooth interpolation within the same bands remains a drop-in if you want more emergence.) ## user: banded threshold is good.
- **Dedicated-vs-incidental line:** vacsuit/vac helmet/hazmat = dedicated (seal to 0 HP); combat armor =
  incidental (degrades). Comfortable, or should cataphract also be dedicated? ## user: incidental.
- **At-a-glance category labels** — "Airborne / Contact / Surface"? ## user: look good.
- **Cook→food weighting** — airway/hands 50/50, or weight hands higher? ## user: 50/50 is fine.
