# Task-013: Direct Schematic Authoring MVP

Authority: Product Owner conversation on 2026-09-24. This continues Task-013 on
the existing Component/Auto branches; it changes the primary workflow to manual
multi-page schematic authoring. Earlier unfinished automatic-layout work is
preserved and must not be represented as accepted.

## Confirmed Scope

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
- Existing archive, project repository, revision history, cable authorities and
  isolated AutoCAD executor are reused. Production data and approved assets stay
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
6. Build direct-authoring Plan/IR projection and test unchanged user coordinates
   through the existing isolated exporter; incomplete engineering stays blocked.
7. Run focused/full tests/build and actual UI flows on a new disposable project.
   Native UI evidence is reported separately from service/API tests.
8. Publish candidate launcher and handoff, update existing Draft PRs, leave
   PRODUCT_OWNER_VISUAL_ACCEPTANCE=PENDING until the owner reviews.

## Current Checkpoint

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
