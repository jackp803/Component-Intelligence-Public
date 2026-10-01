# Component Intelligence Archive Tool Contract

This file defines **capabilities**, not product-specific tool names. A runtime may implement them with ChatGPT tools, MCP, CLI programs, local services, browser automation, or future adapters.

## Capability classes

| Capability | Required behavior | Minimum outcome |
|---|---|---|
| `AuthorityRead` | Read repository files at a declared branch/ref and expose revision when possible. | Current authority set can be resolved. |
| `ArchiveLookup` | Query the central archive by Manufacturer + Model and return existing Component / Ports / Pins / statuses / stable IDs. | CREATE vs UPDATE can be decided without guessing. |
| `EvidenceSearch` | Search for exact-model manufacturer sources and relevant approved evidence. | Candidate source list with traceable URLs/identity. |
| `EvidenceRead` | Read supported product pages, PDFs, manuals, tables, drawings, and images when required. | Evidence content can be inspected; unsupported media is reported. |
| `ChangesetBuild` | Produce `archive/contracts/archive-changeset.schema.json` compatible proposals. | Mutation intent is reviewable before write. |
| `Validate` | Run deterministic archive validation without mutation. | PASS / REJECT / REVIEW_REQUIRED with rule IDs. |
| `ArchiveWrite` | Apply only an authorized validated changeset to the central archive. | Mutation result or explicit failure; no silent partial write. |
| `Readback` | Re-read central state after write and compare to intended state. | MATCH / MISMATCH / FAILED result. |
| `ArchiveSync` | Synchronize archive surfaces required by current storage authority. | Editable central surface and Desktop-consumed mirror state are known. |

## Capability discovery

A runtime must discover what it can actually do before claiming completion. Missing write capability does not block research or validation; it changes the terminal state to `VALIDATED_NOT_WRITTEN`.

Missing search, evidence-reading, authority, or archive-lookup capability can make the job `BLOCKED` or `REVIEW_REQUIRED` depending on whether the requested conclusion can still be supported.

## Evidence-reading notes

A model does not become a browser, PDF parser, OCR engine, or vision system merely because it can issue tool calls. Tools/adapters must provide those inputs. Prefer source-native text/tables when reliable; treat OCR/AI extraction as candidate evidence per archive authority.

## Write safety

`ArchiveWrite` must not accept a model's free-form prose as an implicit write request. The write boundary consumes a structured changeset that has passed `Validate`, and a completed write must be followed by `Readback`.
