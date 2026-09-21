# Phase 2B Symbol Library Onboarding

Task ID: `CODEX-W1-20260921-007`
Disposition: `PARTIAL`
Date: 2026-09-21

## Result

One evidence-backed company symbol was onboarded through the existing CP3-A
approval service. The current production drawing-planning integration does not
consume that approval. A real project-instance probe reproduced the gap before
native execution. No Phase 2B APPLIED result is claimed.

This is an evidence-only branch. No product source, engineering schema, drawing
policy, formal CAD file, or production database was modified.

## Exact executable identities

| Repository | Starting and final executable head | Starting and final executable tree |
| --- | --- | --- |
| Component | a3e89800dcc468401bff321286fcfccd9c0862ac | 09de825f3614622171347af5f50231de343f70bf |
| Auto | c61d1cc3cc66b392aa9a559468d995a0e9fb053b | ea1bb9bce25ef4ca41c9922b29f6f58a68646437 |

Evidence commits contain this handoff and its adjacent sanitized JSON only.
The Phase 2A executable identities remain unchanged.

## Selection and discovery

The configured central workbook was read through
`WorkbookComponentKnowledgeStore.ListAsync()`: 30 authoritative components.
The production project was read with SQLite `mode=ro` and `query_only=ON`.
The selected real project is `2fe3fb260c7d4c0eb3f5661e4b112a01`; its actual
K7L instance is `bom:7:omron:k7l-at50dp:1`.

`BlockArchiveScanner.ScanAsync()` recursively read the established
`FORMAL_ACADE_LIBRARY/Schematic` source root: 35 DWGs, 0 DXFs, 0 exact duplicate
files. `SymbolCandidateMatcher.Rank()` produced suggestions only.
The existing CP3-A archive initially contained no bindings.

The previous full library compatibility audit identifies K7L as its sole
exact-compatible component. Fresh source SHA matches that audited artifact.
The central catalog supplies the exact ComponentId and all eight stable PinIds.
This combination, including the actual project instance and audited electrical
interface, is the approval evidence; the filename is not identity authority.

| Count | Value |
| --- | ---: |
| Scanned source files | 35 |
| Exact duplicate files within scan | 0 |
| Initially existing approved revisions | 0 |
| Newly approved revisions | 1 |
| Reused existing revisions | 0 |
| Candidate-only/unapproved source files | 34 |
| Selected representative component types | 1 |
| GeneratedGeneric assets used in a run | 0 |
| Physical runs / generated DWGs | 0 / 0 |

The batch was deliberately limited to the strongest established evidence.
The remaining 34 files are candidates, not 34 independently validated missing
component types. No arbitrary quota was used to promote ambiguous assets.

## Per-symbol approval

| ComponentId | Manufacturer/model | Role | SourceType | Source alias | Source/archive SHA-256 | Revision | Bindings | Used in run | Generic required | Ambiguity |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| OMRON_K7L-AT50DP | OMRON / K7L-AT50DP | Schematic | ApprovedCustom | FORMAL_ACADE_LIBRARY/Schematic/HDV1_LiquidLeakAmp8.dwg | 217A2AC8BDA3418D4E77D1A4D51015AAD7FC93FE819420EF0279EEFFFFBF3828 | rev-001 | 8 explicit existing catalog PinIds | NO | NO: exact asset exists | NONE for archive identity/binding; project consumption unresolved |

Archive-relative immutable asset:
`Documents/OMRON/K7L-AT50DP/autocad/schematic/rev-001/symbol.dwg`.

Approval was performed by `SymbolArchiveApprovalService.ApproveAsync()` under
the explicit Phase 2B campaign authorization for evidence-established mappings.
It returned `CreatedRevision`. The source was copied, not moved or overwritten.
`SymbolResolver.ResolveAsync(..., allowGeneratedGeneric: false)` then returned
`ApprovedCustom / rev-001` with the exact source/archive hash.

| Existing catalog PinId | Audited connection attribute |
| --- | --- |
| OMRON_K7L-AT50DP_POWER_1 | X8TERM01 |
| OMRON_K7L-AT50DP_POWER_8 | X8TERM08 |
| OMRON_K7L-AT50DP_SENSING_2 | X1TERM02 |
| OMRON_K7L-AT50DP_SENSING_3 | X1TERM03 |
| OMRON_K7L-AT50DP_SENSING_4 | X1TERM04 |
| OMRON_K7L-AT50DP_OUTPUT_5 | X4TERM05 |
| OMRON_K7L-AT50DP_OUTPUT_6 | X4TERM06 |
| OMRON_K7L-AT50DP_OUTPUT_7 | X8TERM07 |

