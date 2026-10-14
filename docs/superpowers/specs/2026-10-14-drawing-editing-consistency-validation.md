# BN Drawing Editing Consistency Validation

## Delivery Identity

- Code commit: `ee74918c1b67a2a066f44abab1a00d1074c5a808`.
- Product version: `BN-2026.10.14+ee74918`; file version `0.14.0.0`.
- Delivery directory: `2026-10-14_BN繪圖編輯一致性修正版` in the existing project-delivery folder.
- Source archive SHA-256: `FB71DBDB5A7394058D561D5420A0099C3142D12C0D1DA3A074FD108AFC428D9D`.
- 222 runtime files verified against the generated SHA-256 manifest.
- `launch-bn.ps1 -ValidateOnly` passed, including the existing AU project-data and selected central archive paths. `StartedSoftware=false`.

## Verification

Clean Git-archived source built and published independently of the dirty working
tree. All **1,702** clean-source domain tests passed; the working-tree suite
passed **1,705**, with the three-test difference belonging to preexisting,
unrelated drawing-planning changes. Those changes were preserved and excluded
from this release commit. No tests were skipped.

The clean-source offscreen editing, dense-wire readability, connector mating,
manufacturing, cable wiring, workspace and performance proofs all passed.
They verified actual wire names, adjacent unique specifications, Ctrl/box
selection, no hidden batch after navigation, immutable topology during fan-out,
paper-safe labels without wire occlusion, SQLite round-trip, PDF physical-cable
deduplication, ordinary-wire forward links, return links and atomic export
failure. The delivery includes 43 proof files under `03_validation`.

Local synthetic performance: single-page redraw 15.4 ms, all-pages redraw
102.4 ms, linked-block drag preview p95 0.009 ms. The 130-reservation fallback
regression improved from 5,848 ms RED to 12.757 ms independently measured.
Grids above 1,000,000 nodes fail before search-array allocation, without dropping
reserved routes or committing partial project changes. These figures do not
promise identical latency on every real drawing.

## Independent Review

Seven reported findings were reproduced or otherwise independently confirmed
and closed: hidden selections across page navigation, metric-source area
conversion, obstruction by a separately moved module, visible fan-out reorder,
manual-bend loss, unrelated endpoint touch, and costly fallback search. Final
targeted review passed 31 cases and approved the routing gate. Owned outward
lead joins remain legal but cannot retrace; unrelated endpoint blockers remain
authoritative. New fan-out presentation uses project schema `0.12`.

## Remaining Acceptance

Native mouse acceptance and the user's actual drawing remain user UAT. Extremely
crowded or physically blocked layouts still require explicit intervention; the
exporter never silently drops necessary annotations. Source-less wire sizes,
selectable operating modes and conflicting functions remain review states,
not electrical approval. Exact changed/missing/duplicate CAD tags cannot be
substituted by nearest geometry.

No original DWG, central workbook, live database, active application window,
GPU/system setting or paid provider was changed during validation. AO was not
used. The existing development branch was kept locally; no push or PR occurred.
