# Component Intelligence Archive Architecture v1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the existing Component Intelligence archive policy into a portable, GitHub-governed Skill + Authority + deterministic Validator + archive contracts/evals that any capable AI can use without relying on chat history.

**Architecture:** Keep engineering policy canonical in GitHub, put procedure in a thin provider-neutral Skill, enforce machine-checkable invariants in a .NET 8 validator, and keep the actual Components / Ports / Pins / Documents archive in Google Drive. Promote only the accepted archive-governance documents from the historical development branch; do not merge unrelated branch code.

**Tech Stack:** Markdown, JSON Schema 2020-12, .NET 8, System.Text.Json, xUnit 2.9.2, existing ComponentIntelligence solution/CLI.

**Spec:** `docs/superpowers/specs/2026-10-01-component-intelligence-archive-architecture-v1-design.md`

## Global Constraints

- `main` becomes the durable archive-governance discovery branch after merge.
- Google Drive remains the central component-data/document archive; GitHub does not become a second component database.
- Policy is defined once in the selected archive specification; Skill and Validator may reference but must not redefine policy.
- The runtime/model is replaceable; no implementation may require ChatGPT memory or one vendor-specific model.
- No NDA/confidential documents, credentials, OAuth tokens, private company files, or archive secrets may be added to the public repository.
- No new paid API or external service is introduced.
- The accepted `COMPONENT_ARCHIVE_SPEC_V2.md` content is promoted without semantic alteration.
- Existing unrelated production behavior must remain unchanged except for the new archive-validation CLI entry point and README documentation.

## Review Focus

- A changeset that claims `TopologyStatus=Ready` while a known `PinCount` is incomplete must be rejected, not downgraded silently.
- Duplicate IDs differing only by case must be treated as duplicates.
- Windows absolute paths and rooted paths must be rejected while valid `Documents/<Manufacturer>/<Model>/...` relative paths remain valid.
- `NC` / `Reserved` pins without evidence must surface review/error semantics rather than being accepted as verified fact.
- A runtime with validate/read capability but no write capability must end as `VALIDATED_NOT_WRITTEN`, never `COMPLETE`.

---

### Task 1: Promote archive authority documents and establish governance entry points

**Files:**
- Create from accepted historical branch content: `docs/COMPONENT_ARCHIVE_SPEC_V2.md`
- Create from accepted historical branch content: `docs/CENTRAL_WORKBOOK_KNOWLEDGE_V1.md`
- Create from accepted historical branch content: `docs/TOPOLOGY_ENDPOINT_ROUTING_V2.md`
- Replace with accepted historical branch content: `docs/VENDOR-PART-INTAKE-V1.md`
- Create: `docs/archive/AUTHORITY.md`
- Create: `docs/archive/ARCHITECTURE.md`
- Create: `docs/archive/CHANGE_POLICY.md`

**Interfaces:**
- Consumes: accepted archive-policy documents from `gpt/topology-direction-layout-fixes`.
- Produces: stable `main`-ready authority graph that later Skill, Validator, manifest, and README reference.

- [ ] **Step 1: Add document-presence/governance tests**

Create `tests/ComponentIntelligence.Tests/Archive/ArchiveGovernanceDocumentTests.cs` with tests that locate the repository root and assert:
- `docs/archive/AUTHORITY.md` exists;
- the authority file declares `main`;
- the selected archive policy path is `docs/COMPONENT_ARCHIVE_SPEC_V2.md`;
- supporting documents are present;
- the authority text does not select legacy Notion-only governance.

- [ ] **Step 2: Run the focused tests and verify RED**

Run:
`dotnet test tests/ComponentIntelligence.Tests/ComponentIntelligence.Tests.csproj --filter FullyQualifiedName~ArchiveGovernanceDocumentTests`

Expected: FAIL because authority documents are not present on the implementation branch yet.

- [ ] **Step 3: Promote only accepted archive-governance documents**

Copy the exact accepted content from `gpt/topology-direction-layout-fixes` for:
- `docs/COMPONENT_ARCHIVE_SPEC_V2.md`
- `docs/CENTRAL_WORKBOOK_KNOWLEDGE_V1.md`
- `docs/TOPOLOGY_ENDPOINT_ROUTING_V2.md`
- `docs/VENDOR-PART-INTAKE-V1.md`

Do not merge unrelated files from that branch.

- [ ] **Step 4: Add governance documents**

Write:
- `AUTHORITY.md`: short authority selection/precedence, `main` as production discovery branch after merge, archive-spec version, supporting docs, legacy status.
- `ARCHITECTURE.md`: canonical `AI → Skill → Authority → Validator → Archive` responsibility boundaries.
- `CHANGE_POLICY.md`: policy/Skill/Validator change rules and migration/eval obligations.

