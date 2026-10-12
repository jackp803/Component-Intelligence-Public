# Manufacturing Sketch Fit Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan inline.

**Goal:** Place complete archived manufacturing sketches without the false source-text overflow error.

**Architecture:** Resolve dynamic fields into presentation-only primitives, measure conservative visible geometry and text bounds, and uniformly fit that content within the requested sketch rectangle. Do not change imported/native CAD coordinates, selections, archive hashes, or engineering connections.

**Tech Stack:** C# / .NET 8, existing domain service, xUnit, offscreen WPF and existing PDF exporter.

**Spec:** The user's approved request: include lines and text in the bounds, then scale to the configured maximum width/height. The screenshot uses sketch X=20, Y=110, width=180, height=70 with the table hidden.

## Global Constraints

- No AO, native app/window interaction, live data writes, original CAD edits, paid fallback, push or merge.
- Reuse the current linked worktree; preserve unrelated dirty files.
- Existing table/page overlap and finite-value validation remain active.
- Placement is a view, not a second physical cable or a new electrical graph.

## Review Focus

- Rotated and wrapped text at/outside legacy bounds must remain complete.
- Field substitution must occur before measuring; archive text remains unchanged.
- Circles, arcs, lines and hatch contours must scale together without clipping.
- Degenerate/invalid geometry must not create nonfinite output or partial mutation.
- Saved layouts and screen/PDF/DXF presentations must agree.

## Task 1: Fit Complete Content

**Files:**
- Modify `src/ComponentIntelligence/Electrical/Schematic/SchematicCableDetailService.Archived.cs`.
- Create `src/ComponentIntelligence/Electrical/Schematic/SchematicCableDetailService.Sketch.cs` for private presentation bounds/fitting helpers.
- Modify `tests/ComponentIntelligence.Tests/Electrical/ArchivedCableManufacturingDetailTests.cs`.
- Create `tests/ComponentIntelligence.Tests/Electrical/ManufacturingSketchFitTests.cs`.

**Interfaces:** `BuildArchived(ElectricalProject, SchematicPage, SchematicCableDetailBinding)` remains unchanged; private fitting returns copied `SchematicCadPrimitive` values.

- [x] Add source-boundary TEXT/MTEXT, substitution, imported-text, geometry, uniform scaling and immutability regressions.
- [x] Run focused xUnit tests; observe the existing overflow error before modifying production code.
- [x] Replace the legacy text-fit rejection with content bounds and uniform fitting, including a small stroke margin; reject malformed/nonfinite content.
- [x] Run focused tests, then the complete suite; retain all table overflow tests.

## Task 2: Verify and Deliver

**Files:**
- Extend `validation/CableManufacturingSmoke/Program.cs` with a synthetic sketch-fit case.
- Record evidence under `docs/superpowers/validation/2026-10-12-manufacturing-sketch-fit.md`.

- [x] Exercise layout placement, edit, SQLite reopen and offscreen WPF text/geometry ink containment at the screenshot's settings.
- [x] Generate and visually inspect a multipage PDF using the same presentation.
- [x] Request a bounded independent code review; fix material findings with regression tests.
- [x] Make a scoped local commit and build a clean BG package from its source archive, excluding unrelated changes.
- [x] Test the clean source and validate the launcher without starting the app. Verify workbook/archive hashes remain unchanged and that validation does not save or replace the live project database.

## Execution Record

- Root cause: the importer measures insertion points for text; the detail service rejected full text extents against those legacy dimensions before applying the requested scale.
- Existing BF selection-frame fitting is a separate display path and does not fix this guard.
- Antigravity was used only for an abstract advisory review without repository or company data; its attachment, nonfinite-value, preservation and export-parity checks are included above.
- Baseline suite passed 1425 tests. The new source-boundary fitting tests failed with the reported error before the production fix; focused suite passed 36 and complete suite passed 1441 after fitting.
- User added a bounded display request during execution: give every wiring-diagram connection a different identification color for easier tracing. Extend the existing shared renderer only, keeping jump arcs the same color as their edge and retaining existing selection highlighting. Sorted endpoint keys make colors identical across movement, save/reopen and PDF; colors are not insulation specifications and are not saved into CAD/engineering data.
- Color rendering smoke failed against the black-only renderer before implementing `DrawDiagram`; test editor and PDF parity, jump arcs, 7 unique colors, selection/hit surfaces, move/undo and saved data preservation.
- Independent review findings fixed: word-boundary MTEXT and narrow glyph overflow, empty TEXT invalid dimensions, large finite coordinate precision. Four domain tests and actual WPF cases went RED before their fixes. Final WPF coverage170 cases; color coverage1000 distinct colors plus the seven-edge editing workflow.
- Final working-tree tests1446 passed; clean archived source tests1443 passed, zero failures. Code commit272c39dd361e650c1c9d6d4d7ea2275dc81e15a4 produced222 published runtime files. Clean-source sketch, color/PDF, manufacturing all/interaction, wiring interaction and company-frame fit checks passed. Launcher validation resolved the existing AU project folder and configured central archive without launching a window.
