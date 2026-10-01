# Component Intelligence Archive Authority

Status: **Authoritative governance entry point**  
Production discovery branch: `main`  
Current archive policy: `docs/COMPONENT_ARCHIVE_SPEC_V2.md`  
Policy version: **v2**

## Rule

After this architecture is merged, every AI/runtime must start archive governance from `main`. Feature branches may propose changes, but they are not production authority until merged.

## Authority precedence

1. This file selects the current authority set and version.
2. `docs/COMPONENT_ARCHIVE_SPEC_V2.md` defines central archive engineering policy.
3. `docs/CENTRAL_WORKBOOK_KNOWLEDGE_V1.md` defines workbook/storage/runtime contract where it does not conflict with v2.
4. `docs/TOPOLOGY_ENDPOINT_ROUTING_V2.md` defines endpoint presentation/routing behavior where relevant.
5. `docs/VENDOR-PART-INTAKE-V1.md` is a compatibility/intake document and defers new archive decisions to v2.
6. Older documents, code comments, and historical chat records are non-authoritative when they conflict with the set above.

## Conflict handling

- Prefer the archive policy selected here.
- Never resolve a conflict by inventing engineering data.
- If policy and current executable storage/runtime behavior conflict, stop archive readiness progression, record the conflict, and require review rather than silently choosing a convenient value.
- Chat history is context, not authority.

## Data location

GitHub stores governance, procedure, contracts, validator code, evals, and non-secret archive metadata.  
The actual reusable Components / Ports / Pins records and engineering documents remain in the Google Drive central archive described by the storage contract and archive manifest.

## Change control

Changes to policy selection or precedence must follow `docs/archive/CHANGE_POLICY.md`.
