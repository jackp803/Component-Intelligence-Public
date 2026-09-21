# MVP Real-Asset Phase 2A - Project Metadata Status

Task ID: `CODEX-W1-20260921-006`  
Disposition: `DONE`  
Date: `2026-09-21`

## Authority and identity

- Starting head: `2dd4981d38eae163e2c55299bfdce9221f1b130d`
- Starting tree: `b099be15479ef3f662308a06495ed9b0f923213c`
- Executable source head: `a3e89800dcc468401bff321286fcfccd9c0862ac`
- Executable source tree: `09de825f3614622171347af5f50231de343f70bf`
- Branch: `codex/mvp-real-asset-phase2a-component-20260921`
- Evidence commit: the commit containing this handoff; it changes documentation only.

The executable source commit is intentionally separate from the evidence-only
handoff commit so the tested binary/source identity remains explicit.

## Implemented bounded surface

- Added typed `DrawingProjectMetadata` and `DrawingPageMetadata` contracts.
- Added a user-local metadata store at the existing Component Intelligence local
  application-data boundary. Project metadata is keyed by stable project ID.
- Added the minimum Project Metadata editor to Drawing Planning.
- Transported metadata through planning input, deterministic hashing, Drawing IR,
  and `LocalDrawingExecutorClient` without changing the accepted schema version.
- Added optional WDT and WDL paths to the existing user-local drawing executor
  runtime settings.
- Kept production ElectricalProject SQLite and workbook data out of this metadata
  flow.

Changed executable/test files:

```text
src/ComponentIntelligence.Desktop/Controls/DrawingPlanningWorkspaceControl.Generation.cs
src/ComponentIntelligence.Desktop/Controls/DrawingPlanningWorkspaceControl.xaml
src/ComponentIntelligence.Desktop/Windows/ElectricalWorkspaceWindow.DrawingPlanning.cs
src/ComponentIntelligence.Infrastructure/Electrical/Drawing/DrawingExecutorRuntimeSettings.cs
src/ComponentIntelligence.Infrastructure/Electrical/Drawing/DrawingPlanningContracts.cs
src/ComponentIntelligence.Infrastructure/Electrical/Drawing/DrawingPlanningInputBuilder.cs
src/ComponentIntelligence.Infrastructure/Electrical/Drawing/DrawingPlanningJson.cs
src/ComponentIntelligence.Infrastructure/Electrical/Drawing/DrawingProjectMetadata.cs
src/ComponentIntelligence.Infrastructure/Electrical/Drawing/LocalDrawingExecutorClient.cs
tests/ComponentIntelligence.Tests/Electrical/DrawingExecutorClientTests.cs
tests/ComponentIntelligence.Tests/Electrical/DrawingProjectMetadataTests.cs
```

## Evidence-backed metadata mapping

The mapping was derived from the accepted WDP/WDT/WDL and representative formal
DWG evidence, not from attribute names alone.

| Component field | Company/ACADE authority | Title-block attribute | Result |
| --- | --- | --- | --- |
| `ProjectName` | WDP project line `*[1]`; WDL identifies project name | `NAME` through `LINE1` | Mapped |
| `EquipmentPartNumber` | WDP project line `*[2]`; WDL identifies equipment part number | `PART_NO.` through `LINE2` | Mapped |
| `Checker` | WDP project line `*[4]` | `CHECKED` through `LINE4` | Mapped |
| `Drafter` | WDP project line `*[5]` | `DRI` through `LINE5` | Mapped |
| `Revision` | WDP project line `*[7]` | `REV.` through `LINE7` | Mapped |
| Page `DrawingTitle` | WDP drawing description 1 | `TITLE` through `DD1` | Mapped |
| Page `DrawingDocumentNumber` | WDP drawing description 2 | `DWFG_NO.` through `DD2` | Mapped |
| Persistent page order/count | ACADE `SHEET` / `SHEETMAX` | `PG` / `TTL` | Derived |
| `Customer` | No unambiguous accepted company evidence | None | Intentionally unmapped |
| `WD_TB` | Existing title-block control mapping | `WD_TB` | Preserved; never user metadata |

## Runtime and persistence proof

The real Component harness loaded the runtime settings and project metadata back
from their user-local stores before planning. The disposable project identity was
`PHASE2A-UAT-SMOKE`; unmistakable test values survived reload and were included in
the deterministic planning/hash chain.

```text
planning input hash = 0AC6D3F03271A601B9B06BDB763F1E01239115D1F51A6F6AE3DA5CB67DCCAAD2
drawing plan hash   = 51AD4264366C5288E45A9BD5C1F649479B2F475AA5BB87CD81A0F0D97D4F3E52
drawing IR hash     = B3767059E59D6147631F3DD25FCE74C026B382BD84499721C7E27F4D05DF3236
```

Private absolute paths remain only in gitignored user-local settings. No private
company path is committed in this branch.

## Fresh verification

- Component test project, Release: `773/773 PASS`
- Desktop Release build: `PASS`, `0` errors, `0` warnings
- `git diff --check`: `PASS`
- Worktree clean at executable source head before this evidence-only commit.

## Cross-repository physical result

The real non-mocked chain completed in isolated staging with run ID
`CP3C-1003AE7975884998A2BE8C39865F9B5B` and
`electrical-execution-result.v1 / APPLIED`. The AutoCAD-side handoff records the
full package, result, title-block readback, skeleton, and protected-asset evidence.

`APPLIED != VERIFIED`. The bounded independent readback proves company title-block
identity and exact mapped attribute values only; it is not CP3-D electrical
semantic verification or Product Owner UAT acceptance.

## Protected state

The production Component SQLite remained byte-identical:

```text
size    = 47,542,272 bytes
SHA-256 = 4B1218C982297B080E31C6E985AB1919F8A7986E4BF61C38969E7830D3338E41
```

The company WDP/DWT/WDT/WDL and all 17 formal DWGs were read-only and are recorded
unchanged in the paired AutoCAD handoff. No workbook, production SQLite, formal
project, or formal ACADE library was modified.

## Remaining boundary

- `Customer` remains unmapped until Product Owner/company evidence makes its
  business meaning unambiguous.
- Company symbol/block library onboarding, CP3-D trusted readback, drawing semantic
  verification, merge/release, and broad Product Owner UAT remain out of scope.

No source-level blocker remains inside Phase 2A scope.
