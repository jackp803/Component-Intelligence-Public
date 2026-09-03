# CP3-C Component Executor Integration — Source Handoff

Date: 2026-09-03
Task: `E5-20260903-023`
Worker: `E5`
Disposition: `SOURCE_SCOPE_COMPLETE_PENDING_PM_DISPATCHED_CODEX_CP3C_TECHNICAL_VERIFICATION`

## Authority and lineage

Repository: `jackp803/Component-Intelligence-Public`

Original provisional CP3-B candidate authorized by the task:

```text
head = bb3cd23951b7c835e8005ac2be9cf0f1a496273b
tree = 15a06c6ce76022bdfa68161309eae5e9098166c0
```

PM-admitted bounded CP3-B repair:

```text
head = cc69a62c4d769672c228b90b66f8d24d67bb2fb5
tree = 3c0cb2de1ba99ad855890bd0f4461ca6cb960a59
```

PM-admitted Codex verification wrapper / final Draft-PR base:

```text
branch = codex/cp3b-mvp-first-technical-verification-20260903
head = 56345a248d1b0c44df17aa85cb034353a2cb2f5c
```

E5 CP3-C branch:

```text
branch = agent/e5-cp3c-autocad-executor-client-20260903
```

Final technical source candidate immediately before this handoff wrapper:

```text
head = 7cad9c6043882e33a8c7da4956348e9409d28b15
tree = 0f300a24f8703d93f2f21cb6dc66a1500d9f16d0
```

That commit is a traceable two-parent reconciliation of the E5 CP3-C source lineage with the admitted Codex verification wrapper. Earlier E5 reconciliation also incorporated the exact bounded repair head. The post-handoff branch head/tree is recorded in the CP3-C Draft PR and `coordination/E5/STATUS.md` because this file cannot self-reference its own commit identity.

## CP3-B execution-sufficiency mirror

The Component Drawing Plan mirror was updated so the persistent C# contract can round-trip the Product Owner-approved CP3-B execution-sufficiency amendments without dropping authority:

- every `DrawingRoute` carries explicit `PageId`;
- cross-page relations carry explicit nullable `SourcePageId`, `DestinationPageId`, `SourceRouteId`, and `DestinationRouteId` fields;
- continuation relations pin exact source/destination page and route identities;
- C# validation rejects unknown page references and invalid execution identity;
- presentation editing preserves route engineering identity and page ownership rather than deriving it from geometry.

No Component code infers page ownership, reroutes geometry, invents topology, creates junctions from crossings, or infers Pins/Core mappings.

Final CP3-B PM acceptance is not claimed by E5; amended cross-repository behavior requires bounded re-verification and PM disposition.

## CP3-C Component integration

### Typed executor result contract

`DrawingGenerationContracts.cs` adds:

```text
DrawingExecutorStatus = Blocked / Failed / Applied
DrawingGenerationStatus += Applied / ExecutionFailed
DrawingExecutorResult = status + stagingRoot + projectFile + pageDrawings + executionEvidenceHash + issues + raw JSON
```

`DwgOrWdpGenerated` becomes true only when the generation result is typed `Applied`, the executor result is typed `Applied`, and required WDP/DWG outputs are present. `READY_FOR_CP3C`, mocks, dry-run state, or source inspection can never set the generated-output flag.

### User-local executor runtime settings

`DrawingExecutorRuntimeSettings.cs` persists exactly six user-local fields, separately from engineering project truth:

```text
pythonExecutable
automationRoot
accoreConsolePath
stagingRoot
projectBaselineWdp
drawingTemplatePath
```

Validation requires the actual local files/directories and `tools/electrical_cp3c_executor.py`, checks `.wdp` / `.dwt` extensions, and rejects staging overlap with the automation implementation root or formal project-baseline directory. Invalid settings are not persisted. No example Product Owner path is automatically written.

### Real local executor client

`LocalDrawingExecutorClient.cs`:

- accepts only READY, hashed Drawing IR;
- writes the exact `DrawingIrDocument.RawJson` and user-local executor settings to disposable temp files;
- invokes the CP3-C Python CLI using `IDrawingProcessRunner` argument vectors, not shell concatenation:

