# Week 1 CP2-D Cable Construction Type Status

## Disposition

```text
task_id: CODEX-W1-20260901-005
checkpoint_id: CP2-D
disposition: COMPLETE_PENDING_PM_PRODUCT_OWNER_REVIEW
```

Only the authorized explicit per-`CableInstance` construction-type evidence refinement was implemented.

## Authority

| Item | Verified value |
|---|---|
| Coordination repository `origin/main` commit | `ccb3c510f09f414744b86ce4b7c18f93d840ef8e` |
| Coordination repository tree | `d57d9b79886973fd59bbe3adf6ecd373de965dc2` |
| Exact source branch | `codex/week1-cp2b-topology-v2-export-ui-20260901` |
| Exact source commit | `5ab9c29a9a936eeaa48e3302d0c2a1f6492ee18c` |
| Exact source tree | `30620cad9a619546bd39c1d3d6288083a31055a3` |
| Implementation branch | `codex/week1-cp2d-cable-construction-type-evidence-20260901` |
| Implementation payload commit | `246f268904b8dec609008432b7e183d47b47dae3` |
| Implementation payload tree | `5bc0c138e26a3d5fabb048c55a8a639693c06148` |
| Payload remote SHA confirmed | `YES` |
| Stacked Draft PR | `jackp803/Component-Intelligence-Public#15` |
| PR base | `codex/week1-cp2b-topology-v2-export-ui-20260901` |
| PR head | `codex/week1-cp2d-cable-construction-type-evidence-20260901` |

The work used a clean isolated worktree created from the exact source commit. The Product Owner's existing checkout was not used for implementation.

## Implemented Semantics

- Added exactly three explicit values: `Unknown`, `Purchased`, and `Custom`.
- `CableInstance.CableConstructionType` defaults to `Unknown`.
- Missing property in an existing `ElectricalProject 0.3` snapshot loads as `Unknown`.
- The existing repository persistence round-trips all three values as enum strings.
- `CableAssembly.IsCustom`, assembly membership, and catalog-looking `CableDefinitionId` values do not override or infer the per-instance value.
- The v2 cable instance exposes `cableConstructionType` as a typed enum string associated with stable `cableId`.
- Missing project evidence at the v2 adapter boundary falls back to `Unknown`.
- The existing bounded Cable Segment editor now provides an explicit three-value selector for create/edit operations.
- No production cable was classified by this checkpoint.

## Compatibility

```text
ElectricalProject schema: 0.3 (UNCHANGED)
v2 contract schema: lrdu-staging-route.v2 (UNCHANGED)
v1 staging contract shape: UNCHANGED
```

The new project property is additive and defaulted. Existing persistence already uses `JsonStringEnumConverter`; no database schema migration or project schema bump is required. The classification is mapped directly from the project into the accepted v2 adapter, so the existing v1 staging contract does not acquire a new property.

Exact-source diffs:

- v1 AutoCAD Review handler/runner and v1 staging contract files: empty.
- CP2-B dedicated non-launch coordinator, handler, and XAML action: empty.
- protected WDP/DWG/DWT/workbook/database extensions in the implementation diff: empty.
- private absolute path scan: empty.

## TDD Evidence

RED evidence:

- Initial focused test compilation failed because `CableConstructionType` and its per-instance property did not exist.
- UI/editor focused test then failed because `CableSegmentOptions` did not accept explicit construction type.

GREEN evidence:

| Verification | Result |
|---|---|
| CP2-D focused tests | `12 passed, 0 failed, 0 skipped` |
| Electrical/AutoCAD focused suite | `435 passed, 0 failed, 0 skipped` |
| Full `ComponentIntelligence.Tests` Release run | `621 passed, 0 failed, 0 skipped` |
| Desktop Release build | `PASS`, `0 errors`, `20` pre-existing obsolete warnings |
| `git diff --check` | `PASS` |

Focused coverage proves:

- old project JSON without the property loads as `Unknown`;
- `Unknown`, `Purchased`, and `Custom` round-trip through the existing SQLite repository;
- assembly-level `IsCustom=true` does not override per-instance `Unknown`;
- catalog-looking definition IDs do not infer `Purchased`;
- v2 JSON exposes the explicit enum string by stable cable instance ID;
- v1 staging JSON remains unchanged;
- the bounded cable editor saves only the explicitly selected value.

## Protected State

Pre-implementation and post-verification values are identical:

| Protected asset/state | Baseline and final evidence |
|---|---|
| Production Component Intelligence SQLite | size `47,542,272`; SHA-256 `4B1218C982297B080E31C6E985AB1919F8A7986E4BF61C38969E7830D3338E41` |
| WDP/DWG/DWT candidates outside automation work areas | `1,878` files; manifest SHA-256 `6C7212A4076E6C9B5D7E46AFDE75D2E96B7996DCE66E6144039D899CB6D00633` |
| Formal ACADE library DWGs | `35` files; manifest SHA-256 `E02AFFD50A06275DDAD24B7C54280F67DFD49C846DB3821C90A47A1E4784D42A` |
| Integration-repository workbooks | `3` files; manifest SHA-256 `97FAE480C7DF65059B40044F896FCB556895A400A4A94AF06551DE81D12494C4` |
| Component source workbooks | `0` files; empty-manifest SHA-256 `E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855` |
| Component Intelligence processes after verification | `0` |
| AutoCAD / accoreconsole processes after verification | `0` |

Manifest authority is deterministic UTF-8 text sorted by relative path, with each record containing relative path, size, and file SHA-256.

## Explicit NOT_RUN

- Production SQLite or workbook writes: `NOT_RUN` - prohibited.
- Production classification of the current 22 cables: `NOT_RUN` - prohibited.
- AutoCAD / accoreconsole / drawing writer execution: `NOT_RUN` - prohibited.
- WDP/DWG/DWT or formal ACADE library writes: `NOT_RUN` - prohibited.
- Cloud/Notion writes: `NOT_RUN` - prohibited.
- Page Planner, Pin-to-Cable, Cable-to-Cable, pin/core completion, and Functional Circuit Profile work: `NOT_RUN` - outside CP2-D.

## Changed Files

- `src/ComponentIntelligence/Electrical/Domain/ElectricalTypes.cs`
- `src/ComponentIntelligence/Electrical/Domain/ElectricalProject.cs`
- `src/ComponentIntelligence/Electrical/Export/AutocadStagingGraphV2Adapter.cs`
- `src/ComponentIntelligence/Electrical/Topology/TopologyConnectionEditor.cs`
- `src/ComponentIntelligence.Desktop/InlineConnectionDialog.cs`
- `src/ComponentIntelligence.Desktop/TopologyCanvasControl.xaml.cs`
- `tests/ComponentIntelligence.Tests/Electrical/CableConstructionTypeEvidenceTests.cs`
- `docs/handoff/WEEK1_CP2D_CABLE_CONSTRUCTION_TYPE_STATUS_20260901.md`

## Blockers

None inside CP2-D scope. Downstream planning and drawing-policy work remains intentionally unstarted.

This document's containing status-only commit records the remotely confirmed payload above and does not change implementation behavior.
