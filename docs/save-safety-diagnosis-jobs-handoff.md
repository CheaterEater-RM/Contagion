# Contagion Save-Safety Handoff: In-Flight Diagnosis Jobs

## Resolution (implemented)

This was resolved by **Option B (vanilla-driver piggyback)**, not the save-time scrub (Option A) drafted below. The fragile custom jobs were removed entirely so an in-flight save only ever holds vanilla `JobDef`/`JobDriver` references:

- **Corpse inspection** now runs on the vanilla `JobDefOf.InteractThing` / `JobDriver_InteractThing` against a `CompInteractable` subclass (`Comp_CorpseInspectable`) injected onto corpse defs; `ContagionCorpseUtility.TryInspectCorpse` runs in `OnInteracted`. The butcher-table requirement was dropped — inspection happens in place on any reachable flesh corpse.
- **Proactive animal screening** now flags the animal with a visible, self-clearing tendable hediff (`Contagion_PendingExam`) and resolves through the vanilla tend path (`JobDriver_TendPatient` + `HediffComp_PendingExamDiagnosis.CompTended`), mirroring the visible-sick path that already survives removal. The marker is guaranteed to clear (tended, 1-day `HediffComp_Disappears`, or yields to `Contagion_AnimalSick`).

`Contagion_InspectCorpse` and `Contagion_DiagnoseAnimal` (JobDefs + drivers) were retired. The candidate plan below is kept for historical context.

## Purpose

This document summarizes the current Contagion save-removal problem around custom diagnosis jobs, the relevant test logs, and a candidate plan for making those jobs save-safe before release.

The immediate goal is not to build a full uninstall framework. This is a forward-looking pre-release cleanup pass: saves made with the fixed mod should not contain fragile Contagion job/filter state that breaks when the mod is later removed.

## Current Save-Removal State

The corpse special-filter issue appears substantially improved after the save shim for `ThingFilter.ExposeData`.

Before that fix, removing Contagion from a save produced missing special filter references:

- `Could not load reference to Verse.SpecialThingFilterDef named AllowInfectedCorpses`
- `Could not load reference to Verse.SpecialThingFilterDef named AllowUninfectedCorpses`
- repeated `Verse.ThingFilter.Allows(Thing t)` failures during load/finalization

Those errors came from vanilla `ThingFilter.disallowedSpecialFilters` containing Contagion `SpecialThingFilterDef` references. Since vanilla `ThingFilter.Allows(Thing)` does not null-check missing special filters before calling `filter.Worker.Matches(t)`, a removed mod could poison stockpiles, bill filters, and haulability checks.

After the filter shim, the user's next removal test produced no flashing colors and the save appeared normal. The relevant post-fix log showed only the expected map-component missing-class warning/error:

- `Could not find class Contagion.Contagion_MapTransmissionComponent while resolving node li. Trying to use Verse.MapComponent instead`
- `SaveableFromNode exception: System.ArgumentException: Can't load abstract class Verse.MapComponent`

That map-component issue is still noisy, but it did not produce the severe load corruption seen from the corpse filter problem. It is tracked separately from the job issue.

## New Problem: In-Flight Custom Corpse Inspection Job

The user then tested removing Contagion while several jobs/actions were in flight:

- A sick husky was being tended through vanilla-style `Tend Animal`.
- An infected ibex deer corpse was being butchered through a vanilla butcher bill.
- A clean pig carcass was being inspected through Contagion's custom corpse inspection job.

Observed result:

- The husky tend path survived.
- The infected corpse butcher path survived.
- The pig corpse inspection failed badly. The pawn Jeffrey became locked/broken and repeatedly spawned errors.

Relevant log evidence from `C:\Users\AMM\.codex\attachments\63d7ca9d-6a6c-498c-8772-f0dd84b638f1\pasted-text.txt`:

```text
Could not load reference to Verse.JobDef named Contagion_InspectCorpse
```

