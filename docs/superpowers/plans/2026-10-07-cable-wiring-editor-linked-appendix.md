# Cable Wiring Editor and Linked Appendix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking. The user approved the spec and explicitly requested direct execution after this plan, without another approval round.

**Goal:** Edit cable-internal exact-Pin wiring visually and export one linked appendix section per physical cable.

**Architecture:** Preserve the existing authoritative `CablePinMapping` pairs. Add an inventory-only table mode and a separate saved diagram layout, staged in `CableManufacturingDraft`. Compose export pages and links from a cloned project and share the diagram presentation between WPF and PDF.

**Tech Stack:** C#/.NET 8, WPF, System.Text.Json, existing PdfSharp, xUnit, offscreen WPF smoke executables. No new paid dependencies.

**Spec:** `docs/superpowers/specs/2026-10-07-cable-wiring-editor-linked-appendix-design.md`

## Global Constraints

- Do not use AO, operate native windows or modify the central workbook or existing project database.
- Preserve the seven preexisting unrelated dirty files; do not include them in commits or delivery.
- Exact source identities, numbering gaps, instance isolation and immutable template snapshots remain authoritative.
- Geometry never creates continuity. NC/unused cannot be wired; electrical edits invalidate confirmation.
- New semantics require archive v7; old schemas remain readable without automatic rewriting.
- Export is atomic, single-save and does not mutate project state. Main sheets precede appendices.

## Review Focus

- Historical rows with unbound function text must reject ambiguous migration without losing text.
- Two physical instances of the same template must never share an appendix by model or topology hash.
- Existing continuation destinations remain valid when dedicated cable pages are moved to the appendix.
- Layout-only saves must not invalidate confirmed mapping or modify sibling instances.
- Large Pin inventories and long labels must not be silently clipped; exact data survives tiling/pagination.

## Task 1: Inventory Mode and Diagram Editing Domain

**Files:**
- Modify: `src/ComponentIntelligence/Electrical/Domain/ArchivedCableTemplate.cs`
- Modify: `src/ComponentIntelligence/Electrical/Schematic/CableManufacturingDraft.cs`
- Modify: `src/ComponentIntelligence/Electrical/Schematic/CableManufacturingEditorService.cs`
- Create: `src/ComponentIntelligence/Electrical/Schematic/CableWiringEditorService.cs`
- Create: `src/ComponentIntelligence/Electrical/Schematic/CableWiringDiagramPresentation.cs`
- Test: `tests/ComponentIntelligence.Tests/Electrical/CableWiringEditorTests.cs`

**Interfaces:** Add `InventoryOnly`, `ExplicitMapping` and `WiringDiagram` to the staged draft; manufacturing metadata stores `InventoryOnly` and `WiringDiagram`. `CableWiringDiagram` stores Port bounds, exact Pin positions and deliberate edge routes keyed by canonical mapping pair. Existing template/binding Mapping remains the electrical authority.

`CableWiringEditorService.EnterVisualMode(CableManufacturingDraft draft)` migrates a staged copy; `EnsureLayout(draft)` initializes missing positions. `Connect(draft, fromPinId, toPinId)`, `Disconnect(draft, fromPinId, toPinId)`, `SetUsage(draft, pinId, usage)`, `MovePort(draft, portId, position)`, `MovePin(draft, pinId, position)` and `SetRoute(draft, fromPinId, toPinId, points)` validate and change only their declared concerns. `Build(draft)` returns `CableWiringDiagramPresentation` with `Primitives`, `Bounds`, `PortBounds`, `PinPositions` and `EdgePaths`.

- [ ] Write numeric/gapped inventory, independent row alignment, legacy branch migration, same-Port short, NC conflict, ambiguous old text, unknown endpoint, stale route, layout-only approval and instance-isolation tests.
- [ ] Run `dotnet test tests/ComponentIntelligence.Tests -c Release --filter FullyQualifiedName~CableWiringEditorTests`; expected new tests FAIL before implementation.
- [ ] Implement migration and canonical explicit pairs, independent table rows and diagram layout/routing. Retain legacy row semantics for old records.
- [ ] Run the new tests and existing manufacturing/multi-Port/CAD binding tests; expected all PASS.
- [ ] Commit only Task 1 files and record commands/results in execution evidence.

