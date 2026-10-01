# Component Intelligence Archive Architecture v1 — Design

Date: 2026-10-01  
Status: Design for review  
Repository: `jackp803/Component-Intelligence-Public`  
Target authoritative branch after implementation: `main`

## 1. Purpose

This design turns the existing Component Intelligence archive rules into a portable, versioned capability that can be used by ChatGPT Chat today and by other AI runtimes later without depending on chat history, one model vendor, or one UI surface.

The target execution chain is:

```text
AI / Chat / Codex / Local Agent
        ↓
Skill
        ↓
GitHub Authority
        ↓
Validator
        ↓
Archive
```

GitHub preserves the rules, procedure, contracts, validator, evals, tool requirements, and archive-location metadata. The actual central component data and archived engineering documents remain in the existing Google Drive central archive.

The system must make AI replaceable while keeping the archive contract stable.

## 2. Goals

1. Make `main` the single durable entry point for archive governance.
2. Preserve the existing archive domain rules instead of rewriting them into a second policy.
3. Add a thin operational Skill that tells an AI how to execute archive work.
4. Add machine-readable contracts between AI reasoning and deterministic validation.
5. Add a deterministic Validator for rules that can be enforced mechanically.
6. Define a portable tool contract so ChatGPT, Codex, Claude, local agents, or future runtimes can provide equivalent capabilities.
7. Preserve archive continuity when the AI, model, computer, or product surface changes.
8. Keep the Google Drive archive as the central engineering-data store.
9. Add regression/evaluation cases based on already accepted components and edge cases.
10. Ensure a future AI can bootstrap from GitHub without relying on historical conversations.

## 3. Non-goals

This design does not:

- move the central component database or engineering documents into GitHub;
- train or fine-tune a model;
- make one local model the mandatory reasoning engine;
- duplicate all archive policy inside `SKILL.md`;
- redesign the existing Components / Ports / Pins data model;
- change Topology UI rules into archive engineering facts;
- implement automatic AutoCAD drawing generation;
- make private, NDA, or company-restricted documents public;
- merge the existing large development PR as a side effect of establishing archive governance.

## 4. Existing authority and bootstrap state

The current archive policy document is:

```text
docs/COMPONENT_ARCHIVE_SPEC_V2.md
```

The document declares itself authoritative and defines source priority, identity rules, stable IDs, Components / Ports / Pins semantics, complete physical-pin rules, `TopologyEndpointMode`, evidence requirements, layout rules, readiness gates, forbidden inference, and the archive SOP.

At design time, this file is not present on `main`. The accepted content is available on the existing branch:

```text
gpt/topology-direction-layout-fixes
```

Known blob SHA at design time:

```text
00e63219387ab66b6efd09cdaa29dfdf2daff4b4
```

The implementation must promote that accepted archive-spec content to `main` without silently changing its engineering meaning. Any later semantic change must follow the change policy defined by this architecture.

This bootstrap exception ends once the archive authority files are merged into `main`.

## 5. Core architectural rule: no duplicated policy

The architecture separates four responsibilities:

| Layer | Responsibility | Canonical artifact |
|---|---|---|
| AI | Reason about the specific component and evidence | Runtime/model, replaceable |
| Skill | Procedure: when and how to perform archive work | `skills/component-intelligence-archive/SKILL.md` |
| Authority | Engineering truth and policy | `docs/archive/AUTHORITY.md` + authoritative specs |
| Validator | Deterministic enforcement | `archive/validator/` |
| Archive | Central reusable component knowledge | Google Drive central archive |

The governing principle is:

```text
Policy once.
Procedure once.
Enforcement once.
```

A policy rule is not copied in full into prompts, Custom GPT instructions, local-agent prompts, or multiple skill files. Runtime adapters may point to the canonical rule but must not redefine it.

## 6. Target repository structure

```text
jackp803/Component-Intelligence-Public/
│
├─ docs/
│  ├─ COMPONENT_ARCHIVE_SPEC_V2.md
│  │
│  └─ archive/
│     ├─ AUTHORITY.md
│     ├─ ARCHITECTURE.md
│     ├─ DATA_CONTINUITY.md
│     ├─ AI_PORTABILITY.md
│     └─ CHANGE_POLICY.md
│
├─ skills/
│  └─ component-intelligence-archive/
│     ├─ SKILL.md
│     └─ references/
│        └─ TOOL_CONTRACT.md
│
├─ archive/
│  ├─ contracts/
│  │  ├─ archive-changeset.schema.json
│  │  ├─ validation-result.schema.json
│  │  └─ archive-readback.schema.json
│  │
│  ├─ validator/
│  │  ├─ README.md
│  │  ├─ rules/
│  │  └─ tests/
│  │
│  ├─ service/
│  │  └─ README.md
│  │
│  ├─ evals/
│  │  ├─ README.md
│  │  ├─ OMRON_F03-20/
│  │  ├─ OMRON_K7L-AT50DP/
│  │  ├─ IFM_AL1342/
│  │  └─ IFM_AL5021/
│  │
│  └─ manifests/
│     └─ CENTRAL_ARCHIVE.md
│
└─ existing Component Intelligence application code
```

