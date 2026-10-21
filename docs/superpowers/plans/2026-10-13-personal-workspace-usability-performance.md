# Personal Workspace Implementation Plan

> Execute inline using executing-plans and TDD. The user approved the written scope and explicitly requested implementation; do not request another execution approval. No AO.

Baseline: `37ce456` (BK runtime plus approved specification). Preserve the seven unrelated dirty files listed in the task handoff. All persistence validation uses temporary databases/preferences, never the live application or workbook.

## Tasks

1. **Session and recovery contracts**: add tests for snapshot isolation, saved/dirty identity, stale session completion, corrupt preferences, and automatic-only retention. Implement small core helpers; capture repository/revision payloads before the first await. Append AutoRecovery without renumbering existing triggers. Atomic insert/prune retains 20 automatic checkpoints per project, all other revisions intact.
2. **Project lifecycle**: integrate one save/switch gate, exact saved snapshot, persistent state/path display, name staging, Save/Discard/Cancel on new/load/close. Complete mutations schedule a coalesced state check, not pointer previews. Two-minute idle recovery skips active edits, offers explicit restore, never writes the formal project row. Guard async imports against stale sessions.
3. **Inspector transactions**: introduce a stable selected-target draft, finite/range validation and one Apply/undo boundary. Module fields use existing transform/resize/scale/reference/lock authorities. Wire designation and sizing reuse existing sizing authority; expose exact endpoint provenance and existing binding editors. Table/sketch layout uses existing service, representation-only deletion. Selection changes require Apply/Discard/Cancel only for a changed draft; no-op viewing never commits.
4. **Workspace layout/preferences**: compact project/command strips, secondary command menus retaining shortcuts, collapsible/resizable left panel, bounded scrollable inspector and default-layout reset. Persist safe local preferences and successful dialog folders only; existing database/workbook locations remain unchanged.
5. **Retained drag visuals**: retain symbol translation transforms and incident-wire paths for the gesture, resolve brushes/hide labels once. Update geometry only; preserve final pointer flush and cancel/capture cleanup. Measure against BK baseline before claiming improvement.
6. **Offscreen validation**: add a WPF workspace smoke fixture for modules/wires/details, drafts/selection, finite-input rejection, single undo, collapsed panels, preferences and viewport sizes. Simulate 100/125/150% client-space constraints without changing OS DPI. Record repeated timing samples, allocations/memory and rendering tier. Run existing compact-wire, cable-editor and PDF regression fixtures; inspect screenshots/PDF.
7. **Independent review**: read-only Codex review of task diff with emphasis on save races, target identity, retention and input focus. Verify findings and add focused regressions before finishing.
8. **Delivery**: scoped commit, clean source archive/build/test/publish, new BL sibling bundle and launcher hash/path validation only. Preserve BK and formal data. Include measured results and known native-user acceptance limits in concise Chinese notes.

## Validation Commands

- `dotnet test tests/ComponentIntelligence.Tests/ComponentIntelligence.Tests.csproj -c Release -p:WarningLevel=0`
- `dotnet build src/ComponentIntelligence.Desktop/ComponentIntelligence.Desktop.csproj -c Release -p:WarningLevel=0`
- CableManufacturingSmoke: workspace, performance/verify, wire-readability.
- CableWiringEditorSmoke: PDF mode.
- Repeat production checks in the clean archive before hashing runtime/source and validating the launcher.

## Evidence and Progress

- BK working-tree baseline: 1484 tests passed (clean BK: 1481; three tests belong to unrelated changes).
- Baseline offscreen render: single page 17.0 ms, all pages 102.1 ms; hidden-page nodes 0, zoom tree retained. These are processing timings, not native FPS.
- Read-only repository mapping identified save snapshot/race issues and per-pointer visual allocation; generic Antigravity advisory confirmed stale-target, snapshot, retention and capture-cleanup regression cases. No company files delegated.
- Tasks 1-6 implemented. Working tree: 1525 tests passed, including 41 new tests beyond BK; three pre-existing unrelated tests remain excluded from the scoped archive.
- Offscreen workspace verification covers no-op viewing, single undo, drafts/target identity, CAD exact-scale preservation, wire sizing authority, table representation isolation, panel constraints/preferences, concurrent saves, two-minute recovery, stale initial BOM retry and accepted-close import invalidation.
- Independent review findings were reproduced and fixed: input-focus conflicts, precision/no-op edits, snapshot normalization, save/load races, queued-import shutdown and redraw during an unchanged-pointer drag release. The held-BOM-retry/active-gesture overwrite was reproduced red, then verified with actual Sheet_Up, retry and final inventory/position. Final read-only follow-up resolved all reported findings; reviewer closed.
- Comparable dense synthetic run: 62 symbols, 5 incident wires, 36000 CAD primitives, 1000 warmed pointer updates. BK p95 0.5818 ms / 295003096 allocated bytes; BL p95 0.0312 ms / 4816640 bytes. Final pointer calculation 428.8 ms versus 149.4 ms. Full dense redraw remains expensive (roughly 339 ms median); these are processing timings, not native FPS or total mouse-up latency.
- Machine observation: Intel Core Ultra 7 255HX, 20 cores, 33736265728 physical-memory bytes, WPF rendering tier 2. No system, GPU, billing or live-data settings changed.
- Task 8: fresh scoped archive/build/test/publish and hash-validated BL sibling delivery in progress.
- Repeat working-tree run: 1525 tests and Desktop build passed; workspace, performance, wire-readability and cable-editor/PDF regressions passed. Repeated dense BL benchmark p95 0.0158 ms, 4816640 preview allocated bytes, final pointer calculation 79.7 ms; dense redraw remains roughly 287 ms median. No native input-to-present claim.
- Task 8 complete: production commit 849ebc07494fe97bef11e9e4e9f768b1da61a094 archived to the new 2026-10-13_BL sibling bundle. Fresh archive: 1522 tests passed, clean publish and all four WPF/PDF smoke modes passed, screenshots and Poppler PDF renders inspected.
- Final clean-source same-harness benchmark: dense BK p95 0.5236 ms versus BL 0.0150 ms; final calculation 446.0 ms versus 96.2 ms; preview allocation down 98.4%, identical 255 visible nodes. Dense complete redraw still 261.7 ms median / 403.1 ms worst in BL; selection/save have additional state/snapshot cost. Full metrics and limitations are in bundle 03_validation.
- Delivery identity validated: all 222 runtime files and source archive hashes, primary assemblies match the clean build, and launcher ValidateOnly checks existing data/central-archive paths with StartedSoftware=false. The seven unrelated paths remain dirty and untouched. This final documentation receipt is separate from the frozen production archive.
- No remaining required implementation work in this approved first round. User acceptance of actual native dragging/wiring remains pending and is not represented by the offscreen tests.