```text
<pythonExecutable>
<automationRoot>/tools/electrical_cp3c_executor.py
execute
--drawing-ir <temp>
--runtime-config <temp>
--output-result <temp>
```

- includes the active Component Intelligence SQLite path as a protected runtime path;
- parses only `electrical-execution-result.v1`;
- requires exact `sourceDrawingIrHash` match;
- rejects nonzero-process APPLIED claims;
- accepts typed APPLIED only with WDP, DWG inventory and evidence hash;
- deletes temp input/result workspace in `finally`.

The Python side seals this six-field Component runtime into the existing `electrical-staging-runtime-config.v1` safety contract and turns explicit IR asset path/hash pairs into immutable execution references. Safety authority is not duplicated or weakened in C#.

### Generation coordinator gate

`DrawingGenerationCoordinator` keeps one generation path:

```text
preflight
→ Drawing Plan
→ READY Drawing IR
→ if executor is null: READY_FOR_CP3C
→ else execute typed CP3-C client
```

- blocked preflight never calls the executor;
- BLOCKED/unhashed IR never calls the executor;
- no executor configured returns `READY_FOR_CP3C` and does not claim DWG/WDP;
- typed executor APPLIED with outputs returns `Applied`;
- executor BLOCKED/FAILED/exception returns `ExecutionFailed` and does not claim generated outputs.

### Drawing Planning WPF integration

The existing `Drawing Planning｜圖面規劃` workspace now:

- labels the action `Generate AutoCAD Electrical`;
- stores CP3-C runtime settings in the user-local profile, separately from planning/engineering state;
- injects a `LocalDrawingExecutorClient` only when CP3-C settings exist and validate;
- leaves the prior `READY_FOR_CP3C` behavior intact if no executor is configured;
- shows actionable failure issues without APPLIED wording;
- shows the staging WDP path only after typed APPLIED;
- enables Open Output Folder / Open Project only after typed APPLIED;
- explicitly displays `APPLIED != VERIFIED` and does not claim CP3-D Trusted Readback.

## Selected durable phase commits

Key branch commits include:

```text
00466eeff382d1c4d4a80cb0739b9f654a4fe012  Task 0 Component baseline attestation
f95fc71b...                                      admitted CP3-B repair reconciliation (earlier two-parent merge)
ef618d1fbe266037ef93eb4cb113feee217d1305  final page-local Drawing Plan mirror validation
c0423a7d4e72654f9359375c8fbcb08717a8b967  executor client/settings test-first contract
8cdcaabe7cbed5dc0e77c09d356c71665bcaa2b8  validated executor runtime settings
53290d847afe882af18e7f7e7cd9a86795514da3  local executor client
d24c00af45580ec94cdd2120542a74dfbd5f9ed7  typed executor generation contracts
689819d6508aff896903f23f76d10e67e496b408  coordinator execution gate
02449bb0fc2948373cb33d3653b17d9fc4cf4ca7  generation-gate regression source
1f6f3e2bc9b181e5eea483a9557adf57eaab4baa  staging output WPF actions
e918888ccefb5172f5d87f7e7448b4244397a9ab  APPLIED / failure UI states
811654b787ea01923ee0daf5e8f06fa494f67980  real executor injection into workspace
4ab687360b52bdadb7a71aeb6834829a5a2a7d92  final executor fixture safety correction
7cad9c6043882e33a8c7da4956348e9409d28b15  verification-wrapper reconciliation
```

The abbreviated earlier repair-merge identifier is historical orientation only; exact repair identity is the full PM-admitted `cc69a62c4d769672c228b90b66f8d24d67bb2fb5` above, and its ancestry is preserved by the CP3-C branch. Final acceptance should rely on Git ancestry/read-back, not the abbreviated display string.

## Changed-file scope from admitted wrapper base

Fresh GitHub compare `56345a248d1b0c44df17aa85cb034353a2cb2f5c → 7cad9c6043882e33a8c7da4956348e9409d28b15` is ahead-only (`17` commits, `0` behind) and contains 14 changed files, restricted to the CP3-B contract mirror, CP3-C executor integration, WPF generation integration, tests, and baseline attestation. No existing CP3-A/CP3-B PR was modified or retargeted.

