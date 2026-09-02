# Week 1 CP2-E2 UI Runtime Isolation Status

Task: `CODEX-W1-20260902-013`
Checkpoint: `CP2-E2-UI-RUNTIME-ISOLATION`
Worker disposition: `UI_RUNTIME_ISOLATION_VERIFIED`

## Authority

| Item | Value |
| --- | --- |
| Exact base commit | `89006b4208beafbab6520761de8e7b7365f85c92` |
| Exact base tree | `3042cdcd7891cf4a3bd27cfc5ae54e2efefa33dc` |
| Branch | `codex/cp2e2-ui-runtime-isolation-20260902` |
| Draft PR base | `codex/local-latest-cp2e1-baseline-integration-20260902` |
| Implementation commit/tree | Recorded after publication |
| Final branch head/tree | Recorded after publication evidence update |
| Draft PR | Recorded after publication |

The branch was created in a new isolated worktree from the exact authorized base. PR #17, #18, and #19 were not modified, rebased, merged, or retargeted.

## Changed files

| File | Rationale |
| --- | --- |
| `src/ComponentIntelligence/Runtime/DesktopDatabasePathResolver.cs` | Adds the approved pure, opt-in `COMPONENT_INTELLIGENCE_DB_PATH` resolver. Unset/blank preserves the prior default; a configured value must be absolute and is normalized; invalid input throws without fallback. |
| `src/ComponentIntelligence.Desktop/MainWindow.xaml.cs` | Replaces the two-line inline production-path construction with one resolver call. Existing path display and all downstream `_databasePath` usage remain unchanged. |
| `tests/ComponentIntelligence.Tests/Runtime/DesktopDatabasePathResolverTests.cs` | Covers unset, whitespace, absolute normalization, relative fail-closed, and no-production-fallback behavior. |
| `docs/handoff/WEEK1_CP2E2_UI_RUNTIME_ISOLATION_STATUS_20260902.md` | Durable task evidence. |

No schema, migration, validator, topology meaning, cable evidence, v1/v2 export, AutoCAD, or CP2-E2 Cable Assembly Editor behavior was changed.

## TDD and automated verification

RED was observed before production implementation: the focused test build failed with `CS0103` because `DesktopDatabasePathResolver` did not exist.

| Gate | Result |
| --- | --- |
| Focused resolver tests | `PASS`, 5/5 |
| Full runnable tests | `PASS`, 676/676; 0 failed; 0 skipped |
| Desktop Debug build | `PASS`, 0 errors; 22 existing obsolete/nullability warnings |
| `git diff --check` | `PASS` |

Commands:

```powershell
dotnet test tests\ComponentIntelligence.Tests\ComponentIntelligence.Tests.csproj -c Debug --nologo --no-restore --filter FullyQualifiedName~DesktopDatabasePathResolverTests
dotnet test tests\ComponentIntelligence.Tests\ComponentIntelligence.Tests.csproj -c Debug --nologo --no-restore
dotnet build src\ComponentIntelligence.Desktop\ComponentIntelligence.Desktop.csproj -c Debug --nologo
git diff --check
```

The first Desktop command attempted with `--no-restore` reported `NETSDK1004` because the new worktree did not yet have that project's `project.assets.json`. A normal restore/build was then run and compiled successfully. This setup miss is not represented as a PASS.

## Safe Windows UI smoke

### Preflight

- Existing Component Intelligence process count: `0`; no Product Owner process was disrupted.
- Production SQLite path: `C:\Users\jackp\AppData\Local\ComponentIntelligence\component-intelligence.db`.
- Production size before: `47,542,272` bytes.
- Production SHA-256 before: `4B1218C982297B080E31C6E985AB1919F8A7986E4BF61C38969E7830D3338E41`.
- Disposable path used: `C:\Users\jackp\Documents\Component-Intelligence-UI-Smoke-CODEX-W1-20260902-013\component-intelligence-ui-smoke.db`.
- Disposable initial size/SHA-256 exactly matched production before candidate launch.

### Visible path gate

Only the candidate `ComponentIntelligence.Desktop.exe` was launched, with `COMPONENT_INTELLIGENCE_DB_PATH` set in that child process. Before entering Topology/Layout, the existing main-window SQLite path display visibly showed:

`C:\Users\jackp\Documents\Component-Intelligence-UI-Smoke-CODEX-W1-20260902-013\component-intelligence-ui-smoke.db`

The visible value was not the production path. This gate passed before any Electrical/Topology interaction.

### Topology observations

- Main Desktop window opened without crash.
- Electrical Design opened against the disposable copy.
- Known project `New Electrical Project` / `2fe3fb260c7d4c0eb3f5661e4b112a01` loaded from the disposable database.
- The current integrated Topology surface was present: Select, Wire, Connector Cable, Auto Layout, route filters, PDF, v1 AutoCAD Review, and separate pure v2 export controls.
- The loaded project exposed more than 900 topology accessibility children; colored routes, terminal/connector endpoints, labels, and component content were visibly readable after bounded in-memory Auto Layout.
- The Connector Cable tooltip explicitly stated that the old direct custom-harness entry is disabled pending the new Cable Assembly Editor. No `CreateCustomCableAssembly` or replacement-editor action was exercised.
- No Save command was invoked.

### Layout/Cabinet observations

- The Layout/Cabinet surface opened successfully.
- Existing cabinet `CAB-01`, mounted components, product-image-backed items, terminal groups, selection/group-movement guidance, terminal-section controls, and FIT issues were present and readable.
- The smoke did not repair engineering data or apply Layout edits.

### Close and postflight

- Electrical Design and the main candidate window both closed normally.
- Candidate process count after close: `0`.
- Production size after: `47,542,272` bytes.
- Production last-write UTC after: `2026-09-01T03:26:21.2822653Z`.
- Production SHA-256 after: `4B1218C982297B080E31C6E985AB1919F8A7986E4BF61C38969E7830D3338E41`.
- Production pre/post size and SHA-256: byte-identical.
- Disposable final size: `47,542,272` bytes.
- Disposable final SHA-256: `ACE459D64F84E0CC42D049B89F3F2DAD01191AC0956A451F79DEC7790CB8A387`; the disposable copy changed during runtime, which is allowed and proves writes remained isolated.
- The disposable production-data copy and its now-empty evidence directory were removed after hashes were recorded. Only non-sensitive hash/path/UI observations remain in this document.

## Protected state

- Protected task-011 checkout remains at `de960f6a31d57650c8a614e42e0551f356e76343` with its original 50 dirty paths.
- PR #17 worktree remains at `d4194c0ab856ef45a4f24e07a85486197b34fa30`, clean.
- PR #18 worktree remains at `599b4f679a9b83ce713083e5ed2f4929f3914018`, clean.
- PR #19 worktree remains at `89006b4208beafbab6520761de8e7b7365f85c92`, clean.
- Production workbook, WDP, DWG, DWT, formal AutoCAD Electrical libraries, and production SQLite were not modified.
- AutoCAD was not launched or controlled by this task.

## Stop condition

Automated verification and the mandatory disposable-database Windows UI smoke both passed, including the byte-identical production SQLite postflight check. The evidence-backed worker disposition is `UI_RUNTIME_ISOLATION_VERIFIED`.

Worker completion is not PM acceptance.
