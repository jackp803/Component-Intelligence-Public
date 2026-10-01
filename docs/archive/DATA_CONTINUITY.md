# Component Intelligence Data Continuity

## Two durable stores, different responsibilities

### GitHub

Git history preserves:

- Authority and policy selection;
- engineering archive specifications;
- the portable Skill and tool contract;
- machine-readable contracts;
- deterministic validator code and tests;
- sanitized eval cases;
- non-secret archive manifest and recovery instructions.

### Google Drive central archive

Google Drive preserves the actual reusable engineering data and source documents:

```text
Component Intelligence/
├─ Component_Intelligence_Database / editable central sheet surface
├─ Component_Intelligence_Database.xlsx
└─ Documents/<Manufacturer>/<Model>/...
```

This architecture intentionally does **not** require periodic Components / Ports / Pins data copies in GitHub.

## Continuity guarantees

- No required rule or procedure may exist only in chat history.
- Stable ComponentID / PortID / PinID values must survive model/provider migration.
- Every central write requires readback before it can be reported as complete.
- A required Google Sheet → `.xlsx` sync failure is a job failure/partial state, not success.
- A future AI reconnects by reading `AI_PORTABILITY.md`, `AUTHORITY.md`, the Skill, and `archive/manifests/CENTRAL_ARCHIVE.md`.
- GitHub losing access does not authorize guessing policy; Google Drive losing access does not authorize reconstructing central data from model memory.

## Recovery sequence

1. Restore/read the repository `main` branch.
2. Resolve current authority and Skill.
3. Reconnect to the authorized Google Drive archive using the manifest and normal account permissions.
4. Verify expected workbook/sheets/document-root structure.
5. Lookup a known reference component before enabling writes.
6. Run Validator on a no-write candidate.
7. Enable `ArchiveWrite` only after identity, permissions, and readback behavior are verified.

## What is not backed up here

This repository is not a backup of every central row, PDF, CAD file, private supplier drawing, or confidential attachment. Those remain subject to the user's/company's Google Drive and storage backup policy.