```text
Could not find class Contagion.JobDriver_InspectCorpse while resolving node curDriver.
Trying to use Verse.AI.JobDriver instead.
Full node: <curDriver Class="Contagion.JobDriver_InspectCorpse">
  <curToilIndex>3</curToilIndex>
  <ticksLeftThisToil>176</ticksLeftThisToil>
  <startTick>23360</startTick>
  <locomotionUrgencySameAs>null</locomotionUrgencySameAs>
</curDriver>
```

```text
SaveableFromNode exception: System.ArgumentException: Can't load abstract class Verse.AI.JobDriver
```

```text
Cleaning up invalid job state on Jeffrey
Could not do PostLoadInit on Verse.AI.Pawn_JobTracker: System.NullReferenceException
  at Verse.AI.Pawn_JobTracker.EndCurrentJob(...)
```

After that, the pawn remained in a broken state with repeated draw/tick/thought errors:

```text
Exception spawning loaded thing Jeffrey: System.NullReferenceException
Exception drawing Jeffrey: System.NullReferenceException
Exception ticking Jeffrey (at (161, 0, 127)): System.NullReferenceException
Exception while recalculating ListeningToHarp thought state for pawn Jeffrey: System.NullReferenceException
```

The draw error included:

```text
Verse.PawnRenderUtility.CalculateCarriedDrawPos(...)
Verse.PawnRenderUtility.DrawCarriedThing(...)
Verse.PawnRenderNodeWorker_Carried.PostDraw(...)
```

Interpretation: the saved pawn had a missing Contagion current job and a missing Contagion job driver. Vanilla attempted to recover in `Pawn_JobTracker.ExposeData` by ending the invalid current job, but the job/driver state was already malformed enough that `EndCurrentJob` itself threw. Jeffrey then remained loaded in a bad current-job/carry/render state.

## Relevant RimWorld Save Mechanics

Checked against decompiled RimWorld 1.6 source:

- `Verse.AI.Job.ExposeData()` saves `Job.def` via `Scribe_Defs.Look(ref def, "def")`.
- If the mod is removed, `Contagion_InspectCorpse` or `Contagion_DiagnoseAnimal` becomes a missing `JobDef` reference.
- `Verse.AI.Pawn_JobTracker.ExposeData()` deep-saves:
  - `curJob`
  - `curDriver`
  - `jobQueue`
  - `posture`
- If `curDriver == null && curJob != null` during `PostLoadInit`, vanilla logs `Cleaning up invalid job state on {pawn}` and calls `EndCurrentJob(JobCondition.Errored)`.
- `Verse.AI.JobDriver` is abstract. When a removed custom driver class is loaded, fallback to `Verse.AI.JobDriver` fails with `Can't load abstract class Verse.AI.JobDriver`.
- `Verse.AI.JobQueue.ExposeData()` already removes queued jobs whose `job?.def == null` during `PostLoadInit`, so queued jobs are less dangerous than active `curJob`/`curDriver`.

Practical conclusion: current jobs and current drivers are the primary save-breaking surface. Queued jobs are safer but should still be scrubbed from future saves to keep removed-mod logs quiet and clean.

## Current Contagion Job Surfaces

Contagion currently defines two custom `JobDef`s in `1.6/Defs/Contagion_JobDefs.xml`:

```xml
<defName>Contagion_InspectCorpse</defName>
<driverClass>Contagion.JobDriver_InspectCorpse</driverClass>
```

```xml
<defName>Contagion_DiagnoseAnimal</defName>
<driverClass>Contagion.JobDriver_DiagnoseAnimal</driverClass>
```

### `Contagion_InspectCorpse`

Implemented by `Source/Jobs/JobDriver_InspectCorpse.cs`.

Current behavior:

- Target A: butchery table.
- Target B: corpse.
- Pawn goes to the corpse.
- Pawn carries the corpse to the butchery table.
- Pawn waits/inspects for 400 ticks.
- Driver calls `ContagionCorpseUtility.TryInspectCorpse(Corpse, pawn)`.
- Driver drops the corpse at the table.

