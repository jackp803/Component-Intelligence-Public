# BI Resize And Interaction Verification

Date: 2026-10-12. Delivery code commit: b48055c74fa970a473b4386b1cda257dcec24bd1.
Runtime: framework-dependent .NET 8 Windows desktop build, 222 runtime files.
Source archive contains only committed work; seven unrelated DrawingPlanning files remain untouched and excluded.

## Resize

- Four original regression failures reproduced: unused maximum height, table use of unused space, empty text origins, and full-circle arc envelopes.
- Six new domain cases pass, including true overflow with unchanged input and extreme finite invisible text.
- Effective fitted content guards sheet edges and table overlap. Requested maximum dimensions remain persisted; original CAD and electrical mapping are not rewritten.
- Offscreen WPF corner resize passes with a 300 mm unused maximum height and visible 189 mm enlarged width near the right sheet edge.
- Tight frame, no-jump grab, move, one commit, undo/redo, SQLite reopen and independent table deletion pass.
- 170 actual WPF TEXT/MTEXT ink cases pass. Four existing archived sketches and frames checked via readonly SQLite.

## Interaction And Performance

Fixture: eight synthetic pages, 600 primitives per manufacturing sketch, five repeated render calls. This measures synchronous render work, not native-window frame rate or end-to-end mouse latency.

| Measure | Before | Final clean build |
| --- | ---: | ---: |
| Single-page render average | 185.1 ms | 28.9 ms |
| Explicit all-pages full redraw average | 137.4 ms | 183.1 ms |
| Initially hidden page top-level nodes built | 140 | 0 |
| Zoom preserves CAD/page instances | No | Yes |

Single-page render work is about 84% lower in this fixture. Full all-pages redraw remains intentionally comprehensive; timings vary with JIT/GC and no all-pages full-redraw speedup is claimed. Gesture previews repaint the active page; release refreshes visible pages.

- Hidden-page activation and all-pages toggle retain content.
- Burst preview, final-pointer flush, release once, cancellation, lost capture, page switch and draining aborted callbacks pass.
- Independent review identified Render-priority starvation. Reproduced 100 queued Input events causing 100 callbacks; Background priority now produces one callback with the latest route.
- Independent review identified stale zoom weights. RED/GREEN tests cover selected/unselected wire bodies, crossing jump arcs and DC-negative outlines without rebuilding CAD.
- Follow-up identified output rendering clearing interactive stroke targets. RED/GREEN temporary-output-before-zoom regression passes with interactive-only cache reset.
- Reviewer confirmed resolution with no new substantive finding in the bounded follow-up.
- Gesture-start bounds are transient and cleared on begin/cancel; caches do not persist or change engineering authority.
- Generic Port/Pin command branches and click threshold are retained; native-window feel and mouse-device acceptance remain with the user.

## Validation And Delivery

- Working tree full suite: 1452 passed, 0 failed, 0 skipped.
- Clean committed source full suite: 1449 passed, 0 failed, 0 skipped.
- Clean WPF review-input, review-zoom, performance, frames, fit-sketch and manufacturing interaction proofs pass.
- Manufacturing PDF: four pages, atomic failure, forward/return wiring links, no project mutation.
- Wiring color regression: 1000 distinct identifiers, seven-edge workflow, same-color jump arcs, dedup and PDF links pass.
- Synthetic fitted PDF rendered with Poppler and visually inspected for complete geometry and text.
- Antigravity received only an abstract no-files WPF advisory; no company files, secrets, billing or paid fallback.
- Native user windows were neither opened nor closed. Source CAD/workbooks/archive were not modified by task tools.
- Live project reads used readonly SQLite; no claim that its hash stayed unchanged while the user works.
- Launcher validation checks all 222 runtime hashes and existing data/selected central paths without starting software.
- Existing AU data directory is reused. No CAD/BOM reimport required.

