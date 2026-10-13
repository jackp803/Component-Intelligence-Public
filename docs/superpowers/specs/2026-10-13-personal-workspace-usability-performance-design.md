# Personal Electrical Workspace: Usability and Performance

Status: written specification for user review; implementation has not started.
Date: 2026-10-13
Baseline: BK, commit `2b138072872db39729df870e28c2d335e7fb0b0b`.

## Intent and Agreement

The user is the sole intended operator. The goal is a faster, less interruptive daily drawing workflow, not a new public-product onboarding experience. Preserve familiar project, BOM, CAD, cable and engineering workflows.

Confirmed in conversation:
- Discuss before changing production code.
- Prioritize dragging modules and drawing wires, the previously reported slow interactions.
- Selecting an item should expose its common settings in a fixed right-side inspector.
- Keep complex cable topology editing in its existing large window.
- No AO.

The following is the proposed first-round scope, awaiting written-spec confirmation. That confirmation permits writing the implementation plan, not silently expanding to unrelated subsystems.

## Approach

Use phased improvements to the existing WPF application. Cosmetic-only changes would not establish a performance improvement; a full rewrite would increase retraining and regression risk. The first round covers the engineering workspace, its project state, local preferences and measured interaction performance. The existing BOM home, central archive, Topology, Layout and Drawing Planning remain compatible; their redesign is not part of this round.

## User Experience

### Drawing Workspace

- A compact project strip shows project name, unsaved/saving/saved/failed state and access to the actual project database and selected central-workbook paths. Project IDs remain available without dominating the drawing area.
- Keep selection, wire mode, finish/cancel wire, undo/redo and zoom readily accessible. Organize secondary commands into predictable menus and contextual actions; do not remove existing commands or keyboard shortcuts.
- Use familiar icons with tooltips; retain words where the command is not recognizable from a standard icon. Use dropdowns for options, checkboxes/toggles for flags and numeric controls for dimensions.
- Make the left page/BOM area collapsible and resizable. Give the drawing the remaining space. Remember panel widths and collapsed state, with a restore-default-layout command.
- The right inspector is scrollable and resizable within sensible minimum/maximum widths. Narrow windows must not hide Apply/Cancel or create overlapping text. Keep existing photographs and CAD linework.
- Empty selection shows page context, not editable fields belonging to the last selection. Changing selection must never apply a draft to a different item.

### Contextual Inspector

Module selection:
- Reference, position, dimensions, rotation/lock and existing CAD scale where applicable.
- Port/Pin inventory, visual order and entry points to exact-contact editing/binding.
- Explicit actions distinguish changes to this project from saving a reusable module layout. No automatic central-archive update.

Ordinary-wire selection:
- Original designation and both exact endpoints; compact W index remains a lookup index, not the persisted wire name.
- Function and color with their endpoint provenance. To edit endpoint-derived values, target the specific Pin explicitly rather than silently rewriting both ends or inventing electrical continuity. Display-only changes must be identified as such and must not conceal physical-color conflicts.
- Existing automatic/manual sizing modes, AWG and conductor area in mm2, with proposal caveats retained. Switching selection or just viewing the inspector must not accept/recompute a suggestion or overwrite a manual size.
- Assigned commercial/custom cable and detailed Pin mapping remain accessible through existing cable editors. Do not treat a physical cable body as an independent ordinary conductor.

Manufacturing table/sketch selection:
- Position, table width/text scale and sketch size/visibility using the existing layout authority.
- Direct edit/delete actions for the selected representation. Deleting a table/sketch must not delete the physical cable, its mapping or a different representation.
- Keep complex connection-table contents and topology in the existing large editors.

### Editing Contract

- Populate controls without committing changes or creating undo entries.
- Stage a small group of field changes, then commit on Enter or Apply; Escape/Cancel discards the staged values. Numeric entry must not mutate the model on each keystroke.
- On selection changes, an uncommitted valid draft requires Apply/Discard/Cancel only if it is actually dirty. Invalid input stays visible with an inline error and cannot be applied to the next object. No prompts for unchanged controls.
- Each successful Apply is one undoable transaction through existing authoring services. Validate locks, ranges, finite numeric values, contact separation and paper bounds before mutation.
- Rejected edits leave project, undo history and saved state untouched. Refreshing the inspector must not move focus or replace a user's active draft.
- Preserve connected short function captions, unconnected Pin labels, grouped wire specifications and complete UI/PDF metadata from BK.

## Project State, Preferences and Recovery

- Track unsaved state against the last successfully persisted project, independently of the latest transient status message. Undo back to that state can clear the dirty indicator; inspector drafts are tracked separately.
- Capture a stable snapshot when saving. If edits happen while the save awaits I/O, finishing the older save must not replace newer edits or falsely mark them saved. Prevent duplicate overlapping save/switch requests.
- New/load/close with unsaved edits offers Save/Discard/Cancel. A failed save keeps the project open and dirty. Preserve incomplete-wire and editor-draft rules rather than silently finishing them.
- Remember panel configuration, display preferences and recently successful import/export directories in local application preferences separate from the electrical project and central workbook. Unknown or corrupt preferences fall back safely. Do not automatically reuse a stale selection or move the active database.
- Reuse existing project revision/checkpoint infrastructure for recovery, not a second competing project format. Capture only completed mutations, never an in-progress drag or incomplete numeric input.
- Proposed default: after two minutes idle with uncheckpointed completed edits, write a recovery checkpoint; skip unchanged state and active gestures. Retain at most 20 automatic recovery checkpoints per project without deleting manual/save/review revisions. Offer recovery explicitly when applicable; never restore it automatically or call a checkpoint a successful formal save.
- Recovery operations must be serialized, bounded and failure tolerant. Missing drives or disk failures must be visible without deleting good revisions or blocking continued in-memory editing. Validation uses isolated databases, not live project data.

