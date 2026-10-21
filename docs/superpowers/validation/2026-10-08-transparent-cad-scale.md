# Transparent CAD and Display Scale Validation

## Build

- Code: c67fb689ed9bf90fa54afcea933fed1c90209583.
- Delivery: BD / 2026-10-08, `2026-10-08_BD透明CAD縮放版`.
- Archive-based Release build; no unrelated working-tree changes included. No push or merge.
- Project display scale is derived from stored representation dimensions, not a rewrite of native CAD geometry. Scaled projects use 0.10; archive format remains v8.

## Evidence

- Initial RED: unscaled display projection and missing scale command; real WPF bitmap showed upper CAD empty area hiding lower lines.
- Independent review found stationary locked-route rerouting, inverse Port drag coordinates, delayed active/history schema promotion, and one-DIP tiny-text clamp. All reproduced with failing regressions, then fixed. Follow-up read-only review: no findings.
- CAD domain suite: 24 passed, covering four rotations, Port and Pin attachments, native geometry/bindings/hashes/approval preservation, arcs/circles/fill/text, absolute resizing, invalid input, locked endpoints/routes, branch movement, per-representation sizes, rebinding, empty-Pin Port placements, SQLite, checkpoints and reset without reopen.
- Full working-tree suite: 1425 passed. Clean archived source suite: 1422 passed; three unrelated in-progress tests are not in the build.
- Clean CAD WPF smoke: visible lower lines through empty upper CAD, pointer hit through empty regions, scale panel and resize handle, apply/reset, invalid input, undo/redo, preview/commit, moving Port, exact 5% text size, PDF without selection controls. All offscreen; no Window.Show.
- Clean wiring appendix fit/PDF smoke: single-page fit, larger measured labels, 8/10/6 Pin inventory, jump arcs, deduplication and forward/return PDF annotations passed.
- Clean manufacturing WPF/SQLite/PDF smoke passed. Working-tree wiring editor, connector editor, manufacturing interaction and fit regressions also passed.
- CAD PDF and fitted appendix rendered with Poppler and visually inspected.
- Release publish succeeded. BD launcher ValidateOnly verified 222 runtime file hashes and existing project/central archive paths; StartedSoftware=false.
- Existing project DB, local workbook/archive and selected central workbook/archive SHA256 unchanged before/after packaging. No source CAD or live archive writes.

## Remaining Acceptance

- User opens BD and verifies their actual project. Native windows were not automated.
- Empty CAD regions are transparent; actual CAD solid hatch fills remain visible. This is not raster/photo background removal.
- Imported unit conversion remains explicit. 100% means the pinned imported CAD size, not a guessed physical measurement.
- After saving a scaled project, use BD or later; older versions deliberately reject 0.10.
