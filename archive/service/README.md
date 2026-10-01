# Archive Service Boundary

The Archive Service is a deterministic execution boundary, not another reasoning agent.

## Target operations

- `lookup_component` — return existing Component / Ports / Pins and stable IDs.
- `prepare_changeset` — normalize a proposed mutation into the archive changeset contract.
- `validate_changeset` — run deterministic archive validation.
- `commit_changeset` — apply an authorized validated changeset to the central archive.
- `readback_changeset` — re-read and compare observed central state after write.
- `sync_archive` — perform/verify storage-contract-required sheet/workbook synchronization.

## v1 implementation status

| Operation | Status |
|---|---|
| `validate_changeset` | **implemented** in the .NET Archive Validator and CLI runner |
| JSON changeset/report contracts | **implemented** |
| `lookup_component` service API | **not implemented** as a new service endpoint; current runtimes may use existing archive tools |
| `prepare_changeset` service API | **not implemented** as a standalone endpoint |
| `commit_changeset` | **not implemented** by this service v1 |
| `readback_changeset` | **not implemented** by this service v1 |
| `sync_archive` | **not implemented** by this service v1 |

A Chat/AI runtime may already have independent Google Drive tools, but that does not make these service write operations implemented.

## Write boundary

Future `commit_changeset` must:

1. consume a structured changeset, not free-form model prose;
2. require deterministic validation with no ERROR status;
3. require explicit authorization;
4. preserve stable IDs;
5. return a write result;
6. require `readback_changeset`;
7. require `sync_archive` where storage authority says synchronization is necessary.

Until those operations exist, an AI without external authorized write tools must stop at `VALIDATED_NOT_WRITTEN`.