- [ ] **Step 5: Run focused tests and verify GREEN**

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add docs/ tests/ComponentIntelligence.Tests/Archive/ArchiveGovernanceDocumentTests.cs
git commit -m "docs: establish archive authority governance"
```

### Task 2: Add portable Skill, tool contract, continuity, and AI bootstrap docs

**Files:**
- Create: `skills/component-intelligence-archive/SKILL.md`
- Create: `skills/component-intelligence-archive/references/TOOL_CONTRACT.md`
- Create: `docs/archive/AI_PORTABILITY.md`
- Create: `docs/archive/DATA_CONTINUITY.md`

**Interfaces:**
- Consumes: Task 1 authority graph.
- Produces: provider-neutral operational procedure and capability contract usable by ChatGPT, Codex, Claude, local agents, or future runtimes.

- [ ] **Step 1: Write Skill contract tests**

Extend `ArchiveGovernanceDocumentTests.cs` to assert:
- Skill frontmatter contains valid `name` and trigger-only `description`;
- Skill points to `docs/archive/AUTHORITY.md`;
- Skill contains CREATE / UPDATE / REVIEW lifecycle, validate-before-write, readback, and no-write terminal behavior;
- Skill does not embed a second copy of the full archive policy;
- portability document contains the ten-step bootstrap flow.

- [ ] **Step 2: Run tests and verify RED**

Expected: FAIL because Skill/portability files do not exist.

- [ ] **Step 3: Implement `SKILL.md`**

Use a thin procedure:
`resolve authority → identity → archive lookup → CREATE/UPDATE/REVIEW → exact-model evidence → changeset → validate → authorized write → readback → sync → report`.

Keep engineering semantics in the authoritative spec rather than duplicating them.

- [ ] **Step 4: Implement provider-neutral tool contract**

Define capability classes:
`AuthorityRead`, `ArchiveLookup`, `EvidenceSearch`, `EvidenceRead`, `ChangesetBuild`, `Validate`, `ArchiveWrite`, `Readback`, `ArchiveSync`.

- [ ] **Step 5: Implement portability/continuity docs**

Document:
- bootstrap sequence for unfamiliar AI;
- Google Drive vs GitHub responsibility;
- no dependency on historical conversations;
- write/readback/sync failure semantics;
- stable-ID continuity across provider migration.

- [ ] **Step 6: Run focused tests and verify GREEN**

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add skills/ docs/archive/ tests/ComponentIntelligence.Tests/Archive/ArchiveGovernanceDocumentTests.cs
git commit -m "feat: add portable component archive skill"
```

### Task 3: Define machine-readable archive contracts

**Files:**
- Create: `archive/contracts/archive-changeset.schema.json`
- Create: `archive/contracts/validation-result.schema.json`
- Create: `archive/contracts/archive-readback.schema.json`
- Create: `src/ComponentIntelligence/Archive/ArchiveContracts.cs`
- Create: `tests/ComponentIntelligence.Tests/Archive/ArchiveContractTests.cs`

**Interfaces:**
- Consumes: authoritative field semantics from Task 1.
- Produces:
  - `ArchiveChangeSet`
  - `ArchiveComponentChange`
  - `ArchivePortChange`
  - `ArchivePinChange`
  - `ArchiveEvidenceReference`
  - `ArchiveValidationReport`
  - `ArchiveValidationIssue`
  - `ArchiveReadbackResult`
  - JSON Schema contracts matching those concepts.

- [ ] **Step 1: Write failing contract tests**

Tests must assert:
- all three JSON schema files parse as JSON;
- a minimal valid CREATE changeset deserializes;
- enum values round-trip as strings;
- missing Manufacturer or Model cannot produce a valid domain changeset after validation;
- report/readback statuses serialize to the exact documented values.

- [ ] **Step 2: Run focused tests and verify RED**

Run:
`dotnet test tests/ComponentIntelligence.Tests/ComponentIntelligence.Tests.csproj --filter FullyQualifiedName~ArchiveContractTests`

Expected: FAIL because contracts do not exist.

- [ ] **Step 3: Implement typed .NET contracts**

Create focused immutable records/enums under `ComponentIntelligence.Archive`. Use `System.Text.Json` only; add no package dependency.

- [ ] **Step 4: Implement JSON Schema 2020-12 files**

Schemas must encode required identity fields, operation/status enums, nested Component/Port/Pin changes, evidence references, validation issue structure, and readback structure.

