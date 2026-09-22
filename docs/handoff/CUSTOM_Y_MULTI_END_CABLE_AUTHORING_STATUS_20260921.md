# Task-012: Custom Y / Multi-End Cable Authoring

Task: CODEX-W1-20260921-012

Disposition: **BLOCKED — actual project ownership and electrical graph differ from the required raw-Wire acceptance input.**

This is a preserved WIP, not a release candidate or completed implementation. Do not use it to author production data. Worker completion is not PM acceptance.

## Authority and identity

- Coordination main read: `23c9b8de2d20ab712e7bb0205c8ffdf349a16a43`.
- Component exact executable source: `88f5aae9ca75bd278c7e6cbafc7baaa43b4e2892`.
- Source tree verified: `d7fafbe4a1d8318233faf8927bc817a580ad47b1`.
- Isolated branch: `codex/custom-y-multi-end-cable-authoring-20260921`.
- Auto source remains `c61d1cc3cc66b392aa9a559468d995a0e9fb053b`, tree `ea1bb9bce25ef4ca41c9922b29f6f58a68646437`; no Auto branch or source changes.
- PR #31/#34 preserved; no merge/release.

## Decisive real-project evidence

The normal application used a freshly captured byte-for-byte disposable database. The visible database-path display was checked before entering Electrical Design. The initial representative project has 89 components, 180 connections, 28 cables and 2 assemblies, not the older 22-cable snapshot.

The six current connections are:

| Connection ID | Actual source | Actual destination | Current authority |
| --- | --- | --- | --- |
| conn-0452c3f5a4dc4c9d8c98e2da0770bb54 | X1 / RJ45-B / Pin 5 | X4 / WIRE-A / Pin 1 | Cable, separate existing CableInstance |
| conn-55d15ed79dd64ec18ccafb4b4568d2c9 | X1 / RJ45-B / Pin 4 | X4 / WIRE-A / Pin 2 | Cable, separate existing CableInstance |
| conn-24e5fc15a29d43ab9f820ba03b786d6f | X1 / RJ45-B / Pin 1 | X4 / WIRE-A / Pin 3 | Cable, separate existing CableInstance |
| conn-872d33b9a12340cbbeac7f98842b1064 | X4 / WIRE-A / Pin 3 | X5 / WIRE-A / Pin 3 | Cable, separate existing CableInstance |
| conn-7157cf207342481994b95df843558383 | X4 / WIRE-A / Pin 2 | X5 / WIRE-A / Pin 2 | Cable, separate existing CableInstance |
| conn-9e612bb102f141aa8f9fc395711341d8 | X4 / WIRE-A / Pin 1 | X5 / WIRE-A / Pin 1 | Cable, separate existing CableInstance |

The selection dialog correctly did not offer already-assigned Cable connections as raw Wire. No ownership was cleared or reassigned.

The retained task-011 initial snapshot has the three X1->X4 connections as raw Wire. Its other three raw Wire connections are also X4->X5, not X1->X5 (historical IDs: conn-b6ef85bea5874e6188a3428010088857, conn-1ff87fd876fe4ec4b0efa548296088ed, conn-41161dc5954c48699eb78b18462dfeb7). Thus reverting the six recent Cable assignments would not itself establish the required direct common-X1 electrical star.

X4/X5 WIRE-A ports own the actual conductor pins. Their M12-F ports are separate project ports. No WIRE-A-to-M12 pin equivalence was invented.

**Required PM/PO clarification:** authorize how the existing X1->X4->X5 electrical graph is to coexist with the explicitly confirmed physical common-X1 Y structure, and how the six existing Cable assignments are to be reconciled. This does NOT imply that electrical rewiring is necessary: preserving the existing graph with a separately confirmed physical topology may be correct, but the current service's direct-star gate does not represent that case. Do not change the common end to X4 just to pass the gate, rewrite X4->X5 as X1->X5, or silently absorb existing cables. No production correction was performed.

## Preserved implementation

- Optional `CableAssembly.PhysicalTopology`: one common port, one whole-cable identity, stable indexed branches, unknown/explicit trunk and branch lengths, original connection membership. The split has no endpoint ID and is not an electrical node.
- One overall CableInstance; no per-pin cable, invented core number, or generated electrical component. Existing connections retain Kind, IDs, endpoints, NetId and core metadata; only cable membership is assigned on successful apply.
- Detached service drafts with explicit construction and end confirmation, duplicate/overlap checks, original-source fingerprints, finite-length validation and stable branch-index editing.
- Separate Topology action, readable conductor picker and dedicated physical editor. Existing multi-end reopening is dispatched separately; legacy editor remains present.
- Connector Cable guidance no longer tells users to model each Y branch independently.
- Additive JSON property at current schema 0.5; missing property remains null. Old-reader round-trip preservation is NOT claimed. General persisted malformed-topology validation and full migration review remain unfinished.