Changed scope:

```text
docs/handoff/CP3C_COMPONENT_BASELINE_ATTESTATION.md
src/ComponentIntelligence.Desktop/DrawingPlanningWorkspaceControl.Generation.cs
src/ComponentIntelligence.Desktop/DrawingPlanningWorkspaceControl.xaml
src/ComponentIntelligence.Desktop/ElectricalWorkspaceWindow.DrawingPlanning.cs
src/ComponentIntelligence/Electrical/Drawing/DrawingExecutorRuntimeSettings.cs
src/ComponentIntelligence/Electrical/Drawing/DrawingGenerationContracts.cs
src/ComponentIntelligence/Electrical/Drawing/DrawingGenerationCoordinator.cs
src/ComponentIntelligence/Electrical/Drawing/DrawingPlanContracts.cs
src/ComponentIntelligence/Electrical/Drawing/DrawingPlanJson.cs
src/ComponentIntelligence/Electrical/Drawing/LocalDrawingExecutorClient.cs
tests/ComponentIntelligence.Tests/Electrical/DrawingExecutorClientTests.cs
tests/ComponentIntelligence.Tests/Electrical/DrawingGenerationGateTests.cs
tests/ComponentIntelligence.Tests/Electrical/DrawingPlanExecutionSufficiencyTests.cs
tests/ComponentIntelligence.Tests/Electrical/DrawingPlanPersistenceAndRevisionTests.cs
```

## Authored regression source

Focused test source covers:

- exact six-field executor settings validation and invalid-settings no-write behavior;
- exact Python executor argv and disposable temp input cleanup;
- production SQLite protected-path propagation;
- typed APPLIED result parsing;
- READY IR + APPLIED executor → `Applied` with output paths;
- READY IR + failed executor → `ExecutionFailed` without output claim;
- no executor → `READY_FOR_CP3C` without generated output;
- blocked preflight → executor call count zero;
- BLOCKED IR → executor call count zero;
- page-local route / continuation persistent mirror semantics.

## Verification truth

This E5 chat does not have the approved exact local repository / Windows / .NET / WPF execution binding. Exact local repository clone attempts were unavailable because the environment could not resolve GitHub directly; copied/reconstructed source would not qualify as candidate verification. No qualifying GitHub Actions run exists for this candidate.

Therefore the following are exactly `NOT_RUN` for this E5 source candidate:

```text
dotnet test tests/ComponentIntelligence.Tests/ComponentIntelligence.Tests.csproj -c Release --filter "FullyQualifiedName~DrawingExecutorClientTests|FullyQualifiedName~DrawingGenerationGateTests"
dotnet test tests/ComponentIntelligence.Tests/ComponentIntelligence.Tests.csproj -c Release --filter "FullyQualifiedName~Drawing"
dotnet test tests/ComponentIntelligence.Tests/ComponentIntelligence.Tests.csproj -c Release
dotnet build src/ComponentIntelligence.Desktop/ComponentIntelligence.Desktop.csproj -c Release
git diff --check
minimal WPF CP3-C interaction smoke
real C# → Python → PowerShell → accoreconsole harness
```

Real Windows/AutoCAD physical execution is exactly:

```text
NOT_RUN / RESERVED_FOR_PM_DISPATCHED_CODEX_CP3C_TECHNICAL_VERIFICATION
```

E5 did not launch AutoCAD/accoreconsole, did not generate a physical WDP/DWG, and does not claim `APPLIED`.

## Protected state and terminal boundary

No production SQLite, formal/production WDP/DWG/DWT, formal ACADE library, Symbol Archive/source asset, central workbook, or implementation worktree output was mutated by E5.

The Component source now exposes the real CP3-C execution capability only through the typed isolated-staging gate. A future qualified physical run can return `Applied` only from a validated `electrical-execution-result.v1` with required outputs/evidence.

**`APPLIED != VERIFIED`.** CP3-D Trusted Readback was not implemented.

Product Owner visual/usability exploration remains outside this source handoff and may be deferred to the integrated MVP.
