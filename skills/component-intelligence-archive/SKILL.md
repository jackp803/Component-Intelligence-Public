---
name: component-intelligence-archive
description: Use when researching, creating, updating, auditing, repairing, or completing reusable electrical, electronic, industrial-control, cable, connector, or custom-part records in the Component Intelligence central archive.
---

# Component Intelligence Archive

## Overview

This Skill owns the archive **procedure**. Engineering truth remains in the authority selected by `docs/archive/AUTHORITY.md`; do not copy or reinterpret that policy here.

## Required Start

Production repository: `jackp803/Component-Intelligence-Public`  
Production discovery branch: `main`

1. Read `docs/archive/AUTHORITY.md` from that production repository/checkout.
2. Read the archive policy and only the supporting documents that the task needs.
3. Discover available capabilities against `references/TOOL_CONTRACT.md`.
4. If authority cannot be resolved, stop with `BLOCKED`. Chat history is never a substitute.

## Workflow

1. Resolve exact Manufacturer + Model / Part Number. Do not create a formal identity when authority says identity is insufficient.
2. Run `ArchiveLookup` before research. Classify the job as `CREATE`, `UPDATE`, or `REVIEW`; preserve existing stable IDs on update.
3. Gather exact-model evidence using the source/evidence rules in current authority. Extraction output is candidate evidence, not automatic truth.
4. Build candidate Components / Ports / Pins and document-path changes. Keep unknowns and source conflicts explicit.
5. Build an archive changeset that records authority version/revision, evidence, unknowns, conflicts, and expected prior state when available.
6. Run `Validate` before any central write. Repair deterministic errors; keep unresolved engineering ambiguity in review state.
7. Use `ArchiveWrite` only when the runtime is authorized. Without write capability, return `VALIDATED_NOT_WRITTEN`; never imply the archive changed.
8. Run `Readback` after every write and compare observed state with the committed changeset.
9. Run `ArchiveSync` when current storage authority requires the editable surface and `.xlsx` mirror to agree.
10. Report what was Created, Updated, Needs Review, Unknown, or in Conflict, plus remaining Topology / Layout / Wiring readiness gaps.

## Terminal Status

| Status | Meaning |
|---|---|
| `COMPLETE` | Required write, sync, and readback succeeded. |
| `VALIDATED_NOT_WRITTEN` | Candidate passed validation; no central write occurred. |
| `REVIEW_REQUIRED` | Engineering ambiguity/evidence gap prevents safe completion. |
| `REJECTED` | Deterministic validation failed. |
| `PARTIAL` | Some requested steps succeeded, but the job did not fully complete. |
| `BLOCKED` | Required authority, archive, or tool capability is unavailable. |

## Hard Procedure Boundaries

- Do not skip current-archive lookup because a component was researched before.
- Do not write directly around the Validator.
- Do not treat Validator PASS as proof that every physical Port or source fact was discovered.
- Do not claim `COMPLETE` without required write + readback + sync.
- Do not silently replace an unavailable tool with a weaker evidence source.
- Do not use UI presentation needs to edit engineering facts; consult authority.

## Quick Reference

```text
Authority → Identity → Lookup → Evidence → Candidate → Changeset
          → Validate → Authorized Write → Readback → Sync → Report
```
