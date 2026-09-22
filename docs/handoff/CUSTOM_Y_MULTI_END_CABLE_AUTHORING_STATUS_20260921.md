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
- Preserved WIP executable commit: `58c06a915d57d0ea65ad6b0ff0135bc27e2d06a1`.
- Preserved WIP tree: `4d2f690b21b261849f0d8fd8d69efaf987786256`.
- Both identities confirmed through the GitHub commits API after push.
- Draft PR: https://github.com/jackp803/Component-Intelligence-Public/pull/32, stacked against the existing Phase 2B Component branch; do not merge.
- Subsequent handoff-only commits do not change the tested executable source.

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

## 2026-09-22 amendment continuation checkpoint

The preceding BLOCKED record is historical. PM amendment at coordination main
`ba83a6356ed7eecc2895b75c535349595b95f730` resolves the direct-star rule.
The same task and branch continue; no new task ID or K7L authority is created.

Current checkpoint: PARTIAL, not a terminal DONE claim. Real editor Apply is
paused for action-time confirmation of removal of the six unreferenced legacy
Cable records in the disposable copy. Production is never the target.

Executable Component commit: `b01024a7ef411b9a4d3e03baa65637b50dd7ed46`.
Tree: `0a8f2b6fe3dac4963f9e9ef5b74848aa98cab968`.
Paired Auto commit: `d7e53dd0c883f2753aeca8ad858d3c21fd447fe7`.
Auto tree: `816eb69a094ab6e88cec9e4be5002e7f8193d20c`.

Implemented and tested:
- Physical Common/Branches are independent of electrical edge direction. A chain
  is accepted without creating a star, new endpoint, junction, or Net.
- Explicit old Cable ownership confirmation is fingerprinted; incompatible,
  shared, stale, or assembly-owned membership fails closed before mutation.
- Old scoped core assignment evidence is retained separately. No new manufacturing
  core numbering or branch-specific conductor route is inferred.
- Persisted malformed physical topology receives RULE-MULTI-END-001 diagnostics.
- Legacy assembly and point-to-point assignment paths cannot replace multi-end truth.
- Planning carries one whole Cable identity with assembly identity, confirmed ends,
  exact mapping list and optional lengths. Multi-end endA/endB are null, not a false
  two-end projection. The additive optional planning fields retain v1 contract names.
- Cable Detail retains physical shape separately from electrical mappings. Auto
  consumes explicit precomputed display geometry on CP3C_CABLE_PHYSICAL, never a
  wire/junction helper. No physical execution has occurred for this checkpoint.

Fresh gates:
- Multi-end focused Release: 24 PASS / 0 FAIL / 0 SKIP.
- Full Component Release: 853 PASS / 0 FAIL / 0 SKIP.
- Desktop Release build: PASS, 0 errors, 22 existing warnings.
- Both git diff --check: PASS.
- Auto multi-end + PowerShell runtime/parse/safety: 11 PASS.
- Auto full regression: 485 run, 484 PASS, 1 ERROR. The historical
  `test_package_is_staging_only_and_has_hd1_and_all_gaps` requires private legacy POC
  WDP/AEPX/three DWGs absent from the isolated worktree. Exact diagnostic:
  `CM_LRDU_SOURCE_MISSING`. It was not skipped, repaired, or represented as PASS.
- Real Python -> C# -> Python three-branch Drawing Plan: exact object/hash equality,
  hash `8D4EAC3CBB6124BA8B7CDDEB17C8A45729DFA42C4BD79C3137B3EAE00BEB3B1F`.
- Schema validation of synthetic 1->2 / 1->3 input/page-plan/drawing-plan: PASS.

UI checkpoint: disposable path visibly verified; the six existing mappings are
selected with their current Cable references; Common X1, Branch X4/X5 and Custom
were explicitly selected. Apply has NOT been clicked. Save/Revision/Reload and
final edited-state Cancel remain NOT_RUN. The open UI build predates the final
planning changes; its authoring service matches the staged authoring behavior,
but final-build UI verification is still required.

Protection: fresh resume production DB size 64,823,296, SHA-256
`4D74B2C59B1A7079508BD2FB371FC232D42B4F16D68535299AC67D33E8B6C33A`.
This differs from the historical initial task baseline before resumption and was
not normalized. Disposable initial copy matched byte-for-byte. 172 protected
files rechecked after the above gates: changed 0. No AutoCAD process was launched.
Raw manifests, snapshots, TRX and Auto regression log remain local/private.

Remaining in order: explicit UI consolidation confirmation; Apply and normal
Save/Revision/Reload preservation comparison; final-build reopen/Cancel smoke;
actual real cable-slice planning and isolated execution if no unrelated engineering
blocker; resolve/report the external regression fixture dependency; final fresh
gates/protected hashes; publish exact heads/trees and terminal disposition.

## 2026-09-22 final directive execution

Current terminal disposition: **PARTIAL**. This section supersedes the previous
pre-Apply checkpoint, without removing its history. Authority: coordination main
`c97ab5eceaa7a16469fea140662b7636cb6f992d`,
`PM_TASK012_FINAL_VERIFICATION_20260922.md` and the physical/electrical amendment.

Component executable remains `b01024a7ef411b9a4d3e03baa65637b50dd7ed46`
(tree `0a8f2b6fe3dac4963f9e9ef5b74848aa98cab968`); tested evidence head
`c1bcbf3e8b931c10931127addfef06eaf5a6decf` has no intervening product changes.
Real execution used Auto `dee5faadd161ac7cb93b9963da11fa7d400e212b`, tree
`077f7295f79060affd674333fab1309f314e8a8c`.