Directories may be introduced incrementally, but the responsibility boundaries above are normative.

## 7. GitHub Authority layer

### 7.1 `docs/archive/AUTHORITY.md`

This is the first governance document an AI reads after discovering the archive capability.

It must define:

- authoritative branch: `main`;
- current archive-policy document;
- supporting contracts and their precedence;
- current policy version;
- how conflicts are resolved;
- which historical/legacy documents are non-authoritative;
- whether a migration or temporary bootstrap exception is active.

The file must remain short enough to inspect quickly.

### 7.2 Authority precedence

After bootstrap, precedence is:

1. `docs/archive/AUTHORITY.md` for document selection and version resolution;
2. the archive specification selected by `AUTHORITY.md`;
3. current storage/runtime contracts explicitly listed as supporting authority;
4. narrower supporting specifications, such as endpoint-routing behavior;
5. legacy documents;
6. historical chat messages.

An AI must not treat chat history as a higher authority than GitHub governance.

### 7.3 `main` rule

Once this architecture is implemented:

```text
main = durable archive-governance entry point
```

Feature branches may propose new rules, but a future AI must never need to know a private branch name to discover the current production archive policy.

## 8. Skill layer

### 8.1 Purpose

The Skill is a thin operational wrapper. It does not duplicate the archive specification.

Its job is to define:

- when archive work should trigger;
- which authority documents must be read;
- the archive job lifecycle;
- tool-capability requirements;
- create/update/review decision flow;
- validation and write gates;
- failure and partial-success behavior;
- final reporting contract.

### 8.2 Skill trigger scope

The Skill should apply when an AI is asked to:

- research a new electrical/electronic/industrial component for the central archive;
- create a central component record;
- update or complete an existing component;
- audit Components / Ports / Pins;
- review archive readiness;
- repair archive inconsistencies;
- process vendor/custom-part archive intake.

It should not trigger merely because the user is discussing topology UI, coding unrelated application features, or creating a project-specific temporary BOM view.

### 8.3 Operational flow

The canonical Skill procedure is:

```text
Resolve authority
    ↓
Resolve exact Manufacturer + Model / Part Number
    ↓
Lookup current central archive
    ↓
Classify operation: CREATE / UPDATE / REVIEW
    ↓
Gather exact-model evidence
    ↓
Build candidate Component / Port / Pin changes
    ↓
Create archive changeset
    ↓
Run Validator
    ↓
PASS?
 ├─ No → repair / collect evidence / keep Review
 └─ Yes
      ↓
Write only when authorized and write capability exists
      ↓
Read back
      ↓
Verify central state
      ↓
Synchronize required archive surfaces
      ↓
Report Created / Updated / Review / Unknown / Conflict / Partial
```

If the runtime lacks write capability, the valid terminal state is a validated changeset plus an explicit statement that no central write occurred.

### 8.4 Skill portability

The Skill must avoid assumptions about one model or tool vendor. It should describe required capabilities semantically, for example:

- source search;
- URL retrieval;
- file/PDF reading;
- image/diagram interpretation when required;
- central archive lookup;
- changeset validation;
- controlled write;
- readback.

The concrete implementation may be ChatGPT tools, MCP, a local service, CLI tools, or future adapters.

## 9. Tool Contract

`skills/component-intelligence-archive/references/TOOL_CONTRACT.md` defines capability classes rather than provider-specific names.

Minimum capability classes:

1. **AuthorityRead**
   - read repository files at a declared ref;
   - identify revision/commit when possible.

2. **ArchiveLookup**
   - lookup by Manufacturer + Model;
   - fetch existing Component, Ports, Pins, status, and stable IDs.

3. **EvidenceSearch**
   - search for exact-model manufacturer sources.

4. **EvidenceRead**
   - read manufacturer product pages, datasheets, manuals, drawings, and supported image content.

5. **ChangesetBuild**
   - represent proposed mutations using the archive-changeset contract.

6. **Validate**
   - validate a changeset without mutating the archive.

7. **ArchiveWrite**
   - apply a validated changeset when the user/runtime is authorized.

8. **Readback**
   - re-read written data and compare against the intended committed state.

9. **ArchiveSync**
   - synchronize the editable central surface and the `.xlsx` consumed by Desktop when required by the current storage contract.

A runtime may be useful with a subset of these capabilities, but it must accurately report the missing capabilities and must not claim completion for steps it could not execute.

