# Cable Wiring And Manufacturing Details Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Independent review is required before delivery.

**Goal:** Import and reuse one cable with connectable CAD geometry, a manufacturing sketch, and an editable pin table without duplicate BOM counts.

**Architecture:** Extend existing cable archive, instance and schematic detail flows. Keep complete Pin pairs as electrical authority; project those pairs into editable table drafts and render both views from one saved cable instance. Preserve legacy records and use explicit versioned CAD-role selections.

**Tech Stack:** C#/.NET 8, WPF, existing netDxf importer, JSON archive, SQLite project snapshots and existing PDF/print path. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-10-01-cable-dual-cad-archive-design.md`

**Status:** owner confirmed the complete scope and native implementation in this conversation. Task 1 in progress; independently verified baseline: 1213 tests passed.

## Global Constraints

- Runtime Port/Pin identities are distinct per physical cable.
- Extra drawings refer to that same `CableInstanceId`; sketches are not connectable authority.
- Keep `CablePinMapping` as the canonical complete mapping.
- Source files are copied, never moved/rewritten.
- Existing projects never upgrade to a new template automatically.
- AI-assisted archival stays outside the application.
- No new dependencies. No production DB/archive writes or AT runtime replacement.
- Preserve existing unrelated modified/untracked Drawing Planning files.
- User acceptance is manual; do not operate the owner's windows.

## Review Focus

- Old single-asset JSON loads with no manufacturing asset and no write-on-read (Task 1).
- Source changes, path traversal, linked descendants and region/hash mismatches fail without publishing partial archives (Tasks 1-2).
- Decreasing Pin count or changing connectors cannot silently lose mapping, bindings or live wiring (Tasks 3-4).
- Empty/incomplete rows and explicit NC/unused are not inferred continuity; edits revoke only the affected confirmation (Tasks 3-4).
- Missing/unavailable archive assets, long text and table overflow remain inspectable on reopened projects/PDFs (Tasks 5-6).

## Task 1: Versioned Optional Manufacturing Assets

**Files:**
- Modify: `src/ComponentIntelligence/SymbolArchive/SymbolArchiveContracts.cs`
- Modify: `src/ComponentIntelligence/SymbolArchive/SymbolArchiveRepository.cs`
- Modify: `src/ComponentIntelligence/SymbolArchive/CableArchiveResolver.cs`
- Modify: `src/ComponentIntelligence/Electrical/Domain/ArchivedCableTemplate.cs`
- Test: `tests/ComponentIntelligence.Tests/SymbolArchive/CableArchiveConsumptionTests.cs`
- Create test: `tests/ComponentIntelligence.Tests/SymbolArchive/CableDualRepresentationArchiveTests.cs`

**Interfaces:** Add optional `CableArchiveEntry.ManufacturingAsset : CableCadRoleAsset?`. `CableCadRoleAsset` stores archive-relative path, source SHA-256, units and optional `CableCadSelection`. Add `ResolvedCableArchive.ManufacturingPath : string?` without changing existing wiring `AbsolutePath`. Use `ci-symbol-archive.v5` only for new role-bearing data; preserve v1-v4 load behavior.

- [ ] Write failing tests: `LegacyArchiveLoadsWithoutManufacturingAndDoesNotWrite`, `DualAssetsRoundTripWithPinnedHashes`, `TraversalAndLinkedAssetsRejected`, `ChangedManufacturingAssetFailsResolution`.
- [ ] Run `dotnet test tests/ComponentIntelligence.Tests/ComponentIntelligence.Tests.csproj --filter CableDualRepresentationArchiveTests` and confirm the expected failures.
- [ ] Implement optional records, role validation and resolver verification. Reject invalid finite units, malformed hashes, duplicate roles and unsupported versions before writes.
- [ ] Run that filter plus `CableArchiveConsumptionTests` and `SymbolArchiveRepositoryTests`; all must pass.
- [ ] Review and commit only Task 1 paths.

## Task 2: Explicit CAD Selection And Transactional Archive Creation

**Files:**
- Create: `src/ComponentIntelligence/Electrical/Schematic/SchematicCadSelectionService.cs`
- Modify: `src/ComponentIntelligence/Electrical/Schematic/SchematicCadImporter.cs`
- Create: `src/ComponentIntelligence/SymbolArchive/CableArchiveCreationService.cs`
- Modify: `src/ComponentIntelligence.Desktop/SchematicCadFileLoader.cs`
- Create tests: `tests/ComponentIntelligence.Tests/Electrical/SchematicCadSelectionTests.cs`
- Create tests: `tests/ComponentIntelligence.Tests/SymbolArchive/CableArchiveCreationTests.cs`

