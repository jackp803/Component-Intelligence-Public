# Week 1 CP2-E2 Baseline Integration Status

Task: `CODEX-W1-20260902-012`
Checkpoint: `CP2-E2-BASELINE-INTEGRATION`
Disposition: `PARTIAL`
Reason: the code/test integration is clean, but the required real Topology/Cabinet visual comparison is `NOT_RUN` because the Desktop application resolves the production SQLite Known Folder path even when the child process receives a disposable `LOCALAPPDATA` value.

## Authority and ancestry

| Authority | Commit | Tree |
| --- | --- | --- |
| Local-used source snapshot | `599b4f679a9b83ce713083e5ed2f4929f3914018` | `518aba7cec29e41a4a82bbcf4a89e5ee4ec11af0` |
| PM-accepted engineering lineage | `db3ed30f0960d59adf7033a279a085c1e8f2fda0` | `b3805e402dd5f10cc02415e8aed2d42a7d0612f3` |
| Common merge base | `de960f6a31d57650c8a614e42e0551f356e76343` | `3cd0a5e3d62faff99a35666bc22a2172568e6ab1` |

Branch: `codex/local-latest-cp2e1-baseline-integration-20260902`
Draft PR base: `codex/local-latest-reconciliation-20260902`
Integration merge commit: `f532f740d9058e12a180835bd1c8a8048f36ed95`
Integration merge tree: `af52761f355423c81b458ecff1653168fd31a739`
Integration merge parents: `599b4f679a9b83ce713083e5ed2f4929f3914018 db3ed30f0960d59adf7033a279a085c1e8f2fda0`
Draft PR: `https://github.com/jackp803/Component-Intelligence-Public/pull/19`
Remote confirmation: the branch remote SHA equaled `f532f740d9058e12a180835bd1c8a8048f36ed95` immediately after publishing the integration merge commit. A final equality check is recorded after this evidence update is pushed.

The candidate was created from exact `599b4f6` and merged exact `db3ed30f` with an explicit merge commit. Neither accepted input history was rebased or rewritten.

## Git conflicts and semantic resolution

Git reported three content conflicts:

| File | Resolution |
| --- | --- |
| `src/ComponentIntelligence.Desktop/ElectricalWorkspaceWindow.xaml` | Preserved the accepted v1 WDP/DWG review action and separate pure v2 export action. The forensic PDF-era rewind was not retained. |
| `src/ComponentIntelligence.Desktop/InlineConnectionDialog.cs` | Combined local multi-connection selection/count support with explicit accepted `CableConstructionType`. Removed legacy CustomTwoEnd/CustomY choices and fabricated harness fields from the active dialog contract. |
| `src/ComponentIntelligence.Desktop/TopologyCanvasControl.xaml.cs` | Preserved connector workflow and selected-connection behavior, transported explicit construction type, and isolated the legacy direct composite-cable entry pending CP2-E2. |

Additional shared-file semantic review:

| File | Resolution |
| --- | --- |
| `src/ComponentIntelligence.Desktop/TopologyCanvasControl.xaml` | Preserved local topology controls while relabeling the reachable cable command as connector-cable-only and documenting the pending replacement in its tooltip. |
| `src/ComponentIntelligence/Electrical/Domain/ElectricalProject.cs` | Combined local terminal/grouping state with schema-0.4 cable construction and structured segment-role evidence. |
| `src/ComponentIntelligence/Electrical/Persistence/ElectricalProjectMigrator.cs` | Preserved current schema `0.4`, accepted migration semantics, and local compatible fields. |
| `src/ComponentIntelligence/Electrical/Topology/TopologyConnectionEditor.cs` | Preserved connector/mated-connector functions and accepted construction evidence; fail-closed isolation replaces legacy direct assembly mutation. |
| `src/ComponentIntelligence/Repository/WorkbookComponentKnowledgeStore.cs` | Combined accepted power evidence ingestion with local archive/image behavior. |

No unresolved conflict marker or unmerged index entry remains. No shared conflict was resolved by blind whole-file ours/theirs selection.

## Local-latest 45-path disposition

Classification is by exact Git blob identity against `599b4f6` and `db3ed30f`, not timestamp.