## 10. Contracts

### 10.1 Archive changeset

`archive/contracts/archive-changeset.schema.json` defines the boundary between AI reasoning and archive enforcement.

At minimum it must identify:

- job/correlation identifier;
- policy version/revision;
- target Manufacturer + Model;
- operation type;
- expected prior state or revision when available;
- Component create/update operations;
- Port create/update operations;
- Pin create/update operations;
- document/path changes;
- readiness changes;
- evidence references;
- unresolved unknowns;
- conflicts;
- explicit deletions, if deletions are ever allowed.

A changeset is a proposal, not proof that a central write happened.

### 10.2 Validation result

`validation-result.schema.json` must distinguish:

- PASS;
- REJECT;
- REVIEW_REQUIRED;
- validator/tool failure.

It must include machine-readable rule identifiers so an AI can repair a rejected changeset.

### 10.3 Archive readback

`archive-readback.schema.json` records the post-write verification result, including:

- what was expected;
- what was observed;
- central archive revision if available;
- sync state;
- mismatches;
- final write/readback status.

## 11. Validator layer

### 11.1 Purpose

The Validator enforces mechanically checkable invariants. It does not replace engineering judgment.

Examples of deterministic checks include:

- duplicate `ComponentID`, `PortID`, or `PinID`;
- orphan Port or Pin ownership;
- allowed enum values;
- `PinCount == ActualPinCount` when `PinCount` is known;
- full physical Pin-row coverage where the authoritative data establishes a count;
- relative-path requirements;
- required identity fields;
- stable-ID mutation detection where prior state is supplied;
- obvious Ready-gate contradictions;
- schema conformance.

### 11.2 What the Validator cannot prove

The Validator must not claim to prove that:

- the AI found every physical Port;
- the chosen source truly applies to the exact variant;
- a visual wiring diagram was interpreted correctly;
- a missing specification does not exist elsewhere;
- an engineering conclusion is correct merely because the schema is valid.

Those remain evidence/reasoning responsibilities and may require stronger models, additional sources, or human review.

### 11.3 Severity model

Rules should have explicit severity:

- `ERROR`: cannot commit;
- `REVIEW`: may persist only in a non-Ready/review state according to policy;
- `WARNING`: allowed but surfaced;
- `INFO`: diagnostic.

The validator must return rule IDs, not only free-form prose.

## 12. Archive Service boundary

The archive service is an implementation boundary, not another reasoning agent.

Its future API may expose operations such as:

```text
lookup_component
prepare_changeset
validate_changeset
commit_changeset
readback_changeset
sync_archive
```

The service should be deterministic wherever practical and should not require an embedded LLM.

ChatGPT or another AI can remain the reasoning layer while the service handles lookup, validation, controlled mutation, and readback.

## 13. Google Drive Archive

The actual reusable component data remains in the existing central archive.

The storage layer includes the current editable central spreadsheet/surface, synchronized `Component_Intelligence_Database.xlsx`, and:

```text
Documents/<Manufacturer>/<Model>/...
```

GitHub does not become a second component database.

The repository stores only the archive location/contract metadata needed to reconnect a future runtime.

## 14. Central archive manifest

`archive/manifests/CENTRAL_ARCHIVE.md` records non-secret connection metadata required to identify the archive, for example:

- logical archive name;
- expected workbook/sheet names;
- required sheets;
- document-root layout;
- whether Google Sheet or `.xlsx` is the editable/read surface;
- synchronization requirements;
- how a runtime verifies it connected to the correct archive.

It must not contain passwords, bearer tokens, secrets, private authentication material, or confidential engineering document contents.

Provider-specific IDs may be included only when their disclosure is acceptable for the repository visibility and they are not authentication secrets.

## 15. Data continuity

`docs/archive/DATA_CONTINUITY.md` must make clear that:

- Git history preserves policy, Skill, contracts, validator, and evals;
- Google Drive preserves the actual central component data/documents;
- the two stores have different responsibilities;
- a model/chat transcript is never the only copy of an archive rule or required data;
- central writes require readback;
- sync failure is distinguishable from reasoning success;
- provider migration must preserve stable engineering IDs.

The continuity design intentionally follows option A: no periodic GitHub copy of Components / Ports / Pins is required by this architecture.

## 16. AI portability

`docs/archive/AI_PORTABILITY.md` is the "START HERE" document for an unfamiliar AI.

The required bootstrap sequence is:

```text
1. Read docs/archive/AI_PORTABILITY.md
2. Read docs/archive/AUTHORITY.md
3. Load skills/component-intelligence-archive/SKILL.md
4. Read the authority documents selected by AUTHORITY.md
5. Discover available tool capabilities against TOOL_CONTRACT.md
6. Lookup the current central archive state
7. Perform research/reasoning
8. Produce a changeset
9. Validate
10. Write only if permitted
11. Read back and report
```

