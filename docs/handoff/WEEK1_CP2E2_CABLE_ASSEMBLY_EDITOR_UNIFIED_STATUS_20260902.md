# Week 1 CP2-E2 Cable Assembly Editor — Unified Baseline Status

Date: 2026-09-02

Task: `CODEX-W1-20260902-014`
Checkpoint: `CP2-E2-UNIFIED-BASELINE-IMPLEMENTATION-RESUME`
Worker disposition: `PARTIAL`
PM acceptance: `NOT REQUESTED / NOT IMPLIED`

## 1. Why this Worker stops PARTIAL

The implementation and automated verification work had already reached the mandatory real Windows UI smoke. The remaining acceptance requires repeated physical WPF canvas selection, multi-select, dialog editing, save/reload and visual validation.

The Chat-host UI-control path proved fragile and previously hit a host safety-layer block. During continuation, the Product Owner explicitly directed this Worker to stop spending time on Chat-based computer/UI control and return the remaining real Windows UI acceptance to PM.

Recommended next executor, in priority order:

1. **Codex on the approved local Windows host** — preferred for deterministic local UI/computer execution and evidence capture.
2. **Product Owner manual execution** — follow the remaining checklist below and capture screenshots/state evidence.

Do not bypass or weaken host safety controls. Unexecuted UI items remain `NOT_RUN / NOT_PROVEN`, not PASS.

## 2. Authoritative lineage re-verified during continuation

Repository: `jackp803/Component-Intelligence-Public`

Required branch:
`codex/cp2e2-cable-assembly-editor-unified-20260902`

Authorized base:
`e7ee6c3a45c6757f00e9421c241bc0176743c4c1`

Authorized base tree:
`0d53314f56914b01de875e097574fb5b17913b91`

Implementation commit re-verified locally:
`12465c4ca45787cede3a97aad3dafecf0d99e128`

Implementation tree:
`8f4f13e9aba179559357880217b9cd33ca344a43`

Continuation verification:

- `e7ee6c3...` is an ancestor of `12465c4...`.
- existing worktree is the required branch; no replacement worktree/branch was created.
- worktree was clean before this handoff-only evidence update.
- reported `12465c4` exists as a real Git commit.

Existing worktree:
`C:\Users\jackp\Documents\_codex_worktrees\Component-Intelligence-Public\cp2e2-cable-assembly-editor-unified-20260902`

## 3. Implementation state already reached before handback

The implementation commit contains the frozen CP2-E2 Cable Assembly Editor work, including:

- ordinary topology connection defaults to `ConnectionKind.Wire` with `CableInstanceId = null`;
- explicit Cable classification path for Unknown / Purchased / Custom;
- existing CableInstance reuse rather than duplicate identity on edit;
- detached Cable Assembly edit draft;
- Save / Cancel and unsaved-close protection;
- Construction: Unknown / Purchased / Custom;
- roles: Unknown / Trunk / Branch / Other;
- Branch suggestion uses maximum existing positive branch index + 1;
- CableInstance length/provenance editing;
- member add/remove with membership-only removal semantics;
- `RULE-CABLE-ASSEMBLY-001..007` projection with Chinese-first actionable messages;
- no generated member sub-tags;
- dedicated `建立複合線` entry and existing-member editor hooks;
- legacy direct `CreateCustomCableAssembly()` remains fail-closed under `LEGACY_CP2E2_REPLACEMENT_PENDING`;
- accepted `COMPONENT_INTELLIGENCE_DB_PATH` runtime isolation remains intact.

Previously recorded automated evidence in the authoritative continuation checkpoint:

- baseline before implementation: `676/676 PASS`;
- focused CP2-E2 gate: `46/46 PASS`;
- Electrical-focused regression: `517/517 PASS`;
- full Release tests: `706/706 PASS`;
- Desktop Release build: PASS, `0 errors / 22 existing warnings`;
- `git diff --check`: PASS;
- service TDD RED -> GREEN: `27/27 PASS`;
- topology ordinary-Wire TDD RED -> GREEN: focused `13/13 PASS`.

These are preserved continuation evidence. A future executor must still run the task-required **fresh final** test/build/diff gate after completing UI smoke.

## 4. Real Windows UI smoke evidence already proven

Production SQLite authoritative identity:

```text
path: C:\Users\jackp\AppData\Local\ComponentIntelligence\component-intelligence.db
size: 47,542,272 bytes
sha256: 4B1218C982297B080E31C6E985AB1919F8A7986E4BF61C38969E7830D3338E41
```

