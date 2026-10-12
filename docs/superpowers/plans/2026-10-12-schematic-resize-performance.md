# Schematic Resize And Interaction Performance

> **For agentic workers:** Use superpowers:executing-plans for inline implementation; request an independent scoped review before delivery.

**Goal:** Let manufacturing sketches enlarge when actual content fits on the sheet, and reduce measured redundant work during drawing gestures and zoom.

**Architecture:** Keep validated copy-on-write authoring as the commit authority. Fit bounds represent visible CAD geometry/text rather than invisible text origins or unused maximum dimensions. Keep WPF presentation work on the UI thread, render only visible pages, coalesce pointer previews with one Background-priority callback (below Input) and flush it before release. Cache unchanged gesture bounds; zoom changes the existing transform and updates wire weights in place.

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
- [x] Verify route previews, undo/redo, table/sketch gestures, multi-page toggle, export and cancellation with offscreen tests; retain the unchanged click threshold and Port/Pin command branches and their domain coverage. Native-window acceptance remains with the user.

## Task 3: Delivery

- [x] Full suite, offscreen WPF regressions, PDF/link checks and independent local review. Two P2 findings were reproduced RED and fixed: Input-priority ordering and zoom-dependent wire thickness. Follow-up checks queue 100 Input events and cover selected/unselected bodies, jump arcs and DC-negative outlines.
- [x] Build clean BI source/runtime package, record before/after synthetic timing and invariants, validate launcher without starting software.
- [x] Preserve original workbooks/archives and readonly live-project access; deliver BI link with no reimport needed.
