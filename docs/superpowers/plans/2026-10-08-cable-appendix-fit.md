# Cable Appendix Fit Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Keep moderately oversized cable wiring diagrams on one PDF appendix page with larger, readable Pin labels.

**Architecture:** Fit only the exported presentation, never the stored wiring geometry or electrical graph. Measure the same WPF text used to render, enlarge fitted Pin labels to at least 3.2 mm high, and scale geometry only when the complete diagram fits without overlapping connector labels. Preserve readable tiled output for genuinely large diagrams; retain original type if a saved narrow connector cannot accommodate enlargement.

**Tech Stack:** C#, WPF, PDFsharp, offscreen smoke executable, xUnit.

**Spec:** User's 2026-10-08 screenshots: a three-connector diagram spills only the bottom of P2 onto a mostly empty second appendix page. Shrink modules or overall geometry and enlarge text. Existing standing approval allows direct implementation and testing without repeated approval.

## Global Constraints
- No AO, native-window operation, central-workbook changes, or live project-data writes.
- Preserve all seven unrelated working-tree changes.
- Keep explicit mappings, Pin IDs, saved layouts, confirmation, jump arcs, and PDF navigation unchanged.
- Respect heading, footer, and company title-block reserves.
- Output geometry scale must not fall below 0.75; larger drawings remain tiled at 1:1.

## Review Focus
- Long labels must not be clipped or overlap neighboring labels after font enlargement.
- Company templates leave less vertical space than blank sheets.
- Nonconsecutive Pin numbers must survive fitting.
- Large inventories must retain every Pin and remain readable.
- Reducing appendix page count must keep forward and return links correct.

## Task 1: Reproduce, Fit, and Render
**Files:** `src/ComponentIntelligence.Desktop/CableWiringAppendixRenderer.cs`, `src/ComponentIntelligence.Desktop/CableWiringVisualRenderer.cs`, `validation/CableWiringEditorSmoke/Program.cs`.
- [x] Add an offscreen appendix-layout test using synthetic RJ45 8P / M12 10P / M12 6P (1,2,3,4,6,7), company frame, branch, and jump arcs. Require one tile, complete label/frame coverage, readable text, and no source mutation. Run to observe the current overflow failure.
- [x] Add presentation-only scale and minimum text-height options to the appendix tile/visual renderer. Measure rendered labels to choose a complete single-page view when safe; otherwise preserve 1:1 tiling.
- [x] Exercise actual rendered text sizes and bounds, scaled geometry, and non-overlapping labels; exercise large-inventory fallback and long-function labels.
- [x] Generate synthetic linked PDFs with company frame. Confirm reduced page count, exact link destinations, no missing Pins, jump symbols, and no mutation. Render and inspect PNGs.

## Task 2: Verify and Deliver
- [x] Run full domain suite and relevant WPF/editor/manufacturing/connector smoke tests.
- [x] Obtain a bounded fresh review of the changed files, address verified findings, and repeat affected tests.
- [ ] Commit only scoped changes locally; package a clean-source BC runtime, source archive, checksums, and launcher using the existing data path. Do not push or alter existing runtime/data.
- [ ] Validate launcher without starting the app and provide the new launcher to the user.

## Execution Ledger
- Root cause: appendix planner tiles at fixed 1:1 even for small overflow; renderer does not independently preserve/enlarge font size when fitting.
- Delegation: this is a localized rendering change; skip Antigravity bulk delegation and keep company assets private. Use only synthetic fixtures for validation.
- RED: the original planner failed the new one-page assertion for the synthetic company-frame fixture. GREEN: one page at 87% geometry; all 24 Pins present, actual rendered labels at least 3.2 mm, no label clipping/overlap, and jump arcs retained.
- Working-tree domain suite: 1401 passed (includes three unrelated pre-existing tests). WPF wiring, manufacturing all/interaction, and connector all passed. Pypdf confirmed exact destinations for base, relocated, and inline linked PDFs. Poppler-rendered appendix inspected.
- Independent review identified two verified issues. Both reproduced RED: enlarging text in a saved 60 mm frame can cross a Pin; an 8 mm tile overlap can clip the enlarged label on every page. GREEN: unsafe enlargement falls back to original type, and tile overlap now covers measured text width/height.
- Ruling: original readable type is retained for narrow saved layouts that cannot safely accommodate 3.2 mm labels. No source-frame resize or electrical-route change is needed. A label longer than a whole printable viewport is rejected explicitly instead of silently clipped; use a shorter label or larger paper. This preserves source geometry and avoids introducing layout damage merely for font enlargement.
- Clean-source suite before review fixes: 1398 passed; runtime published from baseline plus four scoped files only. Republish and repeat clean-source checks after review fixes.