Disposable smoke DB:
`C:\Users\jackp\Documents\Component-Intelligence-CP2E2-UI-Smoke-CODEX-W1-20260902-014\component-intelligence-disposable.db`

Already proven before handback:

- disposable DB was initially a byte-for-byte production copy;
- candidate WPF app visibly showed the disposable DB path before entering Topology/Layout;
- no AutoCAD/accoreconsole process was running;
- disposable smoke project loaded through the real Electrical Workspace UI;
- one ordinary Wire plus three explicit CableInstances were present;
- ordinary connection `smoke-wire-ordinary` opened in the real Inline Connection Editor as `OrdinaryWire`;
- Cancel left disposable SQLite as `kind=Wire`, `cableInstanceId=null`, cable count `3`, assembly count `0`;
- a real explicit Cable route (`smoke-cmp-3:P1 <-> smoke-cmp-4:P1`) was selected/calibrated;
- non-assembly Inline Connection Editor remained available for `smoke-conn-cable-1` and displayed existing Cable/Imported length evidence;
- DPI-aware WPF/UI Automation calibration was investigated, but the remaining repeated canvas-control work is being reassigned rather than forced through Chat.

Local screenshot evidence retained under:
`C:\Users\jackp\Documents\Component-Intelligence-CP2E2-UI-Smoke-CODEX-W1-20260902-014`

Representative files include:

- `01-db-path-gate.png`
- `02-topology-loaded.png`
- `04-foreground-resume-state.png`
- `05-lower-canvas.png`
- `06-current-frame.png`

## 5. Safe stop state

Immediately before handoff, disposable smoke state was re-read from SQLite:

```text
connections: 4
cables: 3
assemblies: 0
ordinary smoke-wire-ordinary:
  kind: Wire
  cableInstanceId: null
```

No composite Cable Assembly had been saved by the incomplete UI smoke.

The candidate `ComponentIntelligence.Desktop.exe` process was closed normally using its main-window close path and reached terminal state.

Production SQLite was re-hashed **after candidate close** and remained exactly:

```text
size: 47,542,272 bytes
sha256: 4B1218C982297B080E31C6E985AB1919F8A7986E4BF61C38969E7830D3338E41
size match: true
sha256 match: true
```

AutoCAD/accoreconsole process count at safe stop: `0`.

Therefore the protected production SQLite identity is unchanged across this partial smoke session.

## 6. Remaining mandatory UI acceptance — transfer to Codex / Product Owner

Resume from the earliest unproven item; do not repeat completed steps blindly.

Still required:

- select the second explicit Cable route;
- select 2+ explicit Cable Segments and invoke `建立複合線` through the real UI;
- exercise explicit construction editing, including Unknown and Purchased/Custom behavior required by the handoff;
- verify Branch 1 + Branch 3 -> next Branch suggestion = 4;
- verify non-contiguous Branch numbering persists through Save/reload;
- edit segment length in metres and reopen with `User` provenance while untouched Imported/Mechanical provenance remains unchanged;
- add/remove member and prove CableInstance, connection and topology geometry remain;
- Cancel isolation;
- structural-error failed-Save isolation;
- real UI blocking and Chinese-first messages for required `RULE-CABLE-ASSEMBLY-001..007` cases (minimum safe fixture/actions, no fabricated PASS);
- Save -> reload -> double-click a member -> reopen the same assembly;
- verify non-assembly double-click still opens Inline Connection Editor;
- Layout/Cabinet real UI smoke;
- verify no generated member sub-tags;
- close candidate normally;
- production SQLite post-run size/SHA-256 must still equal the authoritative identity above.

No AutoCAD execution is authorized.

## 7. Work remaining after UI acceptance

After the transferred UI smoke is completed, the next executor must run fresh final verification required by the authoritative task:

- focused CP2-E2 tests;
- relevant Electrical regressions;
- full runnable test project;
- Desktop Release/required build;
- `git diff --check`;
- protected production asset checks.

Then update this handoff with exact fresh commands/counts, commit evidence, push this same required branch, and create/maintain the single Draft PR targeting:

`codex/cp2e2-ui-runtime-isolation-20260902`

Worker completion remains distinct from PM acceptance. PR creation is not merge authorization.

## 8. Current disposition

`PARTIAL`

Reason: implementation exists and substantial automated/real-UI evidence is preserved, but the mandatory real Windows UI smoke remains incomplete and is intentionally returned to PM for execution by Codex or the Product Owner.