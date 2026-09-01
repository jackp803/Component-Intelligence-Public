# Week 1 CP2-B Topology v2 Export UI Status

Project ID: `electrical-drawing-automation`  
Task ID: `CODEX-W1-20260901-003`  
Checkpoint ID: `CP2-B`  
State: `IN_PROGRESS`  
Disposition: `NOT_FINAL`

## Authority

| Item | Value |
|---|---|
| Repository | `jackp803/Component-Intelligence-Public` |
| Exact source branch | `codex/week1-cp2a-first-slice-v2-export-20260901` |
| Exact source commit | `7c3a1c387f095d5c90d02ef2b4c2f2bfc23717bd` |
| Exact source tree | `154d23c1e7168c5603b3d6c35cf96bc1e49ed073` |
| Implementation branch | `codex/week1-cp2b-topology-v2-export-ui-20260901` |
| Draft PR base | `codex/week1-cp2a-first-slice-v2-export-20260901` |
| Draft PR | `jackp803/Component-Intelligence-Public#14` (`DRAFT / OPEN / UNMERGED`) |

The implementation uses a clean linked worktree created directly from the exact source commit.
The Product Owner's pre-existing dirty checkout was not used for implementation.

## Protected Baseline

Local absolute paths are intentionally excluded from this Git evidence.

| Asset/state | Baseline evidence |
|---|---|
| Production Component Intelligence SQLite | SHA-256 `4B1218C982297B080E31C6E985AB1919F8A7986E4BF61C38969E7830D3338E41` |
| Configured central workbook | SHA-256 `7CD84A26F78F6FA6DF03A4C7193568C4700558C890BCF424958099B02B64CB16` |
| WDP/DWG/DWT candidates outside automation work areas | 1,878 files; manifest SHA-256 `9C81EA1C17A34ADC62C67798E574B3ACB7AC9938C86ADBDF04A8F2027A631CB2` |
| Formal ACADE library DWGs | 35 files; manifest SHA-256 `22C695480B4833F72ECFA6C0A0A2342408812AD8ECD4E8FFB8481DDFC38EFCBC` |
| AutoCAD implementation repository | clean at `b925730a22068981f9b452b9011550f042748a5b` |
| AutoCAD / accoreconsole processes | 0 |
| Product Owner source checkout | 50 pre-existing dirty paths; implementation writes: 0 |

## Baseline Verification

Command scope:

```text
AutocadStagingGraphV2ExporterTests
AutocadReviewPreflightCoordinatorTests
AutocadConnectionPointBindingLoaderTests
AutocadEngineeringDrawingEvidenceLoaderTests
AutocadStagingReviewRunnerTests
```

Result: `26 passed, 0 failed, 0 skipped`.

## CP2-B1 - Dedicated Non-Launch v2 Export Coordinator

```text
status: COMPLETE
payload_commit: f4a55f165ac66fb3f16f030cd9643b8866e0fca7
payload_tree: 6f2ec6f24b1a344944bd5c872cf31742662d2efb
remote_sha_confirmed: true
```

Files:

- `src/ComponentIntelligence.Desktop/AutocadStagingGraphV2ExportCoordinator.cs`
- `tests/ComponentIntelligence.Tests/Electrical/AutocadStagingGraphV2ExportCoordinatorTests.cs`
- `tests/ComponentIntelligence.Tests/ComponentIntelligence.Tests.csproj`

TDD evidence:

- RED: four compile failures because `AutocadStagingGraphV2ExportCoordinator` did not exist.
- GREEN coordinator suite: `4 passed, 0 failed, 0 skipped`.
- Combined exporter/loaders/coordinator/v1-runner boundary suite: `30 passed, 0 failed, 0 skipped`.
- `git diff --check`: PASS.
- Static prohibited-dependency scan: no `AutocadStagingReviewRunner`, `Process.Start`,
  `ProcessStartInfo`, PowerShell, accoreconsole, or symbol-acceptance-registry dependency.

Behavior:

- Loads existing audited connection-point bindings and drawing evidence read-only.
- Delegates serialization to the accepted `AutocadStagingGraphV2Exporter`.
- Writes exactly one `lrdu-staging-route.v2.json` into a fresh child of the existing local staging convention.
- Loader or preparation hard errors return blocking evidence and create no artifact or run folder.
- The input `ElectricalProject` remains unchanged.
- No symbol acceptance registry is required for this pure contract export.

## Explicitly Not Run / Not Changed

`NOT_RUN != PASS`.

- AutoCAD / accoreconsole execution: `NOT_RUN` - prohibited for CP2-B.
- PowerShell drawing runner: `NOT_RUN` - prohibited for CP2-B.
- WDP/DWG/DWT writes: `NOT_RUN` - prohibited for CP2-B.
- Formal ACADE library writes: `NOT_RUN` - prohibited for CP2-B.
- Production SQLite/workbook writes: `NOT_RUN` - prohibited for CP2-B.
- Cloud/Notion writes: `NOT_RUN` - prohibited for CP2-B.
- Page Planner, cable grouping, placement, routing, Drawing IR and unfinished drawing policy: `NOT_IMPLEMENTED` - out of scope.

## Retained Blockers

- `RS485_WIRE_LAYER_POLICY`
- `PAGE_POLICY_FIRST_SLICE`
- visible wire-number policy
- explicit project NetId/runtime evidence
- production symbol/connection-point approval
- POC WDP staging-seed approval
- physical approved title-block/template resolver and compatibility evidence

These remain explicit and are not required to complete the pure v2 artifact export action.
`NET-TBD-*` remains internal machine identity only.

## Next Action

Next owner: Codex within the same authorized CP2-B task.  
Next action: add the separate Topology Editor v2 export action while preserving the existing v1 action and runner behavior.