These PinIds were read from the workbook, never synthesized from pin numbers.
Audited TERM labels and roles correspond to the catalog functions: GND/0V, VIN,
two sensing inputs, signal, leakage output, disconnection output, and COM+.
No common/ground connection was added.

Approval evidence references in the existing local integration audit:
- `symbols/library-compatibility-20260825/library-compatibility-report.md`
- `symbols/library-compatibility-20260825/library-symbol-xterms.tsv`
- `symbol-gate-20260826/component-definition-symbol-gate.tsv`
- current central catalog and actual project snapshot

Other reviewed first-project candidates remain unapproved:
- IFM_AL1342 / HDV1_IOLink: port-level representation versus physical-pin
  interface and incomplete reviewed binding.
- IFM_AL5021 / HDV1_RemoteIO16: field and communication interfaces are not a
  proven one-to-one replacement.
- PRO_FACE_PFXGP6540WCDW / HDV1_HMI: simplified port-level drawing does not prove
  the complete required connection map.
- Cable families / Heavy Duty: filename, nominal pin count, and existing page
  shape do not authorize component identity or pin/page assignments.

## Decisive executable probe

The local C# harness references the exact accepted Component project and invokes
the real CP3-A scanner, catalog reader, approval service, resolver, adapter,
RepresentationPolicy and DrawingPlanningInputBuilder. It passes the actual
project K7L component instance into a disposable in-memory project, retaining its
existing IDs. It does not rewrite the production snapshot.

Observed output:
```text
archiveSourceType = ApprovedCustom
archiveRevision = rev-001
definitionBindingCount = 8
planningFamily = FunctionalGeneric
planning.SourceType = null
planning.AssetPath = null
planning.AssetRevision = null
approvedAssetSelected = false
adapterPathIsFullyQualified = false
exactDefinitionIdsPresentInInstance = 0
```

Four connected integration gaps need an explicit, tested repair plan:
1. `ElectricalWorkspaceWindow.DrawingPlanning.cs` constructs
   `RepresentationPolicy(new SafeNoAssetResolver())`.
2. `DrawingPlanningInputBuilder.cs` always requests `FunctionalGeneric` for
   component schematic representations; `RepresentationPolicy` does not ask
   its resolver for that family. Even injecting the existing CP3-A adapter does
   not select the approved symbol.
3. `Cp3aDrawingAssetResolver` returns archive-relative paths, while the
   execution boundary needs a path resolved against the configured archive
   root with the same pinned SHA.
4. Archive bindings use catalog endpoint IDs; real project instances use
   instance endpoint IDs. The eight strings are not identical. The builder
   currently produces presentation `PORT:` / `PIN:` identifiers, and the policy
   prefers those nonempty request bindings over archive bindings. Safe use
   requires an explicit catalog-to-instance binding bridge, not inferred IDs.

Changing all four layers entails project asset-consumption/binding integration,
beyond merely importing files or correcting an isolated archive copy failure.
Per handoff section 2, no broader architectural change was improvised.
The evidence is ready for PM to scope that integration. Merely patching IR paths
or forcing an ArchivedExact test fixture would not prove the current product
workflow, so that was not used as a substitute.

## Physical execution and symbol-use gate

```text
Phase 2B native execution = NOT_RUN / PROJECT_APPROVED_ASSET_CONSUMPTION_GAP
runId = null
executionEvidenceHash = null
executor package with selected exact asset = NOT_RUN
generated WDP/DWG inventory = empty
generated block-presence readback = NOT_RUN
newly approved source unchanged = YES
```

No AutoCAD/accoreconsole process was launched for this task. Historical Phase 2A
APPLIED is not reused as a Phase 2B result. Existing company skeleton, metadata,
DWT/WDT/WDL source identities remain preserved.

## Fresh required gates

| Gate | Result |
| --- | --- |
| SymbolArchive Release tests | 33 passed, 0 failed, 0 skipped |
| Drawing-focused Release tests | 57 passed, 0 failed, 0 skipped |
| Desktop Release build | PASS, 0 errors, 22 existing warnings |
| Auto execution-package/executor/PowerShell/IR-v2 focused suites | 30 passed |
| Product source changes | NONE |
| Full .NET / full Python regression | NOT_RUN; no product source changes |
| Both branch diff checks | PASS |

Tests validate the existing implementation; they do not negate the separately
reproduced integration gap. `NOT_RUN != PASS`.

## Protected state and durable evidence

See `evidence/phase2b-20260921/onboarding-evidence.json` for the complete sanitized
source manifest, exact approval result, resolver/planning probe, and 152 checked
protected identities. Changes: 0.

Formal ACADE library and production SQLite/workbook have task pre/post hashes.
Company WDP/DWT/WDT/WDL match their accepted Phase 2A hashes; these are explicitly
labeled accepted baselines rather than invented task pre-run measurements.
No formal DWG was opened in this task.

