# Week 1 CP2-A v2 Export Status

Task ID: `CODEX-W1-20260901-002`
Checkpoint ID: `CP2-A`
State: `IN_PROGRESS`
Disposition: `PENDING_CP2_A2_A3_AND_FINAL_VERIFICATION`
Started: `2026-09-01T10:24:00+08:00`

## Authority

| Item | Value |
|---|---|
| Base repository | `jackp803/Component-Intelligence-Public` |
| Base branch | `codex/lrdu-electrical-layout-algorithm` |
| Exact base commit | `d97bd53a413a7391b93909d3f82f942499132c6e` |
| Exact base tree | `85cec62b0a329db902be18de86e77e92e3caa3f4` |
| Implementation branch | `codex/week1-cp2a-first-slice-v2-export-20260901` |
| Draft implementation PR | `jackp803/Component-Intelligence-Public#13` |
| PR state | `DRAFT / OPEN / UNMERGED` |

The implementation worktree was created from the exact pinned commit outside the pre-existing dirty Product Owner checkout. The task worktree was clean before baseline tests.

## Runtime Identity

```text
.NET SDK: 9.0.314
MSBuild: 17.14.43
host runtime: 9.0.16 x64
test target: net8.0
OS: Windows 10.0.26200 win-x64
```

## First-Slice Identity

Project: `2fe3fb260c7d4c0eb3f5661e4b112a01`

Devices:

1. `bom:15:belimo:ev015r2-kbac:1` — BELIMO `EV015R2+KBAC`.
2. `cmp-common-302774bd5b9e4bb5a03eae34736665fc` — X3 M12 male A-code 4-pin cable end.

Connections:

1. `conn-83ac398c69554ce882726b27557bebcd` — X3 pin 1 to BELIMO pin 7 D+.
2. `conn-8032fbadfc904b58bd62368f078c6abe` — X3 pin 2 to BELIMO pin 6 D-.
3. `conn-1f91e8253f494afa801b1b2e3dea19dd` — X3 pin 3 to BELIMO pin 1 COM.

The committed fixture is sanitized. Its connection-point bindings are explicitly test-only and do not claim Product Owner symbol or connection-point approval.

## Checkpoint Ledger

### CP2-A1 — First-Slice Contract Fixture And Invariants

```text
status: COMPLETE
payload_commit: 0b718ac1f1b228708bb56b5c8ed9b141d1b2681f
payload_tree: 63179966d8c947ee75672a92665b5a568d1e28aa
remote_sha_confirmed: true
draft_pr: 13
```

Files:

- `tests/ComponentIntelligence.Tests/Electrical/Week1FirstSliceV2Fixture.cs`
- `tests/ComponentIntelligence.Tests/Electrical/AutocadStagingGraphV2AdapterTests.cs`

Verified invariants:

- exactly three accepted connection IDs become exactly three `segment:<connectionId>` records;
- both accepted component IDs are retained in graph nodes;
- zero explicit Nets produce deterministic internal `NET-TBD-*` machine identities;
- `NET-TBD-*` is not promoted to a visible label or wire-number field;
- wire-layer approval remains `Missing` with no approved layer value;
- page intents, cross-page continuations and Heavy Duty records remain empty without evidence;
- reversed component, pin, binding, role and connection input order produces identical logical v2 JSON.

The A1 invariants passed on the existing v2 builder. A1 made no production-code change.

### CP2-A2 — Deterministic v2 Artifact Exporter

```text
status: NOT_STARTED
payload_commit: PENDING
payload_tree: PENDING
remote_sha_confirmed: false
```

### CP2-A3 — Legacy v1 Review Path Guard

```text
status: NOT_STARTED
payload_commit: PENDING
payload_tree: PENDING
remote_sha_confirmed: false
```

## Verification Commands And Results

| Stage | Command | Result |
|---|---|---|
| Baseline | `dotnet test tests/ComponentIntelligence.Tests/ComponentIntelligence.Tests.csproj --filter FullyQualifiedName~AutocadStagingGraphV2AdapterTests\|FullyQualifiedName~AutocadStagingGraphBuilderTests\|FullyQualifiedName~AutocadMachineNetIdentityResolverTests` | PASS: 27 passed, 0 failed, 0 skipped; pre-existing warnings |
| CP2-A1 | `dotnet test tests/ComponentIntelligence.Tests/ComponentIntelligence.Tests.csproj --filter FullyQualifiedName~AutocadStagingGraphV2AdapterTests` | PASS: 12 passed, 0 failed, 0 skipped; pre-existing warnings |
| CP2-A2 | Exporter-focused suite | NOT_RUN: checkpoint not started |
| CP2-A3 | v1/v2 boundary suite | NOT_RUN: checkpoint not started |
| Final focused | Electrical/AutoCAD filter | NOT_RUN: final verification not started |
| Full test project | Complete test project | NOT_RUN: final verification not started |

`NOT_RUN != PASS`.

## Preserved Product Policy

- `ElectricalProject` schema remains `0.3`.
- No parallel Canonical Topology schema is introduced.
- `CURRENT V1 AUTOCAD REVIEW PATH UNCHANGED`.
- `RS485 WIRE LAYER / PAGE POLICY NOT DECIDED`.
- No page ID, member assignment, same-page/cross-page choice, continuation, visible wire number or explicit NetId is invented.
- `NET-TBD-*` remains an internal machine identity only.
- Symbol, WDP and title-block candidates remain unapproved.
- Heavy Duty pagination is outside this slice and is not fixed to three pages.

## Protected Asset State

Baseline hashes:

| Asset | SHA-256 |
|---|---|
| Production Component Intelligence SQLite | `171320B028793906005653BCD1A2C9E7F37B9D81E026CA184D5A17A866263E0E` |
| Selected POC WDP reference | `4EEE4176669F91B426F5F06DDC342B24EAD9600D80E154F756BB0A92D6DC02A2` |
| Selected POC DWG reference | `7445DA2B2FB52A3C0B3E1AF50631D980161D662C2DADAED9C6861A1363832641` |
| Existing ACADE library cable block | `2DA32CD875FC1C9CB2368BE362F2EF26D937E8770DBCB48D6E3A5B770A4FAE23` |

```text
formal WDP/DWG/DWT modified: NO
formal ACADE library modified: NO
production SQLite/workbook modified: NO
AutoCAD launched: NO
accoreconsole launched: NO
cloud/Notion write: NO
AutoCAD implementation repository modified: NO
coordination repository modified by CP2-A: NO
```

The implementation plan's final coordination-pointer step is `NOT_RUN` because the direct Product Owner instruction and CP2-A handoff authorize modifications only in `Component-Intelligence-Public`. This implementation status file and Draft PR are the authorized durable evidence surfaces.

## Blockers Retained

1. `POC_WDP_STAGING_SEED_APPROVAL`.
2. `APPROVED_TITLE_BLOCK_TEMPLATE`.
3. `REPRESENTATION_APPROVAL`.
4. `RS485_WIRE_LAYER_POLICY`.
5. `PAGE_POLICY_FIRST_SLICE`.
6. `RUNTIME_EXPLICIT_NETS_EMPTY` — derived IDs remain internal.
7. `CURRENT_UI_EXPORTS_V1_ONLY` — intentionally unchanged in CP2-A.
8. `V2_EXPLICIT_EVIDENCE_INCOMPLETE` — unresolved planner evidence remains fail-closed.

## Next Owner / Action

```text
next_owner: Codex CP2-A implementation
next_action: Execute CP2-A2 test-first, then CP2-A3 and final verification. Stop after the complete CP2-A handoff for PM/Product Owner review.
```