## Task 2: Persistence and Archive Compatibility

**Files:**
- Modify: `src/ComponentIntelligence/SymbolArchive/SymbolArchiveRepository.cs`
- Modify: `src/ComponentIntelligence/Electrical/Schematic/ArchivedCableInstanceFactory.cs` if clone/override changes require it
- Test: `tests/ComponentIntelligence.Tests/SymbolArchive/CableWiringDiagramArchiveTests.cs`
- Test: `tests/ComponentIntelligence.Tests/Electrical/CableWiringEditorTests.cs`

**Interfaces:** `CableWiringDiagramSchemaVersion = "ci-symbol-archive.v7"`; normalize schema upward only when new metadata is present or v7 already pinned. Existing Create/Prepare/Apply/ApplyToInstance APIs round-trip the new state. Explicit mapping overrides clear incompatible stale row/diagram geometry, without affecting siblings.

- [ ] Write v6 readback, v7 exact layout/mapping round-trip, invalid geometry rejection, and source/instance clone tests.
- [ ] Run archive-specific tests; expected FAIL before adding v7 support.
- [ ] Add schema normalization, finite coordinates and exact endpoint/route validation.
- [ ] Run archive, manufacturing and archived-instance test filters; expected PASS.
- [ ] Commit Task 2 files and update execution evidence.

## Task 3: Staged WPF Cable Wiring Editor

**Files:**
- Create: `src/ComponentIntelligence.Desktop/CableWiringDiagramEditor.cs`
- Create: `src/ComponentIntelligence.Desktop/CableWiringVisualRenderer.cs`
- Modify: `src/ComponentIntelligence.Desktop/CableManufacturingTableEditor.cs`
- Modify: `src/ComponentIntelligence.Desktop/CableArchiveEditorDialog.cs`
- Modify: `src/ComponentIntelligence.Desktop/SchematicWorkspaceControl.CableProperties.cs`
- Create: `validation/CableWiringEditorSmoke/` project and synthetic test program

**Interfaces:** `CableWiringDiagramEditor` receives the same staged manufacturing draft and emits Changed. Its host explicitly enters visual/inventory mode through an Edit Cable Wiring command, then refreshes table and diagram together. `CableWiringVisualRenderer.Draw` consumes the domain presentation only. It must not write archive or project state.

- [ ] Add offscreen smoke tests for connect/delete/undo, group/Pin drag reroute, Ctrl-wheel/middle pan, numeric inventory editing, cancel and shared-draft save/reopen.
- [ ] Run the smoke executable; expected FAIL with missing editor/behavior before implementation.
- [ ] Implement select/wire/delete/NC/pending/unused, undo/redo, fit/zoom/pan, explicit wire-branch action, group and Pin moves, and deliberate bend editing.
- [ ] Add library and placed-instance entry points, commit staged edits before refresh/save, and disable table row connectivity controls in inventory mode.
- [ ] Run domain tests, Desktop build and offscreen WPF smoke; expected PASS and no native windows.
- [ ] Commit Task 3 files and evidence.

## Task 4: Linked Export-Only Cable Appendix

**Files:**
- Create: `src/ComponentIntelligence/Electrical/Schematic/CableWiringAppendixPlan.cs`
- Create: `src/ComponentIntelligence.Desktop/CableWiringAppendixRenderer.cs`
- Modify: `src/ComponentIntelligence.Desktop/SchematicPdfWriter.cs`
- Test: `tests/ComponentIntelligence.Tests/Electrical/CableWiringAppendixTests.cs`
- Extend: `validation/CableWiringEditorSmoke/` PDF checks

**Interfaces:** `CableWiringAppendixPlan.Build(ElectricalProject project)` produces export main sheets, unique cable sections and forward/return link specifications by physical identity. Stable page identities, not old PDF indices, resolve existing continuation links. The appendix renderer consumes the same domain diagram presentation as the editor and an export tile; it neither infers mapping nor changes the source draft. The existing PDF Write signature and integer page-count result stay compatible.

