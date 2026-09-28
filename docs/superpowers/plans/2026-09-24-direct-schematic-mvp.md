# Task-013: Direct Schematic Authoring MVP

Authority: Product Owner conversation on 2026-09-24. This continues Task-013 on
the existing Component/Auto branches; it changes the primary workflow to manual
multi-page schematic authoring. Earlier unfinished automatic-layout work is
preserved and must not be represented as accepted.

## Confirmed Scope

### Owner Decisions: 2026-09-29 (Supersede Earlier Proposals)

- Primary authoring is the schematic, not Topology or a second Planner. Do not
  immediately delete existing AutoCAD output code: retire old entries separately
  after the new workflow is usable. AutoCAD execution is not an MVP gate.
- Add from the cable library creates one physical cable instance with independent
  identity, Reference, length, specification and connections. Another representation
  references the same instance and does not duplicate cable/member material counts.
  Unknown length/specification does not prevent draft creation. Imported cable
  geometry must expose selectable external contacts, not a generic box or flat image.
- Pin mapping is separate from appearance. Pin exact template and mapping versions.
  Appearance-only drafts allow known external wiring but visibly retain unconfirmed
  internal mapping; no inferred continuity. Instance mapping edits affect only that
  instance, require confirmation, and never alter approved/shared revisions.
- Free wire ends remain unfinished, never inferred NC/ground. Real contacts bind
  exact Pin identity; multi-Pin Ports are selectors, not conducting nodes. Branch
  creation requires explicit wire snap/intent; crossings and geometry moves never
  create connectivity. Continuations require explicit pairing, not matching text.
  Rewiring and geometry editing are separate commands.
- First release supports multiple archived, confirmed representations of one
  component, without arbitrary block decomposition. Shared Reference updates all
  representations; representation deletion must not silently delete the physical
  instance or attached engineering connections. No implicit internal conduction.
- Owner terminal statement: a branch from the same wire is shown with a circular
  terminal mark and needs no special counting. Pending precise clarification:
  whether this is presentation-only without automatically creating terminal/BOM
  authority. Do not infer a terminal model or rewrite existing terminal identity.
- Save editable instances, exact asset/mapping versions, bindings, pages, routes,
  text, continuations and confirmation state. Print and complete multipage PDF use
  the same saved drawing, company frame, page totals, grid and references; no second
  layout. Unfinished projects remain saveable/exportable as clearly marked drafts
  with inspectable issues, never automatically engineering-approved.
- No arbitrary user wire colours or earlier voltage palette. Research standard
  number, edition, scope and clause authority without paid access. Separate physical
  conductor identification, schematic presentation and interaction highlighting.
  Use labels, destinations, patterns, spacing and tracking for distinguishability,
  including monochrome PDF. Unverified rules remain product rules or pending.

- 2026-09-28 owner clarification supersedes the historical execution-output
  requirements below: this application is the primary schematic editor.
  AutoCAD is an auxiliary authoring tool for imported blocks/templates only.
  WDP/DWG generation, execution packages and real AutoCAD execution are NOT
  delivery gates. Do not continue the direct executor integration.

- No in-product AI service. Read the existing component data centre.
- The owner will create a new project for acceptance. No automatic rewriting of
  the existing representative project or its 180 connections.
- Normal components retain recognisable images with explicitly located pins.
- Imported CAD geometry is fixed. Position, rotation, references, text, cable
  fields and endpoint bindings are editable. Geometry is revised in AutoCAD.
- A single canvas owns pages, placements, paths and paired continuations. Output
  consumes those decisions without a second automatic layout.
- Draft wires can be saved before engineering endpoints are complete.
- Colours are centrally controlled. Do not claim a preview palette implements
  international conductor standards until its applicability is substantiated.
- Existing archive, project repository, revision history and cable authorities
  are reused. Production data and approved assets stay
  protected. No new symbol approval, merge or release.

## Execution Order / Durable Ledger

1. Preserve current WIP and record the failed terminal-page visual result.
2. Add versioned direct-authoring page/symbol/wire state, transactional commands,
   draft persistence, exact endpoint checks and migration tests.
3. Wire a real new-project WPF entry: data-centre palette, pages, placement,
   rotation, free orthogonal routing, paired continuations, selection and history.
4. Reuse archive imports for geometry/anchor binding; import company template
   geometry plus explicit page/grid metadata, preserving source hashes.
5. Complete route edits, branches, crossovers, cable detail authoring and shared
   displayed/exported geometry. No colour-based connectivity.
6. Verify the editable authored document, company template, exact anchors,
   page order, continuations and manual routes survive Save/Close/Reload.
   Incomplete engineering remains visibly unresolved but does not prevent draft
   authoring. No AutoCAD output prerequisite.