The document should explicitly state that an AI must not rely on previous ChatGPT conversations to recover missing policy.

## 17. Change policy

`docs/archive/CHANGE_POLICY.md` governs policy evolution.

### 17.1 Policy changes

A semantic archive-rule change requires:

- a Git commit/PR;
- update to the authoritative spec or a new version;
- update to `AUTHORITY.md` if the selected authority changes;
- validator updates when machine-enforceable behavior changes;
- eval updates for affected cases;
- migration notes if existing archive data may require repair.

### 17.2 Skill changes

A Skill change should modify procedure only. If a proposed Skill edit changes what counts as valid engineering data, that change belongs in the archive specification first.

### 17.3 Validator changes

Validator changes must identify which authority rule they enforce. Validator behavior must not silently create new engineering policy.

## 18. Evals and regression corpus

Initial regression cases should include accepted known-reference components already used by the archive specification:

- OMRON F03-20;
- OMRON K7L-AT50DP;
- IFM AL1342;
- IFM AL5021.

Each eval should focus on a distinct failure mode, such as:

- passive functional role vs electrical Direction;
- mixed sensing interface;
- connector-level endpoint mode with complete underlying Pins;
- fixed multi-core independently wired conductors;
- stable `PinID` handling;
- complete physical-pin rule;
- refusal to infer unsupported values.

Evals test both procedure adherence and resulting candidate data. They are not substitutes for live source verification when source facts change.

## 19. Security and confidentiality

The architecture inherits the existing vendor/custom-part confidentiality rules.

Public GitHub content may contain:

- public policy;
- schemas;
- validator code;
- synthetic or non-confidential eval fixtures;
- public source references.

It must not contain:

- NDA/confidential source files;
- credentials;
- Google OAuth tokens;
- private company documents;
- secrets needed to access the archive.

Any eval derived from confidential data must be sanitized or kept outside the public repository.

## 20. Failure semantics

An archive job must distinguish at least:

- `COMPLETE`: write, required sync, and readback succeeded;
- `VALIDATED_NOT_WRITTEN`: candidate passed validation but no write occurred;
- `REVIEW_REQUIRED`: unresolved engineering ambiguity prevents Ready status or safe commit;
- `REJECTED`: deterministic validation failed;
- `PARTIAL`: some requested operations succeeded but the archive job did not fully complete;
- `BLOCKED`: required authority/tool/archive access is unavailable.

An AI must not convert a partial/tool-limited state into a generic "done".

## 21. Migration from current state

Implementation should proceed without relying on the historical development branch after completion:

1. Create the archive governance files on a feature branch based on current `main`.
2. Promote the accepted `COMPONENT_ARCHIVE_SPEC_V2.md` content into the feature branch from the known authoritative source, preserving engineering meaning.
3. Add `AUTHORITY.md` selecting that document.
4. Add the portable Skill and tool contract.
5. Add schemas/contracts.
6. Add the first Validator rules and tests.
7. Add eval fixtures/cases.
8. Add the archive manifest and continuity/portability docs.
9. Validate repository-internal references and bootstrap instructions.
10. Merge through review into `main`.
11. After merge, `main` becomes the only required archive-governance discovery branch.

This migration must not merge unrelated code from the existing large development branch merely to obtain the archive spec.

## 22. Acceptance criteria

Architecture v1 is complete only when all of the following are true on `main`:

- `docs/archive/AUTHORITY.md` exists and identifies `main` as the production authority entry point;
- the accepted archive specification is present on `main`;
- the archive Skill exists and points to authority rather than duplicating policy;
- `TOOL_CONTRACT.md` defines provider-neutral capability requirements;
- changeset, validation-result, and readback schemas exist;
- a deterministic validator exists for the first machine-checkable invariants;
- validator tests cover both passing and rejected cases;
- the initial reference eval set exists;
- `CENTRAL_ARCHIVE.md` identifies the Google Drive archive contract without exposing secrets;
- `DATA_CONTINUITY.md` explains GitHub-vs-Drive responsibility;
- `AI_PORTABILITY.md` allows a new AI to bootstrap without chat history;
- policy changes have a documented version/change process;
- the design does not require an OpenAI-specific model or ChatGPT-specific memory to remain usable.

## 23. Design invariants

The following statements must remain true as the system evolves:

```text
The AI is replaceable.
The Skill is portable.
GitHub is the durable governance source.
main is the durable discovery branch.
The archive policy is defined once.
The Validator enforces but does not invent policy.
Google Drive remains the central component-data/document archive.
Chat history is not required for recovery.
Unknown engineering facts are never invented to make a workflow pass.
```