**Interfaces:** `SchematicCadSelectionService.Select(SchematicCadAsset asset, CableCadSelection selection) : SchematicCadAsset`. Selection records explicit region/block role, original source hash and a deterministic geometry hash. `CableArchiveCreationService.CreateAsync(CableArchiveDraft draft, CancellationToken cancellationToken = default) : Task<CableArchiveEntry>` consumes loaded role geometries and source paths; writes a new revision with expected-original-archive concurrency checks.

- [ ] Write failing tests: `TwoFilesCreateOneTemplateWithoutTouchingSources`, `CombinedRegionsKeepContactTransforms`, `DifferentSelectionsHaveDifferentGeometryIdentity`, `StaticTableExcludedOnlyExplicitly`, `BoundaryCutOrUnsupportedGeometryIsDiagnosed`, `CancelledOrFailedSaveLeavesArchiveUnchanged`.
- [ ] Run the two new test-class filters and verify expected failures.
- [ ] Implement selected-geometry normalization using existing primitives/transforms. Preserve entity/block provenance where needed; do not infer electrical identities. Stage source copies and verify bytes before committing one archive revision. Refuse replacing an existing immutable revision.
- [ ] Run new tests and `SchematicCadImportTests`, `SchematicCadFileLoaderTests`, `SchematicHatchTests`.
- [ ] Review and commit only Task 2 paths.

## Task 3: Connector Choices And Canonical Four-Column Table Drafts

**Files:**
- Create: `src/ComponentIntelligence/Electrical/Schematic/CableManufacturingDraft.cs`
- Create: `src/ComponentIntelligence/Electrical/Schematic/CableManufacturingEditorService.cs`
- Modify: `src/ComponentIntelligence/Electrical/Domain/ArchivedCableTemplate.cs`
- Modify: `src/ComponentIntelligence/Electrical/Schematic/ArchivedCableInstanceFactory.cs`
- Create tests: `tests/ComponentIntelligence.Tests/Electrical/CableManufacturingEditorTests.cs`

**Interfaces:** `CableManufacturingEditorService.Prepare(ArchivedCableTemplate template) : CableManufacturingDraft`; `Apply(ArchivedCableTemplate template, CableManufacturingDraft draft) : ArchivedCableTemplate`; `Validate(CableManufacturingDraft draft) : IReadOnlyList<string>`. Draft end choices reference existing `ConnectorDefinition` and exact source Port/Pin IDs. Complete rows produce existing `CablePinMapping`; incomplete rows/explicit per-Pin usage persist as draft metadata, not a second confirmed pair graph. Table order/layout is separate presentation metadata.

- [ ] Write failing tests: `PinCountCreatesPinsWithoutMapping`, `BlankTargetIsNotNc`, `ExplicitNcAndUnusedRemainDistinct`, `MappedPinRemovalRejected`, `UnknownConnectorRemainsDraft`, `MappingEditRevokesOnlyEditedConfirmation`, `YAppearanceDoesNotShortBranches`.
- [ ] Run `--filter CableManufacturingEditorTests` and verify expected failures.
- [ ] Implement clone-only draft editing, source-ID selection, explicit confirmation evidence, and conflict reports before removing Pins. Define and validate table projection from canonical pairs; no automatic 1-to-1 map. Preserve multi-end identities even while the primary editor starts with P1/P2 groups.
- [ ] Run new tests plus `ArchivedCableInstanceTests` and `CableConstructionAuthorityTests`.
- [ ] Review and commit only Task 3 paths.

## Task 4: Reachable Library Import And Table Editor

**Files:**
- Create: `src/ComponentIntelligence.Desktop/CableArchiveEditorDialog.cs`
- Create: `src/ComponentIntelligence.Desktop/CableManufacturingTableEditor.cs`
- Modify: `src/ComponentIntelligence.Desktop/SchematicWorkspaceControl.CableLibrary.cs`
- Modify: `src/ComponentIntelligence.Desktop/SchematicWorkspaceControl.CableProperties.cs`
- Modify: `src/ComponentIntelligence.Desktop/SchematicWorkspaceControl.CableBindings.cs`
- Modify: `src/ComponentIntelligence/Electrical/Schematic/SchematicAuthoringService.Cables.cs`
- Create tests: `tests/ComponentIntelligence.Tests/Electrical/CableManufacturingInstanceEditTests.cs`

**Interfaces:** `CableArchiveEditorDialog` accepts the archive repository/catalog and returns a saved candidate only after explicit save. Library import is available even with zero templates. `CableManufacturingTableEditor` stages `CableManufacturingDraft` using connector and exact Pin dropdowns. Extend instance edit commands to accept a validated manufacturing draft and retain existing `SetArchivedCableDetails` compatibility.

