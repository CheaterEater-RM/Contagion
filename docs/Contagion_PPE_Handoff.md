# Contagion — PPE Apparel Handoff (for Claude Code)

Purpose: a pointer map of which **reference mods** contain PPE-equivalent apparel that should reasonably reduce disease transmission, and exactly where to find each. These are soft-dependency **patch targets** for Contagion (patch `<li MayRequire="packageId">` so they only apply when the mod is present).

Reference root: `C:\Users\AMM\Documents\Github\Rimworld Mods\Rimworld_Mod_References\`
All paths below are relative to that root. RimWorld **1.6** only — ignore pre-1.6 version folders.

---

## How to think about "blocks disease" (coverage tiers)

Contagion should weight protection by **what the apparel covers**, read from each ThingDef's `<apparel><bodyPartGroups>` + `<layers>`:

- **Airway/face** (`Mouth`/`Teeth`/`Eyes`, overhead/eye-cover layers) → blocks airborne/droplet. Masks, respirators, surgical masks.
- **Full seal** (covers torso+head, often `Middle`/`Shell`+helmet) → blocks airborne + contact. Hazmat-equivalent; here that's the warcasket.
- **Hands/contact** → vanilla has **no glove slot**; none of these mods add true gloves. Don't hunt for them.
- **Medical PPE** (scrubs/surgical) → reduce transmission *during tending* specifically; consider tying to the doctor/tend path, not ambient.

There is **no dedicated "hazmat suit"** in the read mods. Closest equivalents: gas masks (airway) and the VFE Pirates warcasket (full seal).

---

## Recommended implementation pattern (from VFE Medieval 2's plague mask)

The single most useful mechanism in this set, and the one Contagion should mirror:

**apparel → worn hediff (via VEF `CompProperties_ApparelHediffs`) → hediff with `<makeImmuneTo>` / severity effects.**

- For **hard immunity** to a specific disease: a hediff stage with `<makeImmuneTo><li>YourDiseaseHediffDef</li></makeImmuneTo>` — exactly how the plague mask blocks `Plague`. Vanilla `makeImmuneTo` is a clean, code-free way to make a piece of PPE fully prevent a named disease HediffDef.
- For **partial protection** (more realistic for most PPE): make Contagion's infection logic read worn apparel and reduce infection chance / severity gain by coverage, instead of granting blanket immunity. Reserve `makeImmuneTo` for top-tier sealed gear (warcasket) or the dedicated plague mask.
- The VEF comp `VEF.Apparels.CompProperties_ApparelHediffs` (`<hediffDefnames>` list) is the standard "apply this hediff while worn" bridge — usable by Contagion directly (VEF is already a common dependency across this collection) or replicable with a small custom comp if you don't want the VEF dependency.

Decide per item whether immunity is full (plague mask, full warcasket) or graded (cloth/surgical masks, gas masks).

---

## Confirmed targets (defNames verified from the def XML)

### [KK] Gasmask — `Mlie.KKGasmask`  ✅ primary mask pack
File: `KKGasmask-main/1.6/Defs/ThingDefs_Misc/Apparel_Masks.xml`

| defName | covers | toxic stat | notes |
|---|---|---|---|
| `Apparel_GasMask` | Eyes, Teeth (Overhead) | `ToxicResistance 0.5` | strongest face/eye cover here |
| `Apparel_FilterCloth` | Teeth (Overhead) | `ToxicResistance 0.35` | tribal cloth mask |
| `Apparel_GlitterFilter` | Teeth (Overhead) | `ToxicResistance 0.75` | advanced mouth filter/implant |

### Equip Gas Masks — `syila.eqgasmask`
File: `Equip Gas Masks/v1.6/Defs/ThingDefs_Misc/Apparel_Gasmask.xml`

| defName | covers | notes |
|---|---|---|
| `Apparel_GasMask` | Eyes, Mouth, Teeth (EyeCover) | best airway coverage (mouth+eyes); has a custom equip comp |

> ⚠️ **defName collision:** KKGasmask **and** Equip Gas Masks both define `Apparel_GasMask`. If a user runs both, load order decides which wins. Contagion's patch on `Apparel_GasMask` will hit whichever loaded — fine, but don't assume the field set; both versions carry toxic protection and airway coverage, so either is a valid protection target. Patch defensively (check the node exists).

### VFE Medieval 2 — `OskarPotocki.VFE.Medieval2`  ✅ the cleanest disease-block mechanism in the whole set
The **plague mask** grants the wearer **full immunity to vanilla `Plague`** — and shows exactly the pattern Contagion should use (see "Recommended implementation pattern" below).
- Apparel ThingDef: `VFEM2_Apparel_PlagueMask` — file `Vanilla Factions Expanded - Medieval 2/1.6/Defs/ThingDefs_Misc/Apparel_Headgear.xml`. Covers `FullHead` (Overhead layer), `ToxicResistance 0.2`.
- It applies a worn hediff via VEF: `<comps><li Class="VEF.Apparels.CompProperties_ApparelHediffs"><hediffDefnames><li>VFEM2_PlagueMask</li></hediffDefnames></li></comps>`.
- Hediff: `VFEM2_PlagueMask` — file `.../1.6/Defs/HediffDefs/Hediffs_Global_PlagueMask.xml` — a single stage with `<makeImmuneTo><li>Plague</li></makeImmuneTo>`. (Flavor text says "keeps infected pawns at a distance," but the actual mechanic is hard immunity to the `Plague` HediffDef.)

### VFE Pirates — `OskarPotocki.VFE.Pirates`  ✅ full-seal (the "warcasket" the user wants)
Apparel class `Apparel_Warcasket` (sealed industrial powered-armour, body + head + shoulders).
- Decompiled class: `Vanilla Factions Expanded - Pirates/1.6/Assemblies/Decompiled Source/VFEPirates/Apparel_Warcasket.cs` (+ `WarcasketProject.cs`, `Building_WarcasketFoundry.cs`).
- defNames seen in `VFEP_DefOf`: `VFEP_Warcasket_Warcasket` (body), `VFEP_WarcasketHelmet_Warcasket` (helmet), `VFEP_WarcasketShoulders_Warcasket` (shoulders), `VFEP_Warcasket_Bodysuit`.
- **Find the actual ThingDefs** under `Vanilla Factions Expanded - Pirates/1.6/Defs/` (search the Defs folder for `Warcasket`); there are multiple warcasket variants/factions, so enumerate rather than hardcoding one.
- Treat the helmet+body+shoulders set worn together as **full seal** = top-tier disease block. `WarcasketUtility.IsWearingWarcasket(pawn)` (in the decompiled source) is a ready-made "is this pawn sealed?" check Contagion can mirror.

---

## To verify (mods that have PPE — confirm exact defNames before patching)

These definitely contain relevant items (seen in About/descriptions) but I did not open their def XML; Claude Code should grep the Defs folder and pull exact defNames.

### FashionRIMsta (Continued) — `Mlie.FashionRIMsta`
Look in: `FashionRIMsta (Continued)/1.6/Defs/`
Relevant items: **Surgical Mask** (medical airway PPE — strongest fit for tending), **Gasmask** (airway), **Scrubs** (medical PPE), possibly **Desert Head Wrap** (face cover). Grep the Defs for `Mask`, `Scrub`, `Gas`, `Wrap`.

### Vanilla Apparel Expanded — `VanillaExpanded.VAPPE`
Look in: `Vanilla Apparel Expanded/1.6/Defs/`
Relevant items: **gas masks** (airway), **doctor scrubs** (medical PPE). Grep for `Mask`, `Scrub`, `Doctor`. Note: VAPPE is VEF-backed; behaviour fields may be VEF mod-extensions, but the apparel are plain ThingDefs you can patch by defName.

### Vanilla Armour Expanded — `VanillaExpanded.VARME` (optional)
Look in: `Vanilla Armour Expanded/1.6/Defs/`
No dedicated PPE, but **sealed marine-armour variants** (and ghillie suits) could count as partial/full seal if Contagion rewards full-body coverage. Optional, lower priority — decide based on whether Contagion treats sealed power armour as protective.

---

## Not relevant (checked, no PPE)
- **VAE — Accessories** (`VanillaExpanded.VAEAccessories`) — belts/quivers/resurrector/explode belts. No respiratory/seal coverage.
- (VFE Medieval 2 *does* have PPE — the plague mask — now listed under confirmed targets.)

---

## Suggested patch approach for Claude Code

1. **Per-mod XML patches**, each guarded by `MayRequire="<packageId>"`, adding Contagion's protection comp/modExtension (or stat) to the confirmed defNames above. Keep them in separate patch files per source mod for clean optional-dependency behaviour.
2. **Prefer coverage-based logic over a hardcoded defName list** where possible: if Contagion computes protection from `bodyPartGroups`/`layers` at runtime, these items get covered automatically and the XML patches become a fallback / fine-tuning layer. The defName list above is the explicit-target set for items that need an exact tuning value.
3. **Warcasket**: gate the top "sealed" tier on the full set (or reuse the `IsWearingWarcasket` coverage check). Enumerate all `Warcasket*` ThingDefs from the Pirates Defs folder, don't hardcode one.
4. **defName collision** (`Apparel_GasMask` in two mods): patch the defName once; it resolves to whichever mod is loaded. Make the patch tolerant of either field set.

> All source mods here are **reference only** — read for defNames/coverage, do not copy defs or art into Contagion.
