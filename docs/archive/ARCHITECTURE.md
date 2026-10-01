# Component Intelligence Archive Architecture

## Canonical flow

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

## Responsibility boundaries

| Layer | Responsibility | Must not do |
|---|---|---|
| AI / reasoning runtime | Research the exact component, interpret evidence, construct candidate changes | Become the only copy of policy/data |
| Skill | Define the repeatable archive procedure and capability expectations | Redefine engineering policy |
| GitHub Authority | Select and version the engineering rules | Depend on one chat/model vendor |
| Validator | Enforce deterministic invariants with rule IDs | Invent engineering facts or claim undiscovered data is complete |
| Archive | Preserve reusable Components / Ports / Pins / Documents | Treat transient chat output as central truth |

The design rule is:

```text
Policy once.
Procedure once.
Enforcement once.
```

## Durable stores

### GitHub

GitHub preserves:

- archive authority and versioning;
- archive policy and supporting contracts;
- the portable Skill;
- machine-readable changeset/readback contracts;
- deterministic validator code and rule mapping;
- eval/regression cases;
- non-secret archive manifest;
- data-continuity and AI-portability instructions.

### Google Drive central archive

Google Drive remains the system of record for the actual reusable engineering archive:

```text
Component Intelligence/
├─ Component_Intelligence_Database / native editable sheet surface
├─ Component_Intelligence_Database.xlsx
└─ Documents/
   └─ <Manufacturer>/<Model>/...
```

GitHub is not a second component database.

## Model independence

The reasoning runtime is replaceable. ChatGPT may be used today; future Chat, Codex, Claude, or a local model may use the same Skill if it can provide the capabilities in `skills/component-intelligence-archive/references/TOOL_CONTRACT.md`.

Changing the model must not require rewriting the archive policy or changing stable engineering IDs.

## Safety boundary

A model can propose candidate changes. Deterministic validation can reject invalid structure. Neither alone proves that all physical interfaces were discovered or that a source applies to an exact variant. Evidence coverage and unresolved engineering ambiguity remain explicit review concerns.
