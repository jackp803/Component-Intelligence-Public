# Manufacturing Sketch Tight Selection Frame

## Scope

The user's selected manufacturing sketch had large blank space inside its edit frame. `RenderDetailHandles` used the saved maximum width/height, not actual rendered content. The fix is bounded to the existing desktop presentation/gesture path; no schema, source CAD, fitting scale, electrical data, PDF or DXF change.

- Build a sketch-only view with the table hidden, then reuse the existing `CanvasInkBounds` WPF measurement including transformed text and curved paths.
- Fit the transparent selection surface to that ink plus 1 mm each side. Keep the edit button and resize corner with this frame.
- Resize from pointer delta and visible frame dimensions, scaling both saved maximum dimensions equally. Zero pointer delta leaves the layout exactly unchanged.
- Keep table bounds, move/delete, undo/redo, saved bindings and the original fitting constraints intact. An empty drawing falls back to the configured frame.
- No AO, native-window operation, live-data writes or source/archive edits. The unrelated dirty DrawingPlanning work is excluded from the release.

## Tests

- `SketchFrameSmoke` first failed with frame540x210px around actual ink318.467x35.034px. After the fix it checks exact 3px-per-side margins (1mm), rotated/empty text, independent table, unchanged layout on no-op resize, 1.25x resize, one commit per gesture, move, undo/redo, SQLite reopen and deletion of sketch only.
- Four saved manufacturing sketches from readonly SQLite passed the same tight-frame assertions. Their selected-frame PNGs were rendered on white and visually inspected; no saved source was mutated.
- Full working-tree xUnit suite:1446 passed,0 failed. Previous170-case actual WPF text-ink fit regression passed, as did the manufacturing interaction workflow.
- Seven distinct wiring colors, same-color jump arcs,1000-color stress test, PDF forward/return links, physical deduplication, page ordering and failed-export atomicity passed unchanged.

## Delivery

BH bundle: `2026-10-12_BH示意圖緊密外框版` in the existing project collection. Package is built from the scoped clean Git archive at `540765a4a907ffce24b76b444f7e609927d22d08`; actual native-window acceptance remains with the user.

- Clean-source full xUnit:1443 passed,0 failed. Desktop publish succeeded;222 runtime files are recorded with SHA256 hashes.
- The clean-source frame smoke passed synthetic layout/gesture/save tests and all four readonly existing sketches.
- Independent local Codex review of the two production diffs, helper and smoke tests found no material findings. It accessed no company data and performed no edits or native-window actions. Antigravity was skipped for this short bounded task; no external service received company material.
- `launch-bh.ps1 -ValidateOnly` verified222 runtime files and resolved the original AU data folder and selected central archive, without launching software. Source archive hash was checked against the manifest.
- Original CAD, workbooks and archives were not edited; the live SQLite database was opened in readonly mode only and never saved/replaced. Its hash is not asserted unchanged while the user may be working.