- [ ] **Step 5: Run focused tests and verify GREEN**

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add archive/contracts/ src/ComponentIntelligence/Archive/ArchiveContracts.cs tests/ComponentIntelligence.Tests/Archive/ArchiveContractTests.cs
git commit -m "feat: define archive interchange contracts"
```

### Task 4: Implement deterministic Archive Validator and JSON runner

**Files:**
- Create: `src/ComponentIntelligence/Archive/ArchiveChangeSetValidator.cs`
- Create: `src/ComponentIntelligence/Archive/ArchiveValidationJsonRunner.cs`
- Create: `archive/validator/README.md`
- Create: `archive/validator/rules/RULES.md`
- Create: `tests/ComponentIntelligence.Tests/Archive/ArchiveChangeSetValidatorTests.cs`
- Create: `tests/ComponentIntelligence.Tests/Archive/ArchiveValidationJsonRunnerTests.cs`

**Interfaces:**
- Consumes: `ArchiveChangeSet` from Task 3.
- Produces:
  - `ArchiveValidationReport ArchiveChangeSetValidator.Validate(ArchiveChangeSet changeSet)`
  - `Task<(int ExitCode, string Json)> ArchiveValidationJsonRunner.ValidateFileAsync(string path)`

- [ ] **Step 1: Write failing validator tests**

Cover exact rule IDs and severities for:
- required Manufacturer + Model;
- case-insensitive duplicate Component/Port/Pin IDs;
- orphan Port ownership;
- orphan Pin ownership;
- known `PinCount` not equal to actual Pin rows;
- duplicate PinNumber/contact identifier within one Port;
- `NC`/Reserved with no evidence reference;
- invalid absolute/rooted archive paths;
- CREATE with an indicated pre-existing component;
- stable-ID mutation when prior stable ID is supplied;
- `TopologyStatus=Ready` with pin-completeness error;
- valid reference case changeset returns PASS.

- [ ] **Step 2: Run tests and verify RED**

Expected: FAIL because validator is missing.

- [ ] **Step 3: Implement minimal validator**

Use deterministic checks only. Return machine-readable rule IDs and severities `ERROR`, `REVIEW`, `WARNING`, `INFO`; derive report status `PASS`, `REJECT`, or `REVIEW_REQUIRED`.

Do not claim to verify completeness of undiscovered Ports or exact-model evidence applicability.

- [ ] **Step 4: Run validator tests and verify GREEN**

Expected: PASS.

- [ ] **Step 5: Write failing JSON runner tests**

Test valid JSON, invalid JSON, missing file, PASS, REVIEW, and REJECT exit mappings.

- [ ] **Step 6: Implement JSON runner**

Deserialize with string enums, run validator, serialize report, and return deterministic exit codes:
- 0 = PASS
- 2 = REVIEW_REQUIRED
- 3 = REJECT
- 64 = input/usage problem

- [ ] **Step 7: Run runner tests and verify GREEN**

Expected: PASS.

- [ ] **Step 8: Document validator rule mapping**

`RULES.md` must map each implemented rule ID back to the authoritative archive-spec section and clearly state the Validator does not invent policy.

- [ ] **Step 9: Commit**

```bash
git add src/ComponentIntelligence/Archive/ archive/validator/ tests/ComponentIntelligence.Tests/Archive/
git commit -m "feat: add deterministic archive validator"
```

### Task 5: Expose archive validation through the existing CLI

**Files:**
- Modify: `src/ComponentIntelligence.Cli/Program.cs`
- Create: `tests/ComponentIntelligence.Tests/Archive/ArchiveCliContractTests.cs`

**Interfaces:**
- Consumes: `ArchiveValidationJsonRunner.ValidateFileAsync(path)`.
- Produces CLI command:
  `dotnet run --project src/ComponentIntelligence.Cli/ComponentIntelligence.Cli.csproj -- archive-validate <changeset.json>`

- [ ] **Step 1: Write failing CLI contract test**

Test the help text/command dispatch through a small callable helper factored from `Program`, without introducing a second CLI test project.

- [ ] **Step 2: Run focused test and verify RED**

Expected: FAIL because command does not exist.

- [ ] **Step 3: Add `archive-validate` command**

Wire to the JSON runner; preserve all existing CLI commands and output behavior.

- [ ] **Step 4: Run focused test and verify GREEN**

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ComponentIntelligence.Cli/Program.cs tests/ComponentIntelligence.Tests/Archive/ArchiveCliContractTests.cs
git commit -m "feat: expose archive validation CLI"
```

### Task 6: Add regression/eval corpus and central archive manifest

**Files:**
- Create: `archive/evals/README.md`
- Create: `archive/evals/OMRON_F03-20/case.json`
- Create: `archive/evals/OMRON_K7L-AT50DP/case.json`
- Create: `archive/evals/IFM_AL1342/case.json`
- Create: `archive/evals/IFM_AL5021/case.json`
- Create: `archive/manifests/CENTRAL_ARCHIVE.md`
- Create: `archive/service/README.md`
- Create: `tests/ComponentIntelligence.Tests/Archive/ArchiveEvalTests.cs`