- [ ] Write failing tests: `InstanceDraftEditDoesNotModifyTemplateOrSibling`, `ConnectedPinRemovalRejectedAtomically`, `CancelledTableEditChangesNothing`, `IncompleteTableRowsRoundTrip`, `AllNewCommandsHaveNonConflictingShortcuts`.
- [ ] Run the new filter and verify expected failures.
- [ ] Implement explicit wiring/sketch file selectors, CAD-role region/block preview, units, contacts, connector choices, four table columns, draft gaps and evidence fields. Commit pending WPF grid edits before save; cancelled file/dialog operations do not archive. Do not rely on a conductor picker to create an imported custom cable.
- [ ] Build Desktop and run new tests plus `SchematicShortcutTests` and `ArchivedCableInstanceTests`. Verify reachability/binding statically; owner performs mouse UAT.
- [ ] Review and commit only Task 4 paths.

## Task 5: Instance-Pinned Manufacturing Details And Shared Rendering

**Files:**
- Modify: `src/ComponentIntelligence/Electrical/Domain/ArchivedCableTemplate.cs`
- Modify: `src/ComponentIntelligence/Electrical/Schematic/SchematicDocument.cs`
- Modify: `src/ComponentIntelligence/Electrical/Schematic/SchematicAuthoringService.Cables.cs`
- Modify: `src/ComponentIntelligence/Electrical/Schematic/SchematicCableDetailService.cs`
- Modify: `src/ComponentIntelligence.Desktop/SchematicWorkspaceControl.Cables.cs`
- Modify: `src/ComponentIntelligence.Desktop/SchematicWorkspaceControl.xaml.cs`
- Modify: `src/ComponentIntelligence/Electrical/Schematic/SchematicDxfExporter.cs`
- Create tests: `tests/ComponentIntelligence.Tests/Electrical/ArchivedCableManufacturingDetailTests.cs`

**Interfaces:** `SchematicCableDetailService.AddArchivedDetail(ElectricalProject project, string cableInstanceId, string? templatePageId = null) : ElectricalProject` creates a detail without requiring existing external connections. Save optional sketch/table positions in the detail binding and instance-pinned role geometry. `Build(project, page)` keeps its existing primitive/diagnostic return contract. Add same-sheet detail placement referencing an existing cable; no independent cable clone.

- [ ] Write failing tests: `UnwiredArchivedCableHasDraftDetail`, `TwoViewsCountOneCable`, `ReferenceLengthSpecificationUpdateBothViews`, `DetailGeometryHasNoConnectableAnchors`, `RemovingDetailDoesNotRemoveCableOrConnections`, `MoveDetailDoesNotChangePhysicalLength`, `OverflowIsNotSilent`, `LongTextFitsOrIsDiagnosed`.
- [ ] Run the new filter and verify expected failures.
- [ ] Build table/sketch primitives from the saved instance using existing text substitutions. Retain the legacy connection-derived detail path. Reuse these primitives for canvas/export/print and clearly emit draft issues. Support independent table/sketch placement with persisted sheet coordinates.
- [ ] Run new tests plus `SchematicCableDetailTests`, `SchematicPrintSafetyTests`, `SchematicDxfExporterTests`.
- [ ] Review and commit only Task 5 paths.

## Task 6: Persistence, PDF Regression And Owner Test Package

**Files:**
- Create tests: `tests/ComponentIntelligence.Tests/Electrical/CableManufacturingRoundTripTests.cs`
- Extend test: `tests/ComponentIntelligence.Tests/Electrical/SchematicPrintIntegrationTests.cs`
- Update this plan with verified completion/evidence, not predictions.

**Interfaces:** Use the existing project snapshot/reload path and shared PDF/print renderer; no new project repository. Embedded manufacturing snapshots must allow rendering without the original CAD or central archive.

- [ ] Write failing tests: `OldProjectLoadsWithoutManufacturingFields`, `NewProjectReloadsWithAllDraftRowsAndSelections`, `ArchiveRelocationDoesNotChangeSavedGeometry`, `ChangedTemplateDoesNotUpgradeExistingProject`, `MultipagePdfContainsTableSketchFrameAndDraftStatus`, `FailedExportDoesNotMutateProject`.
- [ ] Run the new test filters and verify expected failures where behavior is not yet implemented.
- [ ] Complete serialization/validation wiring. Generate synthetic, non-company CAD fixtures and a sample project for repeatable tests. Run focused tests and the complete suite; record actual counts, warnings and unsupported cases.
- [ ] Obtain independent read-only review of changed product files, fix findings, rerun affected tests, and only then publish a new isolated test runtime with launch validation. Preserve AT and working data. Do not claim installer or ordinary mouse acceptance passed.
- [ ] Commit/push only reviewed source changes under existing owner authorization; attach any created/continued PR. Provide the new test entry and a short owner UAT checklist.

## Execution Environment Note

The repository has `.agent-orchestrator.json` with `enabled: true`. The instructed
Python bridge invocation currently fails with `ModuleNotFoundError` before a
request can execute. Native product mutation requires separate owner authorization
and a native-fallback receipt; do not disable the setting, install a paid service,
or mislabel native changes as Orchestrator-approved work.
