# CP3-A Component / Block Archive Foundation — Source-First Status

Date: 2026-09-03
Task: `E5-20260902-020`
Worker: `E5`
Disposition: `SOURCE_IMPLEMENTATION_DONE_PENDING_CODEX_VERIFICATION`

## 1. Authority and exact lineage

Repository: `jackp803/Component-Intelligence-Public`

Authorized exact base:

```text
SHA  = 69ed47c6539cb2ececd0f93c1c8d2f0aa19533cf
tree = 952ed00f881284d6dbe2011d58576f6f6da36b61
```

Branch:

`agent/e5-cp3a-component-block-archive-foundation-20260902`

Final code/test **source candidate** before this documentation-only handoff commit:

```text
head = 5906ddd38b9583d2591c4c6bcc2c58b793165062
tree = b80264ef1f72ce67e0f50ddb067623b3058aee35
```

Exact base -> source candidate compare:

```text
status     = ahead
ahead_by   = 10
behind_by  = 0
merge_base = 69ed47c6539cb2ececd0f93c1c8d2f0aa19533cf
changed_files = 29
```

Draft PR:

`https://github.com/jackp803/Component-Intelligence-Public/pull/22`

PR base is the accepted CP2-E2 lineage `codex/cp2e2-cable-assembly-editor-unified-20260902`, whose head is exactly the authorized CP3-A base `69ed47c...`. PR #21 was not modified.

## 2. Source commit sequence

1. `00a77ef73b28aa1461d1548669cd4c682b69ea2d` — `feat(cp3a): add strict symbol archive v1 repository`
2. `cd593565f9b9e1e682cc1967f8232befa381381a` — `feat(cp3a): add read-only block folder scanner`
3. `e20a60dcfee1e6678f32ca7d8d40ccf25e5ea749` — `feat(cp3a): rank component matches without auto approval`
4. `41551fda2bafc19b390255207321ef7d1ada03dc` — `feat(cp3a): approve immutable symbol revisions atomically`
5. `b430ea036accb441b4f2a4a2595f4173a33b4bb4` — `feat(cp3a): resolve approved or generic symbols reproducibly`
6. `6db68c69723e6cc3b65d6a11133a48c57627adee` — `feat(cp3a): inspect copied cad blocks with accoreconsole`
7. `1193299368e8c085373e26fa69779f89aad01aad` — `test(cp3a): define block archive batch review behavior`
8. `c935f36265683f94443dd47c442d8fbeb8c83296` — `feat(cp3a): add block archive batch review ui`
9. `e0ac6538b28e41fc547d75acaa4824e0e0dd2a4c` — `test(cp3a): cover end to end symbol archive flow`
10. `5906ddd38b9583d2591c4c6bcc2c58b793165062` — `fix(cp3a): harden archive path and batch preflight source`

The later handoff commit is documentation-only and is not part of the code/test source candidate identity above.

## 3. Changed source surface

Exact compare reports 29 changed files. CP3-A changes are bounded to:

- `src/ComponentIntelligence/SymbolArchive/*`
- `src/ComponentIntelligence/Electrical/Export/SymbolExportManifest.cs`
- `src/ComponentIntelligence/Cache/HashService.cs`
- CP3-A Desktop deep-inspector / batch-review WPF files and the bounded MainWindow entry
- Desktop/test project linkage required by those files
- CP3-A focused/integration test source

No Page Planner, placement, routing, full Drawing IR page writer, formal WDP/DWG/DWT library integration, or unrelated older primitive-rendering policy was added.

## 4. Implemented source behavior by plan task

### Task 1 — Symbol Archive v1

Implemented:

- exact v1 roles: `Schematic`, `ConnectorDetail`, `PanelFootprint`, `TopologyVisual`;
- exact source types: `ApprovedCustom`, `Manufacturer`, `LibraryStandard`, `GeneratedGeneric`;
- revision states: `Candidate`, `Approved`, `Superseded`, `Rejected`;
- versioned `SymbolArchive.json` sidecar (`ci-symbol-archive.v1`);
- deterministic string-enum serialization;
- strict fail-closed load/validation;
- archive-relative path enforcement including OS-independent Windows drive-root rejection, UNC/root and `..` escape rejection;
- canonical lowercase 64-hex SHA-256;
- duplicate revision / duplicate endpoint / multiple-Approved rejection;
- temp + flush + move persistence.

The central workbook remains a read-only authority input. No `WorkbookComponentKnowledgeStore.UpsertAsync()` path was added.

### Task 2 — recursive read-only scanner and exact duplicate classification

Implemented:

- streaming `HashService.Sha256FileAsync`;
- recursive `.dwg` / `.dxf` scan, case-insensitive;
- deterministic normalized relative-path ordering;
- nested reparse-point skip;
- basic file metadata and SHA-256 without AutoCAD;
- archive SHA index producing duplicate hints only;
- no ComponentId, SymbolRole, SourceType, or approval mutation during scan.

