# Connector Mating and Function Mapping Implementation Plan

> For agentic workers: Use superpowers:executing-plans. Execute inline with one read-only final reviewer.

**Goal:** Mate CAD cable heads and propagate exact-Pin functions without guessing continuity.

**Architecture:** Add explicit mapped DirectMating to the existing connection model.
Use a single exact-Pin connectivity projection for inferred functions and review.
Keep declared Pin functions and archive templates untouched by inference.

**Tech Stack:** .NET 8, C#, WPF, xUnit, SQLite, existing PDF exporter.

**Spec:** ../specs/2026-10-13-connector-mating-function-mapping-design.md

## Global Constraints

- No AO, central workbook changes, original DWG edits or active-window operations.
- No added physical cable or AWG requirement for DirectMating.
- Never infer conductivity from a whole Port or screen order.
- Preserve manual declarations and unknown mapping as pending review.
- Preserve all unrelated dirty files.

## Review Focus

- Persisted malformed or legacy mates cannot silently imply Pin continuity.
- Mated face Pins cannot receive additional external wires.
- Source changes, conflicts, and disconnection must clear obsolete inference.
- Opening and saving a manufacturing editor cannot turn inference into a source.
- Graph-wide reconciliation must not execute per drag pointer sample.

### Task 1: Explicit connector mating and exact-Pin connectivity

Files: domain connection metadata; Topology/ConnectorMatingService.cs;
Connectivity/TerminalConnectivityEngine.cs; topology endpoint capacity;
Schematic/SchematicAuthoringService.Mating.cs; tests/Electrical/ConnectorMatingTests.cs.

Interface: Prepare(project, fromPortId, toPortId) returns Pin pairs by actual number;
Connect(project, ports, pairs, evidence) creates one DirectMating; Validate checks
persisted maps; conductive edges expand only valid confirmed maps.

- [x] Write failing tests for CAD mates, distinct nets, counts/coding/gender,
      complete maps, repeated faces, and no extra cable/BOM/gauge.
- [x] Run focused tests RED, implement minimal services, run GREEN.

### Task 2: Project function inference and review

Files: ComponentPin project-only inference; Connectivity/ProjectPinFunctionService.cs;
validator, persistence migrator/repository; schematic edit boundary;
wire contacts, manufacturing effective-template projection; focused tests.

Interface: Synchronize(project) recomputes inferred functions; Inspect(project)
returns conflict/missing/pending issues; SetOverride(project, pinId, value, manual)
preserves blank overrides and reset-to-auto.

- [x] Write and run failing propagation, conflict, reset/disconnect, manufacturing
      no-op and SQLite round-trip tests.
- [x] Implement and run GREEN, then full suite.

### Task 3: WPF interaction, review, export and delivery

Files: schematic XAML and a focused Mating partial; preview/review dialogs;
synthetic WPF smoke; validation receipts and isolated runtime bundle.

- [x] Write WPF assertions for mating mode, preview, delete/undo, inferred labels.
- [x] Add compact icon commands and mapping preview, manual override and review.
- [x] Run WPF smoke with screenshots, existing PDF/wire/workspace regressions.
- [x] Run independent review, address important findings with RED/GREEN tests.
- [x] Build/publish an isolated verified runtime; never replace the active runtime.