## Performance Strategy

Existing BK already coalesces pointer previews, batches immutable CAD linework, retains the zoom visual tree, compresses undo snapshots and caches wire-label metadata. Preserve these paths and measure the remaining cost before choosing changes.

Measure separately:
- Pointer preview for module drag, pending wire and detail drag/resize.
- Commit on mouse-up and consequent route/label recomputation.
- Selection/inspector population, zoom, page switching, save and export.
- Allocations, GC pauses, working set and repeated disk/CAD/image reads.

Candidate changes must follow evidence:
- Update retained transforms/routes for the selected item and its incident wires; avoid repeated whole-sheet child scans and recreating unchanged shapes on every pointer event.
- Keep immutable pictures/geometry and endpoint lookups cached with explicit invalidation. Never share a mutable visual between incompatible canvases or grow caches without bounds.
- Coalesce input updates without dropping the final pointer position; flush once on commit, restore correctly on cancel/capture loss.
- Invalidate only affected page presentation where correct; collision-sensitive routing and labels must still account for all relevant obstacles.
- Run safe I/O and pure preparation off the UI thread. WPF objects retain dispatcher affinity; render/export may require bounded dispatcher work. Do not blindly wrap all rendering in Task.Run.
- No explicit GC.Collect in interactive paths; no GPU driver, OS, billing or hardware changes. Record the actual rendering tier/CPU/memory observations rather than claim buying RAM or enabling a new renderer is necessary.

Measurement gate:
- Repeat comparable runs using the same machine, project, zoom and viewport, with a warm-up and no simultaneous builds. Report median/p95 and worst stalls, not one favorable sample or metadata timing presented as FPS.
- Target p95 pointer-preview processing under 16.7 ms and no repeated preview work item over 100 ms on the declared benchmark. These are acceptance targets to verify, not a current performance claim. Also measure user-visible input-to-present behavior where available.
- Compare small, representative and dense synthetic projects, and a local read-only/isolated snapshot of the user's project when available. Declare counts of visible symbols, wires, CAD primitives and pictures.
- Do not satisfy targets by hiding functional content, disabling evidence/validation, losing routes or reducing saved data. If a fixture cannot meet the target, report the measured remaining bottleneck and scope the next optimization rather than call the target passed.

## Boundaries and Compatibility

- No framework/GPU-renderer rewrite, public onboarding, multi-user permissions, installation-package overhaul or repository-wide refactor in this round.
- No automatic BOM additions, electrical-net merging, Pin renumbering, cable conduction guesses, manufacturer-approved sizing claims or central-workbook writes.
- Existing project IDs, persistence location, archived CAD assets, cable identities, manual wire routes and saved names remain compatible.
- PDF main pages, exact grouped specifications, full ordinary-wire details, cable-appendix deduplication and forward/return navigation remain intact.
- Preserve all unrelated dirty worktree files. Use scoped commits and a clean archive/runtime for delivery.
- Do not manipulate the user's live windows, original DWG files or formal project database during tests.

## Verification and Delivery

- Domain/service tests: staged edits, validation failure, lock rules, correct endpoint/representation targeting, single undo, redo, inspector refresh/no-op and save races.
- Persistence tests: preferences survive restart, corrupt/missing files are tolerated, unsaved-state transitions are truthful, recovery retention excludes nonautomatic revisions, and recovery never overwrites a newer formal save.
- Offscreen WPF tests and screenshots: selection for module/wire/table/sketch/empty state, panel collapse/resize, draft handling and numeric validation, viewport/DPI constraints, focused text and shortcut coexistence.
- Exercise 1280x800 and 1920x1080 viewports with 100%, 125% and 150% simulated scaling as supported. Do not change the user's Windows scaling. Verify actual available logical client sizes and document limitations.
- Regression tests: Port expand/collapse/order, contact attachment, module/CAD resize, manual route commit/cancel, pin colors/function labels, grouped legend/PDF, BOM isolation, save/reopen, existing complex editors and central-library selection.
- Performance report stores baseline/final fixture and machine information, timings and memory observations, with separate synthetic/offscreen and native acceptance labels.
- Independent review, fixes with focused regressions, full clean-source test suite, image/PDF inspection and a new sibling release bundle. Verify runtime/source hashes and launcher paths without starting the formal app.
- User acceptance checks their actual drag/wire workflow; this must not be represented as completed by offscreen benchmarks.

## Next Gate

User reviews this specification. After confirmation, write the detailed implementation plan. Production implementation begins only after the plan/execution handoff required by the agreed workflow; approval of the right-side inspector alone does not authorize unreviewed changes to recovery or persistence behavior.