Changed source/test files:

- `src/ComponentIntelligence/Electrical/Domain/ElectricalProject.cs`
- `src/ComponentIntelligence/Electrical/Domain/MultiEndCableTopology.cs`
- `src/ComponentIntelligence/Electrical/Editing/MultiEndCableEditorService.cs`
- `src/ComponentIntelligence.Desktop/MultiEndCableEditorDialog.cs`
- `src/ComponentIntelligence.Desktop/TopologyCanvasControl.CableAssembly.cs`
- `src/ComponentIntelligence.Desktop/TopologyCanvasControl.xaml`
- `src/ComponentIntelligence.Desktop/ConnectorCableEditorDialog.cs`
- `tests/ComponentIntelligence.Tests/Electrical/MultiEndCableTests.cs`
- `tests/ComponentIntelligence.Tests/Electrical/MultiEndCableEditorServiceTests.cs`
- `tests/ComponentIntelligence.Tests/Electrical/MultiEndCableNavigationTests.cs`

## Verification

| Gate | Result |
| --- | --- |
| Exact baseline tree | PASS |
| Existing cable baseline | 64 PASS |
| Model RED | 1 expected assertion failure: physical topology dropped |
| Model GREEN | 1 PASS |
| Service RED | Missing new API compile errors, not a behavioral RED; recorded honestly |
| Service first run | 10 PASS / 1 FAIL: test assumed collection position instead of stable branch identity |
| Service/legacy focused after correcting that assertion | 75 PASS |
| UI navigation RED | 1 expected failure: new entry absent |
| Full final Release regression | 841 PASS / 0 FAIL / 0 SKIP |
| Desktop Release build | PASS / 0 errors / 22 existing warnings |
| Normal application startup/path/navigation/picker | PASS |
| Picker Cancel persisted snapshot equality | PASS |
| Real X1/X4/X5 authoring/Save/revision/reload | BLOCKED, NOT passed |
| Generic 1->2 / mixed-count 1->3 service | PASS |
| JSON round-trip / non-contiguous branch indices | PASS |
| General real editor Cancel and failed Save | NOT_RUN |
| New physical Cable Detail/planning contracts | NOT_IMPLEMENTED |
| AutoCAD physical execution | NOT_RUN |
| Normal candidate close | PASS; no candidate process remains |
| git diff --check | PASS |

Full tests include the focused UI navigation test. Initial Desktop compile had an extra closing parenthesis in the new picker; corrected and build rerun successfully. No build failure is represented as a passing run.

**Downstream safety limitation:** this WIP has no completed multi-end planning projection or executor contract. It is not approved for drawing generation; the existing point-to-point planning path must not be treated as correct multi-end output. No AutoCAD was launched.

## Protection and evidence

- 172 protected immutable files captured before/after: changed 0.
- Production DB and initial disposable copy SHA-256: `B53324E867424055A7503AB6AE5F2CA2C8E6BA19722DEE1F79E85B3BB8E7EB25`.
- The production DB differs from the older task-011 hash before this task began; it was not reverted or normalized.
- Production SQLite, workbook, formal CAD files, source libraries and approved K7L revision remain unchanged during task012.
- Local private evidence is under the task-ID evidence directory in the user's temp root: protected-before/after manifests; before-authoring/after-cancel snapshots; read-only snapshot script; UI DB. Private absolute paths are not published.
- TRX evidence is retained locally under the isolated test project's TestResults directory; sanitized aggregate evidence accompanies this handoff.
- The normal project's load-time central archive synchronization updated only the disposable in-memory project/cache. A normal Save established the comparison baseline before the picker was opened. No cable was authored in that UI session.

## Resume boundary

Do not mark DONE or resume the Human Engineering Gate based on this WIP. First reconcile the acceptance input and physical/electrical grouping semantics with PM. Then complete bounded domain validation, safe legacy editor protection, multi-end planning/detail consumption, real editor and revision/reload verification, all fresh final gates and any admitted isolated execution proof. Do not auto-confirm other cables.

`NOT_RUN != PASS`. `APPLIED != VERIFIED`. `DONE != Product Owner UAT acceptance`.
