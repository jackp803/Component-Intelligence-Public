# Compact Wire Legends Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans. User authorized direct implementation without repeated approvals. Do not use AO.

**Goal:** Replace crowded remote wire descriptions with adjacent function/index labels, one exact-size legend per group, and complete UI/PDF details.

**Architecture:** Presentation-only W01 indices map to original wire identities. A domain projection retains complete contacts, functions, colors, sizing and review evidence. The WPF canvas draws short local captions and grouped specifications; full details appear in a read-only dialog and paginated PDF appendix.

**Tech Stack:** Existing .NET/WPF, xUnit, PDFsharp and offscreen validation harnesses.

**Spec:** User approved the proposed compact labels, complete details table and identical-specifications-once option on 2026-10-13.

## Global Constraints
- No changes to electrical connectivity, Pin numbering, routes, manual gauges or saved wire designations.
- No AO, paid providers, central workbook modifications or live-window operations.
- Preserve unrelated dirty files; use the already attached task worktree.
- Full functions and review caveats remain available. Explicit Pin names take precedence over inferred abbreviations.
- Group by exact AWG, area, approximate flag and pending review status, not rounded display text.

## Review Focus
- Long multifunction descriptions need explicit short names without guessing an electrical function.
- Nearly equal areas and manual/provisional specifications must not share a group.
- Vertical wires, dense parallel spans and old remote saved labels must remain locally identifiable without covering ink.
- Details pagination must not crop oversized cells or silently omit rows.
- Cable appendix deduplication/navigation and drag performance must remain intact.

## Tasks
1. **Domain projection:** Add `SchematicWireLegendPresentation.cs`; add complete-text metadata to `SchematicCableLabel`; expose explicit short Pin function. Write a failing compact-caption regression first, then grouping, projection immutability, unknown/long-function and persistence tests.
2. **WPF presentation:** Modify `SchematicWorkspaceControl.WireLabels.cs`; use one-line adjacent labels, local search only for ordinary wires, vertical-span placement and an exact-spec group legend. Add read-only wire details dialog and toolbar command. Preserve complete warning/tooltip data. Run offscreen dense/vertical/quarter-zoom/saved-position checks.
3. **PDF details:** Add `SchematicWireDetailsRenderer.cs`, append paginated complete wire details in `SchematicPdfWriter.cs`, after existing cable sections. Use measured cell heights, fail explicitly for unprintable cells, and provide return navigation. Protect cable appendix tests.
4. **Verify/deliver:** Run full xUnit suite, WPF/PDF smoke tests and image inspection, request independent review, fix important findings with regression tests, build a clean BK runtime/source bundle and validate launcher hashes without starting the app.

## Execution Record
- Baseline: HEAD 01fbdbdb001f7f0bf6eeaa68c958cbcee38ac221; seven unrelated dirty files preserved.
- Ruling: Use W01 as a presentation index, not a replacement wire designation. Full table is appended to PDF rather than crowded onto the engineering sheet; costs extra appendix pages, guarantees complete metadata.
- Ruling: Unknown/long functions without an explicit short Pin name show 'function in details' rather than inventing AO/polarity. Original text is retained in details.
- Ruling: Prefer the same side of every parallel span, trying normal then compact type before the opposite side. A paper-edge fallback uses a short attribution tick, not a remote dashed leader. Saved remote/wrong-side positions are reflowed for display only.
- Ruling: W indices are drawing lookup indices, not durable electrical designations; adding/removing wires can renumber them. The original component-based designation is never overwritten.
- Ruling: PDF details follow the existing raster export pipeline; no searchable PDF text is claimed. Full wrapped content and return links are preserved.
- TDD: The compact-caption test failed before implementation. Five domain regressions now cover explicit short Pin names, exact grouping, long unknown functions, hidden-page isolation/human page numbers, and conflicting raw endpoint colors.
- Independent review: Reproduced missing paper-edge captions, manufacturing-table clearance, lost raw endpoint color suffixes, quadratic index preparation and unwrapped dialog cells. Added failing regressions and verified fixes. Actual rendered detail bounds replace the fixed-height blocker; measured PDF borders match rendered cell widths/heights.
- Working-tree verification: 1484 xUnit tests passed, including three preexisting unrelated drawing tests. Offscreen WPF checks passed for dense/vertical/quarter-zoom captions, remote saved positions, paper-edge attribution ticks, actual tall-table clearance and complete warning behavior.
- PDF verification: 160 details rows paginate to 14 pages with zero measured cell overflow; first/last pages visually inspected. Oversized individual rows fail explicitly rather than crop. Existing physical-cable deduplication, forward/return links and atomic failure tests passed.
- Review follow-up: All findings resolved by read-only independent inspection. A final A4 portrait header regression failed with the old fixed 8 mm height, then passed with shared measured header/data-row sizing (10.23 mm for this fixture). Pagination reserves the same measured height as rendering.
- Performance verification: Synthetic 1000-caption preparation fell from 5850 ms in the reproduced regression to 84.048 ms after per-page metadata/index caching. This is not a native UI FPS claim. Lightweight drag previews, routes, electrical identities and source snapshots remain unchanged.
- Delivery gate: Build/test a fresh scoped-source archive, publish BK, rerun smoke fixtures from that source and validate all launcher hashes without starting the app. Record exact clean counts and artifact identity in the bundle manifest.