7. Run focused/full tests/build and actual UI flows on a new disposable project.
   Native UI evidence is reported separately from service/API tests.
8. Publish candidate launcher and handoff, update existing Draft PRs, leave
   PRODUCT_OWNER_VISUAL_ACCEPTANCE=PENDING until the owner reviews.

## Current Checkpoint

### 2026-09-28 Continuation

- Current priority: native company-template import and new-project authoring.
  The superseded uncommitted execution compiler and its four tests were backed
  up locally and withdrawn; no executor was connected to the product. Historical
  execution requirements/results below are retained for audit, not active scope.
  Current published code is 817236e; candidate o is the native editor candidate.

- Latest published detail checkpoint: b113df4. Linked cable detail uses existing
  cable authority, with normal point-to-point Save/Close/new-process Reload and
  edited-length Cancel proof. Multi-end detail has 1-to-2/1-to-3 automated proof,
  not native acceptance. Company cable CAD-template appearance remains open.
- Text fidelity continuation: baseline/rotation and multiline export corrections;
  full Release 1009 passed and candidate m build passed. Native m NOT_RUN after
  black capture plus failed window activation. Verified launcher still l.
- Direct Plan/IR/executor remains open: existing plan uses integer coordinates,
  authored CAD uses fractional millimetres. Preserve geometry and truthful
  provenance; no rounding or old-planner regeneration as an acceptance shortcut.

- Product Owner conversation is the current feature authority; do not restart
  Task-013 or let historical coordination handoffs override the agreed editor.
- Previous direct-authoring checkpoint was published as de7c312. Earlier
  "uncommitted" entries below are historical, not the current publication state.
- Added ordered all-sheet viewing, first-wire/other-sheet/second-wire pairing,
  command shortcut catalog/dispatch, pending placement preview, explicit AWG
  selection/area/persistence and exact catalog-core lookup (no inferred size).
- Fresh Release tests: 969 passed, zero failed/skipped. Desktop Release build:
  zero errors, 16 existing warnings. Diff check passed.
- Native disposable UI proved click/bend/double-click draft drawing, explicit
  AWG selection, two-page wire pairing, Ctrl+S, F6 all-pages, normal close and
  reload retaining page IDs, signal and AWG. Screenshot evidence stays local.
- UI exposed keyboard focus restoration after canvas selection and low-zoom
  wire visibility. Minimum screen weight was rebuilt/retested successfully.
  Further focus correction is pending native retest; do not label all shortcuts
  accepted. Placement Escape/drag, cable detail and direct export still need
  end-to-end verification.
- Production SQLite still matches the reconciled E6708879... baseline; no
  claim is made that the earlier historical write did not happen.
- Overall remains PARTIAL; continue the same execution order below.

- Local recoverable WIP backup created before implementation, including tracked
  patches and untracked files for both repositories. Private paths stay local.
- Previous terminal-page screenshot: FAIL. It groups terminals as standalone
  FieldDevices instead of integrating used contacts with the circuit. Its earlier
  automated results do not establish drawing quality.
- Step 2 implemented in uncommitted WIP: ElectricalProject 0.6 stores the new
  optional schematic document. Migration preserves the prior DrawingPlan. The
  version change prevents older readers from silently dropping authored state.
- Step 3 implemented in uncommitted WIP: a new-project schematic tab with catalog
  placement, draft wires, paired continuations, page controls, rotation, locks,
  history and the existing cable editors. Native startup/new-canvas loading has
  been observed on disposable data; end-to-end mouse acceptance is NOT_RUN.
- Step 4 partial: fixed DXF geometry and copied DWG/DWT conversion, explicit
  anchor binding and grid metadata. A real company DWT copy converted through
  accoreconsole with the original hash unchanged. MText, Hatch and some text
  formatting are not yet fully represented; the importer reports this rather
  than treating the template as complete. No new archive approval was made.
- Step 5 partial: segment movement, detours, bend removal, atomic page transfer
  and reunion, marker movement, endpoint checks and crossover geometry exist.
  Shared-pin dots and overlap diagnostics are tested. Electrical branching,
  automatic lane separation, complete cable-detail authoring and normal UI
  acceptance remain open. Physical Y must not create an electrical junction.
- Step 6 remains open: the new canvas is NOT connected to a no-replanning
  Plan/IR/export flow. Existing old-planner output is not evidence for this gate.
- Step 7 partial: latest full Component Release test run passed 962/962 with no
  skipped tests. The fresh candidate Release build includes the anchor
  notification, MText fallback and page-rename changes. None of these
  automated gates establishes visual or complete workflow acceptance.
- Step 8 pending. Current source is WIP, not yet a published executable lineage.
  PRODUCT_OWNER_VISUAL_ACCEPTANCE=PENDING; overall MVP remains PARTIAL.