**Interfaces:**
- Consumes: Validator and authority semantics.
- Produces: non-secret deterministic regression fixtures and a reconnectable description of the Google Drive archive/service boundary.

- [ ] **Step 1: Write failing eval tests**

Tests load all four `case.json` files and assert each declares:
- reference component;
- policy focus;
- expected validator status;
- expected key assertions.

Also run validator-backed fixtures where applicable.

- [ ] **Step 2: Run focused tests and verify RED**

Expected: FAIL because eval corpus does not exist.

- [ ] **Step 3: Add four reference eval cases**

Cover:
- F03-20: functional Input/Output role may coexist with electrical Passive direction;
- K7L-AT50DP: SENSING is Mixed and Pins endpoint mode;
- AL1342: M12 A-coded IO-Link ports remain Mixed, Connector endpoint mode, all underlying Pins retained;
- AL5021: fixed 18-wire field cable uses Pins endpoint mode and exposes all conductors.

Use only non-confidential/synthetic fixture data needed to test the rule; do not pretend fixtures replace live evidence verification.

- [ ] **Step 4: Add central archive manifest**

Document logical archive name, workbook/sheet names, document root layout, editable/read surfaces, sync requirement, connection verification, and secret-exclusion rules. Do not place authentication secrets or confidential documents in GitHub.

- [ ] **Step 5: Add service boundary README**

Document deterministic future operations:
`lookup_component`, `prepare_changeset`, `validate_changeset`, `commit_changeset`, `readback_changeset`, `sync_archive`.

State that v1 ships validation locally; write automation remains a capability boundary until a runtime provides authorized write tools.

- [ ] **Step 6: Run eval tests and verify GREEN**

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add archive/evals/ archive/manifests/ archive/service/ tests/ComponentIntelligence.Tests/Archive/ArchiveEvalTests.cs
git commit -m "test: add archive reference eval corpus"
```

### Task 7: Rewrite README as the human/AI entry point and verify the whole branch

**Files:**
- Modify: `README.md`
- Modify if necessary for link correctness only: archive docs created in earlier tasks.

**Interfaces:**
- Consumes: every artifact from Tasks 1-6.
- Produces: one reviewable README that lets the Product Owner detect wrong architecture, wrong authority, missing pieces, or false completion claims.

- [ ] **Step 1: Add README acceptance assertions**

Extend `ArchiveGovernanceDocumentTests.cs` to assert README contains:
- `Chat/AI → Skill → Authority → Validator → Archive`;
- exact paths to `AI_PORTABILITY.md`, `AUTHORITY.md`, `SKILL.md`, archive spec, validator, contracts, evals, and manifest;
- statement that Google Drive contains actual central archive data;
- statement that GitHub stores governance/capability, not a second component database;
- CLI validation example;
- status table distinguishing implemented v1 from future ArchiveWrite automation.

- [ ] **Step 2: Run focused tests and verify RED**

Expected: FAIL against the old MVP-only README.

- [ ] **Step 3: Rewrite README**

Keep the existing runnable-MVP information, but add a prominent `Component Intelligence Archive Capability` section near the top:
- purpose and architecture;
- START HERE links for AI and humans;
- authority;
- repository layout;
- how ChatGPT/other AI should use it;
- what Validator guarantees and cannot guarantee;
- current central-archive location contract;
- `archive-validate` example;
- implementation status;
- data continuity and security boundaries;
- existing build/test commands.

Do not claim automatic central writes exist if only validation is implemented.

- [ ] **Step 4: Run all archive-focused tests**

Run:
`dotnet test tests/ComponentIntelligence.Tests/ComponentIntelligence.Tests.csproj --filter FullyQualifiedName~Archive`

Expected: all Archive tests PASS.

- [ ] **Step 5: Run full build**

Run:
`dotnet build ComponentIntelligence.sln --no-restore`

Expected: exit 0.

- [ ] **Step 6: Run full test suite**

Run:
`dotnet test ComponentIntelligence.sln --no-build`

Expected: 0 failed.

- [ ] **Step 7: Verify repository diff**

Confirm the branch contains only intended archive-governance/validator/README changes and the prior design/plan docs; no unrelated development-branch source files are present.

- [ ] **Step 8: Commit**

```bash
git add README.md tests/ComponentIntelligence.Tests/Archive/
git commit -m "docs: make archive capability discoverable"
```

- [ ] **Step 9: Final branch review**

Use a fresh review pass focused on:
- authority drift;
- duplicated policy;
- false claims in README;
- validator rules unsupported by the spec;
- portability leaks that assume ChatGPT-specific behavior.