This is the path that failed in the Jeffrey test. The failure occurred during toil index 3 with `ticksLeftThisToil` still present, so the save caught the job mid-driver.

### `Contagion_DiagnoseAnimal`

Implemented by `Source/Jobs/JobDriver_DiagnoseAnimal.cs`.

Current behavior:

- Target A: animal.
- Pawn reserves the animal.
- Pawn walks to/touches the animal.
- Pawn waits/examines for 100 ticks.
- Driver calls `ContagionAnimalDiagnosisUtility.ResolveDiagnosisAttempt(animal, pawn)`.
- Adds Medicine skill XP.

This job was not the failure observed in the current log, but it has the same save-removal shape: a custom `JobDef` and custom `JobDriver` can be present in `curJob`/`curDriver`.

## Animal Diagnosis Design Clarification

The user clarified that the sick husky test used vanilla-style `Tend Animal`, not the custom proactive "check/examine for illness" job.

This distinction matters:

- The vanilla tend path uses `JobDriver_TendPatient`, not a Contagion custom job driver.
- Contagion piggybacks on vanilla tending through `TendUtility.DoTend` patches and `HediffComp_AnimalSickDiagnosis`.
- That path survived removal in testing.

The proactive animal screening job is intentionally different and should remain:

- It lets the player screen a domestic animal before the visible `Contagion_AnimalSick` signal exists.
- `ContagionAnimalDiagnosisUtility.GetSickSignalProfile(animal)` can detect:
  - `Hediff_ContagionIncubation` when its disease profile has `showsSickSignal`.
  - undiagnosed `Hediff_ContagionAnimalHiddenDisease` when its disease profile has `showsSickSignal`.
- A successful roll reveals the disease.
- A failed roll or clean animal sends the "nothing found" message.
- Both success and failure apply the one-week diagnosis cooldown.

Requested product rule:

> Proactive screening is helpful, let's keep it. But if an animal is visibly sick, then we should limit it to "tend" tied to that sick hediff so we don't duplicate work.

So the desired behavior is:

- Healthy-looking/suspected animal: proactive screening option may appear.
- Animal with visible `Contagion_AnimalSick`: no proactive screening option; use vanilla `Tend Animal`.
- If a proactive screening job is already underway and the animal gains `Contagion_AnimalSick`, the custom job should fail and allow the vanilla tend workflow to take over.

## Important Constraint: Autosaves Must Not Interrupt Jobs

One tempting fix is to physically drop carried corpses during save if a pawn is in `Contagion_InspectCorpse`. That should be avoided.

Autosaves happen during normal gameplay. A save-time fix must not:

- call `TryDropCarriedThing` as part of ordinary saving,
- mutate live carry state,
- interrupt current gameplay,
- alter reservations or job queues permanently,
- spawn/despawn corpses as a side effect of saving.

The save shim should change only the serialized representation, then immediately restore the live in-memory state.

## Candidate Solution

Use the same general pattern as the corpse filter save shim: temporary save-time substitution, immediate in-memory restoration.

### 1. Add a Contagion job save helper

Add a small internal helper that can identify Contagion's fragile custom jobs:

- `Contagion_InspectCorpse`
- `Contagion_DiagnoseAnimal`

It should provide:

- `IsContagionDiagnosisJob(Job job)`
- `IsContagionDiagnosisJobDef(JobDef def)`
- `IsContagionDiagnosisDriver(JobDriver driver)`
- `IsContagionDiagnosisQueuedJob(QueuedJob queuedJob)`

Detection should be explicit by `defName` and/or exact driver type. It should not treat vanilla `JobDriver_TendPatient` as a Contagion job.

### 2. Patch `Pawn_JobTracker.ExposeData`

Add a Harmony patch around `Verse.AI.Pawn_JobTracker.ExposeData`.

On `Scribe.mode == LoadSaveMode.Saving`:

- If `curJob` or `curDriver` is a Contagion custom diagnosis/inspection job:
  - save the original `curJob`,
  - save the original `curDriver`,
  - save the original `posture`,
  - temporarily set `curJob = null`,
  - temporarily set `curDriver = null`,
  - temporarily set `posture = PawnPosture.Standing`.