### Task 3 — candidate matcher

Implemented deterministic ranked suggestions using the approved weak-signal inputs while reusing existing Manufacturer/Model normalizers. Ranking output is structurally separate from user-selected authority. Equal scores remain multiple candidates; Role is never inferred by the matcher.

### Task 4 — immutable explicit approval

Implemented:

- required `UserConfirmed=true` before archive write;
- file import rejects `GeneratedGeneric`;
- ComponentId must exist in the supplied central component catalog;
- Port/Pin mapping accepts only explicit stable PortID or nonblank PinID; PinNumber is never synthesized as PinID;
- DWG/DXF-only file approval;
- deterministic `Documents/<Manufacturer>/<Model>/autocad/<role>/rev-NNN/symbol.*` storage;
- source SHA before/after and copy SHA equality;
- temp-copy then move;
- same hash / same Component+Role exact-duplicate reuse;
- changed content creates next immutable revision;
- new/re-approved revision supersedes exactly the prior Approved revision while retaining historical assets;
- sidecar-save failure removes only the uncommitted newly copied asset/revision directory.

### Task 5 — GeneratedGeneric / resolver / export pinning

`GeneratedGeneric` is implemented only under the explicit Product Owner CP3-A exception.

Generic endpoint rules are evidence-only:

- explicit `TopologyEndpointMode == Pins` -> only nonblank stable PinID values;
- otherwise stable PortID;
- no M12/RJ45/PinCount/name-based Pin invention;
- no direction/topology inference from geometry or placement;
- deterministic component-level canonical descriptor/hash;
- virtual `generated://<ComponentId>/<SymbolRole>` identity.

Resolver behavior:

- corrupt/contradictory archive authority fails closed before generic fallback;
- Approved file revision is verified against on-disk SHA before use;
- no formal Approved asset + permitted fallback -> explicit `GeneratedGeneric` resolution;
- concrete export manifest pins ComponentId, role, SourceType, exact revision/path/SHA and is deterministic.

### Task 6 — AutoCAD / accoreconsole deep-inspector source

Implemented source includes:

- `IBlockDeepInspector` / result contract;
- deterministic `accoreconsole.exe` locator with explicit environment override;
- testable subprocess executor boundary;
- per-inspection disposable staging directory;
- **source file is never passed directly to accoreconsole**; only a copied inspection asset is passed;
- source hash before/after evidence and `SourceIntegrityFailed` blocker;
- structured stdout protocol for block names, attributes, text and bounding box;
- failure preserves basic candidate state;
- LISP resource used only for bounded metadata extraction.

No real accoreconsole process was launched by E5 in source-first mode.

### Task 7 — Batch Block Archive WPF workflow

Implemented:

- main-window Block Archive entry;
- folder scan UI;
- multi-row review grid;
- candidate suggestions shown without auto-populating Component authority;
- explicit Component, SymbolRole, file SourceType selection;
- `GeneratedGeneric` excluded from file-import SourceType choices;
- deep-inspection action;
- exact-duplicate/review status visibility;
- stable PortID/PinID -> connection-point mapping editor;
- source-integrity blocker;
- whole-selected-batch preflight before first archive write;
- explicit confirmation dialog immediately before writes;
- actual revision/path/SHA reflected after approval;
- closing/cancel/scan/deep inspection paths do not intentionally write `SymbolArchive.json` or archive revisions.

### Task 8 — CP3-A integration test source

Added a disposable-workbook/full-flow test source covering:

```text
minimal workbook with explicit Component/Port/Pin
-> recursive scan
-> candidate suggestion
-> explicit rev-001 approval
-> exact duplicate reuse
-> rev-002 supersede
-> independent PanelFootprint role
-> current Approved resolution
-> GeneratedGeneric fallback with explicit PIN-1 only
-> historical/current manifest pinning
-> workbook/source byte-hash preservation
-> corrupt multiple-Approved authority fail-closed
```

## 5. TDD / verification truth

The Product Owner-approved source-first amendment permits test source to be authored before/alongside implementation when the E5 conversation lacks Windows/.NET execution binding.

The current execution container was checked and has no `dotnet`, `csc`, or `msbuild` executable. Therefore E5 did **not** execute or claim any of the following as PASS:

