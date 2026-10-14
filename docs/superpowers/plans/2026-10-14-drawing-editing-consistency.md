# Drawing Editing Consistency Implementation Plan

> For agentic workers: Use executing-plans inline for core integration, with bounded
> disjoint Codex workers and one independent final review. The human approved
> the consolidated scope and explicitly requested implementation.

**Goal:** Make edit, move, label, batch-delete and refresh workflows stay consistent.
**Architecture:** Preserve electrical identities; add deterministic presentation
and transactional editing boundaries around existing authoring APIs.
**Tech Stack:** .NET 8, WPF, xUnit, SQLite, current PDF renderer.
**Spec:** ../specs/2026-10-14-drawing-editing-consistency-design.md

## Global Constraints

- No AO, production data/DWG changes, external publication or paid fallback.
- Keep unrelated dirty drawing-planning files unchanged.
- Only exact IDs/tags establish conductivity or CAD binding.
- Persisted new presentation fields must fail closed in older versions.
- No project-wide graph or full redraw per pointer sample.

## Review Focus

- Shared/locked/manual routes and deliberate branch junctions.
- Mixed batch settings and orphaned cross-page markers after deletion.
- CAD refresh with reordered, missing or duplicate contact tags and two instances.
- Very narrow frames, multiline descriptions and white wire outline clearance.
- No undocumented engineering approval or manufactured cable spec from a label.

### Task 1: Safe lifecycle edits

Owner: bounded worker; files SchematicAuthoringService.Lifecycle.cs and
tests/Electrical/SchematicLifecycleTests.cs only.
Interfaces: DeleteWires(project, ids); DeleteRepresentations(project, ids,
deleteConnections); DeletePage(project, pageId, destinationPageId,
deleteContents). Add impact projection as needed. Preserve physical inventory.
- [x] RED tests for connected/batch/page deletion, locks, branches, cross-page cleanup.
- [x] Implement atomic clone-edit-validation; GREEN focused tests.

### Task 2: Explicit placed CAD refresh

Owner: bounded worker; files SchematicAuthoringService.CadRefresh.cs and
tests/Electrical/SchematicCadRefreshTests.cs only.
Interface: RefreshCad(project, symbolIds, geometry, assetPath, revision).
Exact bindings must remain available or operation fails without mutation.
- [x] RED tests for changed positions, missing/duplicate tags and preserved identity.
- [x] Implement refresh plus route reanchor; GREEN focused tests.

### Task 3: Joint orthogonal routing and label semantics

Owner: lead; authoring route methods, SchematicOrthogonalRoute, wire presentation
and legend files; focused domain tests; desktop WireLabels partial.
- [x] Reproduce shifted six-Pin bundle overlaps and manual name mismatch RED.
- [x] Add route reservations and stable independent channel selection; GREEN.
- [x] Actual designation captions, readable spec formatting and in-sheet labels.

### Task 4: Nonconductive Port fan-out

Owner: bounded lifecycle worker plus lead rendering; SchematicDocument/authoring fan-out partial, AnchorPoint, Port
presentation, persistence migrator, WPF symbol rendering; focused tests.
- [x] RED endpoint/render/persistence tests, retain native CAD bindings.
- [x] Add compact offset layout and graphical fan-out with exact Pin IDs; GREEN.

### Task 5: Integrated editing UI

Owner: lead; new Selection/Lifecycle/CadRefresh desktop partials, toolbar/menus,
existing input/render handlers, contextual inspector.
- [x] Offscreen WPF RED tests for selection, captions and ordinary-wire PDF links; domain deletion/refresh.
- [x] Wire new domain APIs, impact confirmation, batch fields and refresh preview.
- [x] Separate missing-data review from layout messages; check all labels on-sheet.

### Task 6: Verification and delivery

- [x] Full domain suite, WPF screenshots, PDF link/layout regression, performance check.
- [x] Independent review; important findings reproduce RED then fix GREEN.
- [x] Commit only scoped edits; clean-source rebuild and isolated BN runtime bundle.
- [x] Verify launcher/source/runtime hashes without opening the production app.

## Evidence So Far

2026-10-14: domain suite 1,705 passed; lifecycle 35, CAD refresh 23,
fan-out 36, routing 20, plus movement/order/name/spec/batch regression cases.
Offscreen editing, dense-wire readability, mating, manufacturing, wiring PDF,
workspace and performance proofs passed. Single-page redraw 15.3 ms, all-pages
110.9 ms in final synthetic benchmark; linked-block drag preview p95 0.010 ms.
These are local synthetic measurements, not a guarantee for every real project.
Boundary tests now explicitly reject overlapping module exits and out-of-sheet
draft leads, then exercise unchanged identity assertions at valid positions.
Independent review findings closed: hidden batch across page navigation, exact
metric-source reuse, moved-body obstruction, fan-out reorder, manual-bend
preservation, accidental automatic endpoint touch, and slow fallback routing.
130-reservation fallback improved from 5,848 ms RED to 12.757 ms independently
verified; owned outward stubs cannot retrace, and grids above 1,000,000 nodes
fail before allocating search arrays. All tests use synthetic data.

Delivery completed from code commit ee74918c1b67a2a066f44abab1a00d1074c5a808.
Clean archived source: 1,702 tests passed (unrelated preexisting dirty changes
were excluded). Clean-source editing/readability/mating/manufacturing/wiring,
workspace and performance proofs passed. Runtime product version
BN-2026.10.14+ee74918; 222 runtime files and the source ZIP passed SHA-256
launcher validation. Existing AU data and selected central archive paths were
verified read-only; StartedSoftware=false. Native user UAT remains pending.
