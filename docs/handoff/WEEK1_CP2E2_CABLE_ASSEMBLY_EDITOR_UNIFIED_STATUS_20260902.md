# Week 1 CP2-E2 Cable Assembly Editor - Unified Baseline Status

Date: 2026-09-02

Task: `CODEX-W1-20260902-014`
Checkpoint: `CP2-E2-UNIFIED-BASELINE-IMPLEMENTATION-RESUME`
Worker disposition: `COMPLETE`
PM acceptance: `NOT REQUESTED / NOT IMPLIED`

## 1. Authoritative lineage

Repository: `jackp803/Component-Intelligence-Public`

Branch: `codex/cp2e2-cable-assembly-editor-unified-20260902`

Authorized base: `e7ee6c3a45c6757f00e9421c241bc0176743c4c1`

Published implementation commit: `12465c4ca45787cede3a97aad3dafecf0d99e128`

Continuation input head: `25193030fbe51ab4efad585c0178e52e43cbcf6c`

The authorized base and implementation commit remain ancestors of the continuation branch. The existing worktree, branch, and Draft PR #21 were reused; no replacement branch or PR was created.

## 2. Runtime isolation and protected state

Candidate executable:

```text
C:\Users\jackp\Documents\_codex_worktrees\Component-Intelligence-Public\cp2e2-cable-assembly-editor-unified-20260902\src\ComponentIntelligence.Desktop\bin\Release\net8.0-windows\ComponentIntelligence.Desktop.exe
SHA-256: 9C20EC89E4BC0C21971D5668E7CC76841C78C9785382BE03442D4423209F77D1
```

Disposable database:

```text
C:\Users\jackp\Documents\Component-Intelligence-CP2E2-UI-Smoke-CODEX-W1-20260902-014\component-intelligence-disposable.db
```

Before entering Topology/Layout, the application visibly displayed that exact disposable path. All UI mutations were confined to the disposable database. AutoCAD and `accoreconsole` remained stopped throughout.

Production SQLite before and after the complete UI smoke:

```text
path: C:\Users\jackp\AppData\Local\ComponentIntelligence\component-intelligence.db
size: 47,542,272 bytes
sha256: 4B1218C982297B080E31C6E985AB1919F8A7986E4BF61C38969E7830D3338E41
pre/post size match: true
pre/post SHA-256 match: true
```

Final process state: Component Intelligence `0`; AutoCAD/accoreconsole `0`.

## 3. Real Windows UI smoke

The mandatory continuation was completed against project `cp2e2-ui-smoke-014` in the disposable database.

PASS evidence:

- selected the second explicit Cable route and opened the real `建立複合線 / Cable Assembly` flow with two routes;
- changed construction explicitly through Unknown, Purchased, and Custom; final saved authority is Custom;
- assigned Branch 1 and Branch 3, added a third Cable, and observed the next suggestion as Branch 4 rather than reusing gap 2;
- saved, reloaded, double-clicked a member route, and reopened the same persisted assembly;
- non-contiguous Branch 1/3/4 persisted through SQLite Save/reload;
- changed `SMK-CBL-3` length to `1.75 m`; reload preserved `User` provenance;
- untouched `SMK-CBL-1` remained `1.25 m / Imported` and `SMK-CBL-2` remained `0.9 m / Mechanical`;
- Cancel after temporary construction and length edits left persisted Custom / 1.75 m User data unchanged;
- removing `SMK-CBL-1` and saving changed only membership: all 3 CableInstances, 4 connections, and 4 topology routes remained;
- adding the same CableInstance back restored Branch 4 and retained its stable identity, connection, geometry, length, and Imported provenance;
- the structural-error Save control was disabled; an attempted click kept the editor open and SQLite still had zero assemblies before the valid Save;
- Layout/Cabinet opened in the real candidate UI and rendered the expected empty-cabinet review state without mutating production data;
- final saved project contained no generated member sub-tags;
- candidate Electrical Workspace and root application closed normally.

Previously published continuation evidence remains valid and was not repeated blindly:

- ordinary `smoke-wire-ordinary` opened in the Inline Connection Editor as `OrdinaryWire`;
- Cancel preserved `kind=Wire`, `cableInstanceId=null`, and no Cable authority;
- a non-assembly explicit Cable still opened the Inline Connection Editor.

## 4. Chinese-first fail-closed rule evidence

Normal editor actions exercised:

- `RULE-CABLE-ASSEMBLY-001`: two Trunk members -> `錯誤：只能有一條主幹，請重新指定。`
- `RULE-CABLE-ASSEMBLY-002`: duplicate Branch 3 -> `錯誤：分支編號重複，請使用其他分支編號。`
- `RULE-CABLE-ASSEMBLY-003`: Branch 0 -> `錯誤：分支編號必須是大於 0 的整數。`
- `RULE-CABLE-ASSEMBLY-005`: Other with blank name -> `錯誤：「其他」角色需要填寫名稱。`

The remaining structurally unreachable states were exercised with isolated rows added only to the disposable database after first copying it to `component-intelligence-disposable-before-rule-fixtures.db`:

- `RULE-CABLE-ASSEMBLY-004`: Trunk carrying an index -> editor blocked Save with `錯誤：主幹不能帶有分支編號。`
- `RULE-CABLE-ASSEMBLY-007`: the same CableInstance listed twice -> editor blocked Save with `錯誤：同一線段不能在同一複合線中重複加入。`
- `RULE-CABLE-ASSEMBLY-006`: an assembly member referenced missing stable ID `missing-cable-fixture`; member double-click failed closed in the UI with `無法開啟複合線` and `找不到 CableInstance 'missing-cable-fixture'。`

No invalid fixture was saved through the product UI, and no invalid data reached production SQLite.

## 5. Persisted valid state

Readback of `cp2e2-ui-smoke-014` after the final valid Save:

```text
CableAssembly construction: Custom
members:
  smoke-cable-2: Branch 1
  smoke-cable-3: Branch 3
  smoke-cable-1: Branch 4
cables: 3
connections: 4
topologyRoutes: 4
lengths/provenance:
  smoke-cable-1: 1250 mm / Imported
  smoke-cable-2: 900 mm / Mechanical
  smoke-cable-3: 1750 mm / User
generated member sub-tags: 0
```

## 6. Fresh final verification

Focused CP2-E2 gate:

```text
dotnet test tests/ComponentIntelligence.Tests/ComponentIntelligence.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~CableAssembly|FullyQualifiedName~TopologyInteraction"
PASS: 65 passed, 0 failed, 0 skipped
```

Relevant Electrical regression:

```text
dotnet test tests/ComponentIntelligence.Tests/ComponentIntelligence.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~Electrical"
PASS: 517 passed, 0 failed, 0 skipped
```

Full runnable tests:

```text
dotnet test tests/ComponentIntelligence.Tests/ComponentIntelligence.Tests.csproj -c Release --no-build
PASS: 706 passed, 0 failed, 0 skipped
```

Desktop Release build:

```text
dotnet build src/ComponentIntelligence.Desktop/ComponentIntelligence.Desktop.csproj -c Release --no-restore
PASS: 0 errors, 22 existing warnings
```

`git diff --check`: PASS.

## 7. Scope and disposition

No AutoCAD execution, WDP/DWG/DWT/library mutation, production SQLite mutation, workbook mutation, or cloud write occurred. No Product Owner drawing policy was inferred.

Disposition: `COMPLETE`

Worker completion is not PM acceptance. Draft PR #21 remains `OPEN / DRAFT / UNMERGED` for PM/Product Owner review.
