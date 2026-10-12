# Schematic Resize And Interaction Performance

> **For agentic workers:** Use superpowers:executing-plans for inline implementation; request an independent scoped review before delivery.

**Goal:** Let manufacturing sketches enlarge when actual content fits on the sheet, and reduce measured redundant work during drawing gestures and zoom.

**Architecture:** Keep validated copy-on-write authoring as the commit authority. Fit bounds represent visible CAD geometry/text rather than invisible text origins or unused maximum dimensions. Keep WPF presentation work on the UI thread, render only visible pages, coalesce pointer previews with a single pending dispatcher callback and flush it before release. Cache unchanged gesture bounds; zoom only changes the existing transform.

**Spec:** User reports manual enlargement blocked with `線材明細位置與尺寸超出有效圖面` despite room around a compact selected sketch, and lag while moving modules or generally interacting. Previously authorized direct implementation/tests/review without repeated approval. No promise of arbitrary out-of-page placement or machine-independent FPS.

## Global Constraints

- No AO, native-window operation, live database writes, original CAD edits, paid fallback or upload of company material.
- Preserve table size/overlap, true sheet-edge guards, electrical mapping, one undo record per release and cancellation.
- Reuse current worktree; exclude seven unrelated DrawingPlanning changes from delivery.
- Screen/PDF/DXF use the same fitted content; presentation-only caches must not alter snapshots or persist.

## Task 1: Actual Sketch Bounds

- [x] Add regression for a wide, short sketch whose requested maximum box exceeds the page but whose complete scaled content fits; actual overflow still rejects atomically.
- [x] Add empty-label origin and partial-arc regressions; validate nonfinite properties even for invisible text.
- [x] Fit actual curved extrema and visible text envelopes, publish effective sketch bounds, then validate the effective box for page/table overlap. Retain strict finite and positive layout checks.
- [x] Exercise visible-corner enlargement near a sheet edge, no-jump resize, save/reopen and actual WPF ink containment.

## Task 2: Interaction Work

- [x] Measure a deterministic synthetic multi-page CAD fixture before changes; record dimensions/iterations and timings without claiming live-window latency.
- [x] Red tests: hidden pages remain unrendered, zoom retains visual instances, burst pointer requests coalesce, release flushes the last position, cancel/page-switch invalidates queued work.
- [x] Repaint only visible pages, update selection once, avoid CAD rebuild on zoom, and cache gesture-start selection bounds. Coalesce redundant pointer work on the dispatcher without dropping final edits.
- [ ] Verify selection-only clicks, routes/Port/Pin gestures, undo/redo, wires/placement, multi-page toggle, export and cancellation remain correct.

## Task 3: Delivery

- [ ] Full suite, offscreen WPF regressions, PDF/link checks and independent local review.
- [ ] Build clean BI source/runtime package, record before/after synthetic timing and invariants, validate launcher without starting software.
- [ ] Preserve original workbooks/archives and readonly live-project access; deliver BI link with no reimport needed.
