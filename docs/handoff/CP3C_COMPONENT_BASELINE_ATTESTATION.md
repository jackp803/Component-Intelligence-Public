# CP3-C Component Executor Baseline Attestation

Task: `E5-20260903-023`
Date: `2026-09-03`
Repository: `jackp803/Component-Intelligence-Public`
Branch: `agent/e5-cp3c-autocad-executor-client-20260903`

## Exact starting identity

- provisional CP3-B head: `bb3cd23951b7c835e8005ac2be9cf0f1a496273b`
- provisional CP3-B tree: `15a06c6ce76022bdfa68161309eae5e9098166c0`
- branch was created directly from that exact commit; GitHub branch readback matched the head/tree above.
- exact local `git status --short`: `NOT_RUN / no approved local checkout or command runner in this Worker environment`.

## Existing CP3-B executor boundary

### `DrawingGenerationContracts.cs`

Current CP3-B source provides:

- generation target/status/preflight contracts;
- typed `DrawingIrDocument` with exact planning/page-plan/drawing-plan provenance hashes and raw IR JSON;
- placeholder `IDrawingExecutorClient` returning only `Task`;
- `DrawingGenerationResult.DwgOrWdpGenerated` hard-coded false.

CP3-C must replace only the executor boundary required by the approved design, not planning semantics.

### `DrawingGenerationCoordinator.cs`

Current Full Generation sequence is:

`input -> full preflight -> Drawing Plan -> READY IR -> ReadyForCp3C`

The coordinator intentionally does not call the existing placeholder executor. CP3-C will preserve all preflight/IR gates, never invoke an executor for BLOCKED preflight/IR, and call the executor only after a hash-bearing READY IR exists.

### `DrawingPipelineProcessClients.cs`

Current process infrastructure already provides:

- argv-based `ProcessStartInfo.ArgumentList` rather than shell string concatenation;
- isolated temporary JSON files for planner/IR process clients;
- exact output provenance checks;
- cleanup of temporary working directories.

CP3-C will reuse this process-runner boundary for the real local executor client.

## Integration decisions

CP3-C Component source will add:

1. `DrawingExecutorStatus { Blocked, Failed, Applied }` and typed `DrawingExecutorResult`;
2. a user-local executor runtime settings object containing exactly `pythonExecutable`, `automationRoot`, `accoreConsolePath`, `stagingRoot`, `projectBaselineWdp`, and `drawingTemplatePath`, separate from engineering project truth;
3. a `LocalDrawingExecutorClient` invoking only `tools/electrical_cp3c_executor.py execute` with explicit argv/temp files;
4. coordinator statuses `Applied` and `ExecutionFailed` while retaining `ReadyForCp3C` when no executor is configured;
5. `DwgOrWdpGenerated=true` only for a typed APPLIED result with expected staged output paths;
6. WPF integration that reports staging output/failure truth without presenting dry-run/static evidence as APPLIED.

## Verification truth at attestation

No .NET tests/build, WPF interaction, Windows process execution, AutoCAD or DWG/WDP generation was performed in this Worker environment. These remain `NOT_RUN / RESERVED_FOR_PM_DISPATCHED_CODEX_CP3C_TECHNICAL_VERIFICATION` where physical execution is required.