- [ ] Write main-before-appendix, repeated-view dedup, same-model distinct-instance, missing inventory, mixed-detail preservation, continuation relocation, and serialized-input-unchanged tests.
- [ ] Run appendix filter; expected FAIL before implementation.
- [ ] Implement export-only cloning/planning, preserved dedicated details, readable tiling for large diagrams, body/explicit-cable segment hit boxes and return destinations.
- [ ] Modify PDF writer to render all planned pages and add document links before its one final atomic save.
- [ ] Verify real PDF link annotations, destinations, page count and rendered synthetic appendix pixels; existing destination and injected render failure leave input/files intact.
- [ ] Commit Task 4 files and evidence.

## Task 5: Review, Regression and Test Bundle

**Files:** delivery scripts/docs and synthetic validation evidence only; no private asset or data publication.

- [ ] Run full Release domain suite and all affected WPF smoke programs.
- [ ] Request independent whole-feature review focused on the five Review Focus risks; reproduce Important findings with failing tests before fixing.
- [ ] Run the full suite again after fixes. Verify only intended files are committed.
- [ ] Export a clean committed source tree and publish Desktop from it. Reuse user test-data paths without launching or modifying data.
- [ ] Validate launcher runtime hashes, document version/entry points, and report human acceptance steps and any limits honestly.

## Execution Evidence

Spec approved in chat on 2026-10-07; direct execution without further approval was explicitly selected. Existing linked worktree reused. Baseline: `dotnet test tests/ComponentIntelligence.Tests/ComponentIntelligence.Tests.csproj -c Release --no-restore --nologo --verbosity minimal` PASS 1336/1336. Abstract Antigravity advice was reviewed; no private files were sent. Deduplication by model/topology hash was rejected: physical identity is the required key.

Task 1: domain tests RED (missing CableWiringEditorService), then GREEN 77/77 including wiring, manufacturing, multi-Port and CAD bindings. Diagram geometry is not stored as another electrical graph. Existing SchematicDraftRoute handles orthogonal simplification. Instance layout edits preserve confirmation and template snapshots.

Task 2: archive schema regression RED (expected v7, actual v6), then GREEN 128/128 across wiring, archive, manufacturing and archived instances. V6 remains unchanged until an explicit visual edit; v7 round-trips all exact layout and mapping state. Test fixture pending-CAD records use zero hashes and Candidate status, matching the existing contract.

Tasks 3/4: shared WPF editor and PDF export implemented. Offscreen smoke verifies explicit branching, NC error feedback, delete/undo/redo, staged cancellation, independent inventory controls, border-attached Pin dragging, save/reopen and nonblank rendering. PDF tests verify atomic render failure, refused overwrite, immutable input, repeated physical-instance views, distinct same-model instances, preserved manufacturing detail and 300-Pin readable tiling without omitted contacts. Real PDF destinations verified with pypdf: four-page document `[[3,4],[3],[1,2],[1]]`; relocated-detail five-page document `[[3,4,5],[4],[1],[1,2],[1]]`.

2026-10-08 user correction: a dragged GND must stay on its Port frame. Three attachment regression assertions RED, then GREEN 10/10 wiring tests. Nearest-border projection is shared by editor and PDF; older detached geometry is repaired in a presentation clone without modifying electrical mapping or render input. Final synthetic PDF inspected after repair. NC interaction feedback regression also went RED before the transactional UI error fix.

Project compatibility: saved new cable semantics require project schema 0.8; existing projects without wiring metadata retain 0.7. SQLite round-trip regression RED (0.7 instead of 0.8), then GREEN. Existing source-text assertion was updated from `render(project,sheet)` to the new export snapshot contract; actual output smokes prove rendering, not just the source assertion.

Current working-tree verification: Release domain suite PASS 1384/1384; CableConnectorEditorSmoke `all` and `wire` PASS; CableManufacturingSmoke `all` and `multi` PASS; CableWiringEditorSmoke with `pdf` PASS. These use synthetic data and never launch native windows or write the user archive/database. Seven unrelated dirty files remain excluded from delivery.