```text
Task 0 baseline full Release tests: NOT_RUN / RESERVED_FOR_CODEX_LOCAL_VERIFICATION
Task 0 Desktop Release build: NOT_RUN / RESERVED_FOR_CODEX_LOCAL_VERIFICATION
Task 1 RED/GREEN execution: NOT_RUN / RESERVED_FOR_CODEX_LOCAL_VERIFICATION
Task 2 RED/GREEN execution: NOT_RUN / RESERVED_FOR_CODEX_LOCAL_VERIFICATION
Task 3 RED/GREEN execution: NOT_RUN / RESERVED_FOR_CODEX_LOCAL_VERIFICATION
Task 4 RED/GREEN execution: NOT_RUN / RESERVED_FOR_CODEX_LOCAL_VERIFICATION
Task 5 RED/GREEN execution: NOT_RUN / RESERVED_FOR_CODEX_LOCAL_VERIFICATION
Task 6 fake-process/unit execution: NOT_RUN / RESERVED_FOR_CODEX_LOCAL_VERIFICATION
Task 7 coordinator/UI test execution: NOT_RUN / RESERVED_FOR_CODEX_LOCAL_VERIFICATION
Task 8 integration test execution: NOT_RUN / RESERVED_FOR_CODEX_LOCAL_VERIFICATION
focused SymbolArchive Release suite: NOT_RUN / RESERVED_FOR_CODEX_LOCAL_VERIFICATION
full Release suite: NOT_RUN / RESERVED_FOR_CODEX_LOCAL_VERIFICATION
Desktop Release build: NOT_RUN / RESERVED_FOR_CODEX_LOCAL_VERIFICATION
git diff --check executable command: NOT_RUN / RESERVED_FOR_CODEX_LOCAL_VERIFICATION
```

`NOT_RUN != PASS` is preserved.

No baseline `706/706` or prior CP2-E2 PASS was reused as fresh CP3-A evidence.

## 6. Protected-state truth

E5 source-first execution used Git source/metadata and did not have access to the Product Owner Windows production data paths.

Therefore:

- production `Component_Intelligence_Database.xlsx`: **NOT_TOUCHED_BY_E5_SOURCE_FIRST**; no new workbook write code path was added;
- production Component Intelligence SQLite: **NOT_TOUCHED_BY_E5_SOURCE_FIRST**; no CP3-A SQLite mutation code was added;
- source DWG/DXF production folders: **NOT_TOUCHED_BY_E5_SOURCE_FIRST**;
- formal WDP/DWG/DWT/AutoCAD Electrical production assets: **NOT_TOUCHED / NOT_AUTHORIZED**;
- archive-write test source uses disposable temp/archive roots;
- integration test source creates its own disposable workbook and text-byte DWG/DXF fixtures.

Actual production workbook/SQLite/source-folder before/after SHA checks require the authorized Windows verification host and remain NOT_RUN.

## 7. Task 9

Exact required status:

```text
Task 9 = NOT_RUN_BY_E5 / RESERVED_FOR_SEPARATE_CODEX_LOCAL_ACCEPTANCE
```

This source-first DONE claim does not imply WPF physical acceptance, real AutoCAD/accoreconsole execution, real DWG metadata correctness, or Product Owner acceptance.

## 8. Exact Codex verification handoff

PM should dispatch Codex against the final E5 branch after source review, preserving the same implementation lineage.

Codex must first verify:

```text
repo   = jackp803/Component-Intelligence-Public
branch = agent/e5-cp3a-component-block-archive-foundation-20260902
base   = 69ed47c6539cb2ececd0f93c1c8d2f0aa19533cf
source candidate commit = 5906ddd38b9583d2591c4c6bcc2c58b793165062
source candidate tree   = b80264ef1f72ce67e0f50ddb067623b3058aee35
Draft PR = #22
```

Then run the source verification gates from the exact checked-out final branch head:

```powershell
dotnet test tests/ComponentIntelligence.Tests/ComponentIntelligence.Tests.csproj -c Release --filter FullyQualifiedName~SymbolArchive
dotnet test tests/ComponentIntelligence.Tests/ComponentIntelligence.Tests.csproj -c Release
dotnet build src/ComponentIntelligence.Desktop/ComponentIntelligence.Desktop.csproj -c Release
git diff --check
```

Codex must report actual counts/warnings/errors and fix only CP3-A source defects if separately authorized by PM. It must not convert NOT_RUN into PASS without execution.

For physical Task 9, use disposable source/archive/database assets, record production workbook/SQLite/source-file identity before and after as required by the implementation plan, verify the WPF Block Archive flow, run supported accoreconsole deep inspection on disposable copies, and capture source-integrity/process/screenshot evidence. Do not bypass host safety controls.

## 9. Remaining gates

Source implementation Tasks 1–8 and source/handoff Task 10 are complete for E5 source-first delivery.

Remaining gates are external verification/acceptance, not source implementation claims:

1. Codex exact-workspace compile + focused/full tests + Desktop build + `git diff --check`.
2. Codex Task 9 Windows/WPF/AutoCAD physical acceptance.
3. PM source/evidence review.
4. Product Owner/PM acceptance and any later merge decision.

No merge is authorized by this handoff.