| Path | Disposition |
| --- | --- |
| `src/ComponentIntelligence.Desktop/AutocadReviewPdfPackage.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence.Desktop/AutocadStagingReviewRunner.cs` | `SUPERSEDED_ACCEPTED_EXACT` |
| `src/ComponentIntelligence.Desktop/CabinetLayoutWorkspaceControl.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence.Desktop/CabinetLayoutWorkspaceControl.Interactions.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence.Desktop/CabinetLayoutWorkspaceControl.TerminalSections.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence.Desktop/ConnectorCableEditorDialog.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence.Desktop/ElectricalWorkspaceWindow.AutocadReview.cs` | `SUPERSEDED_ACCEPTED_EXACT` |
| `src/ComponentIntelligence.Desktop/ElectricalWorkspaceWindow.CentralSync.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence.Desktop/ElectricalWorkspaceWindow.PrimaryTabs.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence.Desktop/ElectricalWorkspaceWindow.xaml` | `SUPERSEDED_ACCEPTED_EXACT` |
| `src/ComponentIntelligence.Desktop/InlineConnectionDialog.cs` | `SEMANTIC_COMBINATION` |
| `src/ComponentIntelligence.Desktop/MainWindow.Electrical.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence.Desktop/MainWindow.Workflow.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence.Desktop/TopologyCanvasControl.ComponentVisuals.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence.Desktop/TopologyCanvasControl.EndpointModes.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence.Desktop/TopologyCanvasControl.MatedConnectors.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence.Desktop/TopologyCanvasControl.MultiSelect.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence.Desktop/TopologyCanvasControl.OrthogonalRouting.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence.Desktop/TopologyCanvasControl.Palette.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence.Desktop/TopologyCanvasControl.RouteIsolation.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence.Desktop/TopologyCanvasControl.TerminalGroups.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence.Desktop/TopologyCanvasControl.TerminalJunctions.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence.Desktop/TopologyCanvasControl.ViewFilters.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence.Desktop/TopologyCanvasControl.WirePreview.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence.Desktop/TopologyCanvasControl.xaml` | `SEMANTIC_COMBINATION` |
| `src/ComponentIntelligence.Desktop/TopologyCanvasControl.xaml.cs` | `SEMANTIC_COMBINATION` |
| `src/ComponentIntelligence.Desktop/TopologyImageCache.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence.Desktop/TopologyPdfExporter.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence/Electrical/Bridging/CentralArchiveImageSynchronizer.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence/Electrical/Bridging/CentralArchiveProjectSynchronizer.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence/Electrical/Domain/ElectricalProject.cs` | `SEMANTIC_COMBINATION` |
| `src/ComponentIntelligence/Electrical/Layout/PhysicalTerminalGroupingPolicy.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence/Electrical/Persistence/ElectricalProjectMigrator.cs` | `SEMANTIC_COMBINATION` |
| `src/ComponentIntelligence/Electrical/Topology/CommonConnectorCatalog.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence/Electrical/Topology/ConnectorCableTopologyService.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence/Electrical/Topology/MatedConnectorPresentationPolicy.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence/Electrical/Topology/TopologyConnectionEditor.cs` | `SEMANTIC_COMBINATION` |
| `src/ComponentIntelligence/Electrical/Topology/TopologyConnectionPotentialClassifier.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence/Electrical/Topology/TopologyEndpointBranchPolicy.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence/Electrical/Topology/TopologyEndpointConnectionService.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence/Electrical/Topology/TopologyPortGeometry.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence/Electrical/Topology/TopologyTerminalGroupingPolicy.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence/Electrical/Topology/TopologyTerminalJunctionService.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence/Repository/ComponentImageFileCache.cs` | `PRESERVED_LOCAL_EXACT` |
| `src/ComponentIntelligence/Repository/WorkbookComponentKnowledgeStore.cs` | `SEMANTIC_COMBINATION` |

Totals: `35 PRESERVED_LOCAL_EXACT`, `7 SEMANTIC_COMBINATION`, `3 SUPERSEDED_ACCEPTED_EXACT`.

## Legacy composite-cable isolation

All active references to `CreateCustomCableAssembly()` were inspected. The non-connector toolbar path no longer calls it and reports `LEGACY_CP2E2_REPLACEMENT_PENDING`. The service method remains compiled for compatibility but throws that same fail-closed diagnostic before any project mutation.

TDD evidence:

1. RED: the new isolation regression failed because the inherited legacy method mutated the project and did not throw.
2. GREEN: the minimal fail-closed guard made the regression pass while retaining unrelated connector-cable workflows.
3. The regression asserts that no Cable, CableAssembly, CableInstanceId, or connection Kind mutation occurs.