Authorized new archive files are the immutable K7L copy and
`SymbolArchive.json` in the configured central archive. The workbook itself
was not edited. Private absolute paths and proprietary DWG binaries are not
published to Git.

## Stop state

Phase 2B cannot be marked DONE: the representative exact symbol is approved but
not yet selected by the real project planning path. Physical execution and
company-block insertion proof remain outstanding. PM can scope the four
integration boundaries above; engineering and visual UAT remain separate.

`APPLIED != VERIFIED`. No merge, release, CP3-D or production generation.

## Corrective continuation: task-008 (2026-09-21)

Parent: `CODEX-W1-20260921-007`.
Corrective: `CODEX-W1-20260921-008`.
Terminal disposition: **BLOCKED** (supersedes task-007 PARTIAL as current stop state).
Authority: coordination main `37d2651`, corrective handoff and PM-approved design.

Starting evidence heads:
- Component: `1c9bb3fbe3b42fa8153205d15454acce874fdad0`
- Auto: `aebdc26f478dda4e6a6b912ef19a6d181a2284f3`

Both existing local branches were clean and matched remote before inspection.
No product source was changed; executable heads/trees remain the Phase 2A
identities listed above. No repair commit or new K7L revision was created.

### First decisive fresh evidence

A local .NET probe used Microsoft.Data.Sqlite with Mode=ReadOnly, Pooling=false
and PRAGMA query_only=ON to read the actual current production snapshot.
It deserialized the domain and called the existing ElectricalProjectMigrator
in memory only. It did not use ElectricalProjectRepository.GetAsync because
that method initializes tables; no database write was permitted.

Observed:
- Actual project: `2fe3fb260c7d4c0eb3f5661e4b112a01`.
- Actual instance: `bom:7:omron:k7l-at50dp:1`.
- Exact ComponentDefinitionId: `OMRON_K7L-AT50DP`.
- Snapshot schema 0.3; current in-memory migration produces 0.5.
- Three instance ports and eight instance pins exist.
- Typed SourcePortId / SourcePinId: **all null** after migration.
- Approved binding count: 8; explicitly bridgeable bindings: **0**.
- Missing required source endpoint bindings: **8**.
- K7L rev-001 / ApprovedCustom / approved SHA remain unchanged.

Blocker code: `ACTUAL_K7L_EXPLICIT_SOURCE_ENDPOINT_IDS_MISSING`.

There are legacy SOURCE_PORT_ID capability strings, but they are not the
authorized typed SourcePortId/SourcePinId bridge, and they do not supply the
eight missing typed pin source identities. We did not parse instance PinId
suffixes, compare PinNumber/PinName/PortName, use index/geometry/filename, or
populate missing source IDs. The current migrator preserves these missing
values rather than reconstructing them.

The domain's availability of source-ID properties does not establish that
this legacy actual project populated those properties. Thus a complete
real-instance exact bridge cannot be proved under the current authority.
Changing import/migration or restoring engineering source lineage is outside
the four authorized consumption repairs and requires a separately bounded
PM decision. A synthetic fixture with invented SourcePinIds would not satisfy
the required actual-project proof.

### Stop and continuation requirements

Implementation stopped before source edits. Resolver injection, approved-first
selection, execution-path adaptation and bridge implementation remain
NOT_IMPLEMENTED by task-008. Existing task-007 behavior remains unchanged.
Do not treat this evidence-only stop as a tested corrective implementation.

PM must authorize an evidence-backed source-lineage restoration procedure or
provide an authoritative actual-project snapshot with explicit typed source
IDs. It must preserve instance IDs and engineering connections and must not
silently infer identities or mutate production. Then resume the same branches,
reuse rev-001, implement the four corrections with TDD and run every required
fresh gate and real company-format execution.

Fresh probe: PASS as blocker reproduction, not product acceptance.
Focused/full product tests, Desktop Release build and real AutoCAD execution:
NOT_RUN in task-008 because the prerequisite actual bridge authority is absent.
The probe's referenced core project compiled with one existing nullable warning.
Both evidence-only diff checks: PASS.
No planning/IR/package/APPLIED proof, runId or generated WDP/DWG exists for task-008.
No new candidate onboarding, no approval recreation, no new archive revision.

Protected pre/post checks: 154 identities, 0 changed, including source library,
production SQLite/workbook, company WDP/DWT/WDT/WDL, approved rev-001 and
SymbolArchive.json. Sanitized full hashes and endpoint evidence:
`evidence/phase2b-20260921/task008-source-endpoint-blocker.json`.

Task-007 evidence and approval are preserved. PR #31 and #34 remain
Draft/Open/Unmerged. Final evidence commit identity is the Git commit containing
this appended record, not a new executable source revision.