After vanilla `ExposeData` returns:

- restore the original `curJob`,
- restore the original `curDriver`,
- restore the original posture.

This makes the saved file represent the pawn as idle/standing rather than mid-Contagion custom job, while the live game continues as before after the save completes.

Expected removal behavior:

- No saved `Contagion_InspectCorpse` current job.
- No saved `Contagion.JobDriver_InspectCorpse` current driver.
- Vanilla no longer tries to clean an invalid `curJob`/`curDriver` pair on load.
- Jeffrey-style `Cleaning up invalid job state` and subsequent draw/tick spam should disappear.

### 3. Patch `JobQueue.ExposeData`

Add a Harmony patch around `Verse.AI.JobQueue.ExposeData`.

On `Scribe.mode == LoadSaveMode.Saving`:

- access the private `jobs` list with `AccessTools.FieldRef`,
- temporarily filter out queued Contagion diagnosis jobs,
- let vanilla serialize the filtered queue,
- restore the original queue afterward.

This is less critical than `curJob`/`curDriver`, because vanilla already removes queued jobs whose def loads null. It still keeps future saves cleaner and prevents missing `JobDef` noise for queued proactive screenings or corpse inspections.

### 4. Do not alter `Pawn_CarryTracker` during save

Leave carried things alone during autosaves.

Rationale:

- The bad load path was the missing custom current job/driver, not merely "pawn is carrying a corpse."
- Vanilla can generally draw carried things without a current custom job.
- `PawnRenderUtility.CalculateCarriedDrawPos` only calls `pawn.jobs.curDriver.ModifyCarriedThingDrawPos(...)` when `pawn.CurJob != null`.
- If the saved `curJob` is null, that custom-driver draw path should not be reached.
- Most new vanilla jobs drop carried things on start if the new job does not allow keeping the carried thing.

This should avoid autosaves causing visible corpse drops or job interruptions.

### 5. Narrow proactive animal screening UI

Update `FloatMenuOptionProvider_DiagnoseAnimal`:

- If the animal has `Contagion_AnimalSick`, do not show the proactive option.
- Let the player use vanilla `Tend Animal` instead.
- Keep the cooldown-disabled option for animals without `Contagion_AnimalSick` but with `Contagion_AnimalDiagnosisCooldown`.

Possible helper:

- `ContagionAnimalDiagnosisUtility.HasVisibleSickSignal(Pawn animal)`

This should check for `ContagionDefOf.Contagion_AnimalSick` in the animal's hediff set.

### 6. Add proactive job fail condition

Update `JobDriver_DiagnoseAnimal.MakeNewToils()`:

- Add a `FailOn` condition if the animal gains `Contagion_AnimalSick`.

This prevents a proactive exam from continuing after the visible sick signal appears and reinforces the product rule that visible sickness belongs to vanilla tending.

## Candidate Test Plan

### Build

Run:

```text
dotnet build Source\Contagion.csproj
```

### Corpse inspection tests

With Contagion enabled:

1. Start `Inspect for disease` on a clean corpse.
2. Save during each major phase:
   - before pawn reaches corpse,
   - after pawn starts carrying corpse,
   - during inspection wait at table,
   - after diagnosis before drop if reproducible.
3. Inspect the save file:
   - no `Contagion_InspectCorpse` in saved `curJob`,
   - no `Contagion.JobDriver_InspectCorpse` in saved `curDriver`.
4. Remove Contagion and load the save.
5. Confirm:
   - no missing `Contagion_InspectCorpse` job-def error,
   - no missing `JobDriver_InspectCorpse` class error,
   - no `Cleaning up invalid job state on Jeffrey`-style crash,
   - no repeated pawn draw/tick errors,
   - pawn is controllable and eventually drops or otherwise resolves any carried corpse through vanilla behavior.

### Animal proactive screening tests

With Contagion enabled:

1. Animal has no visible `Contagion_AnimalSick`:
   - right-click option appears,
   - proactive screening can be ordered,
   - save during screening,
   - save contains no `Contagion_DiagnoseAnimal` current job/driver.
2. Remove Contagion and load:
   - no missing `Contagion_DiagnoseAnimal` job-def error,
   - no missing `JobDriver_DiagnoseAnimal` class error.
3. Animal has visible `Contagion_AnimalSick`:
   - proactive screening option does not appear,
   - vanilla `Tend Animal` remains available,
   - diagnosis resolves through the existing tend path.
4. If animal gains `Contagion_AnimalSick` during proactive screening:
   - custom job fails cleanly,
   - vanilla tend workflow can take over.

### Queued job tests

1. Queue `Contagion_InspectCorpse` behind a vanilla job.
2. Queue `Contagion_DiagnoseAnimal` behind a vanilla job.
3. Save.
4. Inspect save:
   - queued Contagion jobs are absent,
   - queued vanilla jobs remain.
5. Continue playing without removing Contagion:
   - autosave should not interrupt the live queue after save restoration.

### Regression tests

1. Verify infected-corpse butchery still works while Contagion is enabled.
2. Verify sick animal vanilla tending still works while Contagion is enabled.
3. Verify the corpse special filter save shim still removes:
   - `AllowInfectedCorpses`
   - `AllowUninfectedCorpses`
   from vanilla `ThingFilter.disallowedSpecialFilters`.
4. Remove Contagion after a post-fix save:
   - expected tolerated warnings may still include `Contagion_MapTransmissionComponent`,
   - no known save-breaking errors should include special filters or custom job drivers.

## Open Questions / Risks

### 1. Carried corpse after removal

The candidate plan intentionally does not mutate `Pawn_CarryTracker` during save. This avoids autosave side effects, but should be tested carefully.

Expected behavior is that a pawn loading with a carried corpse but no current job is recoverable. If testing disproves that, a more sophisticated non-mutating serialization shim may be needed for carry state. That would require careful work because `Pawn_CarryTracker` deep-saves an inner container, and manipulating it during save may affect live gameplay if done incorrectly.

### 2. Visible sick hediff removal

The in-flight job log also showed missing hediff errors:

```text
Could not load reference to Verse.HediffDef named Contagion_AnimalSick
SaveableFromNode exception: System.NullReferenceException
<li Class="HediffWithComps">...<def>Contagion_AnimalSick</def>...</li>
```

This did not appear to lock Jeffrey; it is a separate save-safety surface. It may still deserve a future pass if the goal is quiet removal logs rather than merely non-breaking removal.

Potential future approach: save-time stripping or conversion of short-lived Contagion marker hediffs, similar in spirit to the filter/job shims. That should be evaluated separately because disease state has gameplay implications.

### 3. Map component removal warning

The post-filter-fix removal log still contains:

```text
Could not find class Contagion.Contagion_MapTransmissionComponent
SaveableFromNode exception: Can't load abstract class Verse.MapComponent
```

This is probably safe-noisy based on current tests, but it is not clean. It may deserve a later save-shape pass if Contagion wants minimal warnings after removal.

### 4. Save-time shims and exception safety

Any patch that temporarily swaps live fields during save must restore them even if vanilla serialization throws. The implementation should use Harmony finalizers or robust postfix/finalizer state restoration so a failed save cannot leave the live game with `curJob` or `curDriver` nulled.

This is especially important for autosaves.

## Recommended Next Step

Implement the save-time job scrubber first, focused on `Pawn_JobTracker.ExposeData` and `JobQueue.ExposeData`, with restoration protected by a finalizer-style pattern.

Then implement the animal-screening UX rule:

- no proactive screening option when `Contagion_AnimalSick` is present,
- proactive job fails if `Contagion_AnimalSick` appears mid-job.

After that, rerun the exact Jeffrey-style removal test. If the pawn still breaks while carrying the corpse with no current job saved, revisit carry-state serialization as a separate, narrower issue.