No replacement Cable Assembly Editor was implemented.

## Historical test-source handling

Four task-011 historical test files were selectively ported because they directly exercise preserved runtime classes without conflicting with accepted schema semantics:

- `ConnectorCableTopologyServiceTests.cs`
- `PhysicalTerminalGroupingPolicyTests.cs`
- `TopologyTerminalGroupingPolicyTests.cs`
- `CentralArchiveImageSyncTests.cs`

Their source/destination SHA-256 values matched at import. Four tracked historical test edits were not wholesale ported because they mixed schema time-slice expectations or legacy custom-harness behavior:

- `ElectricalWorkflowTests.cs`
- `TopologyEndpointConnectionServiceTests.cs`
- `TopologyInteractionTests.cs`
- `TopologyPortGeometryTests.cs`

This leaves a truthful visual-test gap for the marquee/group UI; it is not reported as a visual PASS.

## Contract preservation

- `ElectricalProjectMigrator.CurrentSchemaVersion` is `0.4`.
- CableInstance and CableAssembly construction type default to `Unknown`.
- CableAssembly member structured role defaults to `Unknown`.
- Legacy `Purpose` does not become structured role authority.
- `RULE-CABLE-ASSEMBLY-001..007` remain present.
- Accepted v2 export and power-evidence types/tests remain present.
- Existing v1 AutoCAD Review and pure v2 export boundary regressions pass.

## Verification

| Verification | Result |
| --- | --- |
| Focused v1/v2/legacy gate | `PASS`, 8/8 |
| Broad Electrical/topology/local behavior suite | `PASS`, 261/261 |
| Debug solution build | `PASS`, 0 errors; 0 warnings in the final no-restore gate |
| Full test project | `PASS`, 671/671; 0 failed; 0 skipped |
| `git diff --check` | `PASS` after removing one inherited Markdown trailing-space marker |

Commands:

```powershell
dotnet test tests/ComponentIntelligence.Tests/ComponentIntelligence.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~AutocadTopologyExportUiBoundaryTests|FullyQualifiedName~AutocadStagingGraphV2ExportCoordinatorTests|FullyQualifiedName~AutocadStagingReviewRunnerTests|FullyQualifiedName~LegacyCompositeCableIsolationTests"
dotnet test tests/ComponentIntelligence.Tests/ComponentIntelligence.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~Electrical|FullyQualifiedName~Topology|FullyQualifiedName~Cable|FullyQualifiedName~CentralArchiveImageSync"
dotnet build ComponentIntelligence.sln -c Debug --nologo --no-restore
dotnet test tests/ComponentIntelligence.Tests/ComponentIntelligence.Tests.csproj -c Debug --nologo --no-restore
git diff --cached --check
```

## Desktop visual/runtime smoke

Status: `NOT_RUN` for the required Topology/Cabinet comparison.

The exact candidate Desktop executable launched and exposed the main Component Intelligence window. The window still displayed the production SQLite path even though the process was started with a disposable `LOCALAPPDATA`; therefore the application was immediately closed before opening Electrical/Topology/Layout or invoking any write-capable workflow. Main-window launch alone is not treated as visual equivalence.

No AutoCAD or drawing writer was launched.

## Protected state

- Protected original dirty checkout: remains at `de960f6`; its 50 dirty paths and status-manifest SHA-256 `E27CF5AE764198CC0C46750488751CDF1F2809BE45762DF3B18134EC5AF08073` were not reset, cleaned, restored, stashed, rebased, merged, or edited.
- PR #17 branch/worktree: remains at `d4194c0ab856ef45a4f24e07a85486197b34fa30`, clean.
- PR #18 branch/worktree: remains at `599b4f679a9b83ce713083e5ed2f4929f3914018`, clean.
- Production SQLite: size `47,542,272`; last write UTC `2026-09-01T03:26:21.2822653Z`; SHA-256 `4B1218C982297B080E31C6E985AB1919F8A7986E4BF61C38969E7830D3338E41`, equal to the prior authoritative checkpoint hash after the bounded launch.
- Workbook, WDP, DWG, DWT, and formal AutoCAD Electrical libraries were not written.

## Blockers and next authority

The unified source candidate, build, and all runnable tests are green. The sole remaining blocker is an approved safe method for interactive Topology/Cabinet visual comparison without exposing production SQLite, followed by PM visual confirmation. CP2-E2 feature implementation and PR #17 remain on HOLD.

Worker completion is not PM acceptance.