### Actual Desktop acceptance

- Fresh disposable DB matched production byte-for-byte; the displayed application
  path was checked before entering Topology. Production was never opened for writes.
- Normal Load and Save established the baseline after normal catalog synchronization.
- The normal multi-end picker selected exactly six existing X1/X4/X5 connections.
  Construction and Common were initially unselected. Custom, Common X1, Branch 1
  X4, Branch 2 X5 and the explicit membership-consolidation checkbox were selected.
- Apply, normal Project Save, Load, actual wire double-click editor reopen: PASS.
- Reference `TEST-X1-X4-X5`; one new physical assembly and one overall Cable.
  Exact existing port IDs remain RJ45-B at X1 and WIRE-A at X4/X5. The UI discloses
  their M12 sibling-interface context; no WIRE-A contact was replaced by an M12 pin.
- All 180 connection IDs remain. The selected six differ only in CableInstanceId;
  FromEndpointId, ToEndpointId, NetId and explicit pin meaning are unchanged.
  The other 174 connections are identical. Components/Ports/Pins, nets, topology
  placements/routes and every other project field are unchanged.
- Six old Cable records were removed only after scoped confirmation and no remaining
  references; other 22 cables and prior two assemblies are unchanged.
- Saved assembly: `cable-assembly-b8ac120277044b62aab3365fb5786f48`.
  Overall cable: `cbl-ce99780c5c3b4ef9b1b1ed81260210e0`.
- Normal revision snapshot `REV-265C1E4C140DB1C505260D97` matches the saved project.
  The repository recorded trigger `MajorImport`; no direct revision write was used.
- Fresh reopened editor: changed Reference to `CANCEL-MUST-NOT-PERSIST`, Cancel,
  normal Save. Entire persisted project JSON equals the first saved state: PASS.
- Candidate closed normally. No electrical star rewrite or fake junction occurred.

### Real planning and execution

The bounded slice uses the three actual components, six original connections,
saved assembly and overall cable. C# DrawingPlanningInputBuilder, real
PythonDrawingPlannerClient and PythonDrawingIrClient produced READY, two pages,
one multi-end Cable Detail, null endA/endB and all six exact electrical mappings.
This is not a full-project readiness or company-symbol approval claim.

Initial runtime preflight rejected missing page metadata, before AutoCAD launch.
The disposable harness registered the two actual generated page IDs; all unconfirmed
project/title-block text fields remain null. User runtime settings were not changed.
The next preflight exposed Windows PowerShell HTML escaping of `->` as `-\u003e`,
causing packageHash mismatch. Auto's bounded canonicalization repair was RED/GREEN
tested, including literal escaped text and tamper rejection; no hash gate disabled.

Fresh real C# LocalDrawingExecutorClient -> Python -> PowerShell -> accoreconsole:
- run `CP3C-B7D3E8134FD947558367F9110752AF9C`;
- `electrical-execution-result.v1` status APPLIED; 13/13 command IDs APPLIED;
- self-contained company WDP with two local page DWGs; no legacy drawing references;
- IR hash `2A5C7AF20C3D04BE3E6689718ED3559623F9A9C255FA631ABF43B7CE1FEC0C48`;
- executor plan hash `266AD0667131252B4D821D99B413BA43B714E5C1262EC08FE85F00EA9C2B7BC9`;
- evidence hash `23A66ABBFF5D82FB4293BCAE5D57DA860E5D2EEC60699CCD6A43427502028247`.

### Open verification anomaly / stop boundary

A separate bounded accoreconsole inventory reopened the generated Cable Detail.
It returned exactly the five expected LINE segments on CP3C_CABLE_PHYSICAL;
their endpoints match the IR. The execution package contains no junction command.
However, the inventory script's before/after DWG SHA comparison failed with
`Readback changed DWG`, despite an inspection-only script and QUIT / N.
This affects only the disposable generated output, not any protected source.
The cause is unresolved. No further AutoCAD attempt or cleanup was performed.
Do not claim readback immutability, semantic VERIFIED, or terminal DONE.
The diagnostic stopped before persisting both hash strings, so those exact two
values are not claimed as durable evidence. The script, exception and raw readback
logs are preserved; a future investigation must capture them before another run.

### Fresh final gates and protection

- Component multi-end Release: 24 PASS; full Release: 853 PASS / 0 FAIL / 0 SKIP.
- Desktop Release build: PASS, 0 errors, 22 existing warnings.
- Auto focused planner/IR/package/executor/Windows PowerShell: 28 PASS.
- Auto full regression: 486 run, 485 PASS, 1 ERROR in the same unavailable private
  legacy POC fixture (`CM_LRDU_SOURCE_MISSING`); not represented as full-suite PASS.
- Both git diff --check: PASS. No implementation was changed in Component.
- Protected pre/post: 172 files, zero size/SHA differences. Production DB remains
  64,823,296 bytes, SHA `4D74B2C59B1A7079508BD2FB371FC232D42B4F16D68535299AC67D33E8B6C33A`.
- Private evidence remains under the task-ID temp root: final-ui.db, three project
  snapshots, normal revision query/proof scripts, real planning/IR/package/result,
  runtime logs, readback logs, TRX/build/test logs and protected manifests.
- Sanitized machine-readable evidence accompanies this handoff. Private absolute
  paths and production project snapshots are not published.

PR #32 / Auto #35 remain Draft/Open/Unmerged. No cleanup/slimming, release, CP3-D,
formal CAD write, central workbook write, source library write or K7L change.
Next bounded decision: investigate the staging readback hash discrepancy; do not
repeat the successful UI consolidation or rewrite the electrical graph.
