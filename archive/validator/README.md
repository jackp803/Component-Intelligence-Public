# Archive Validator

The Archive Validator is the deterministic enforcement layer between AI-generated archive candidates and any future central write operation.

## What it enforces

The first v1 rules cover:

- formal Manufacturer + Model identity;
- case-insensitive uniqueness of ComponentID / PortID / PinID;
- Component → Port → Pin ownership;
- stable ID mutation detection;
- known PinCount vs physical Pin-row completeness;
- duplicate PinNumber/contact identifiers within a Port;
- evidence review for `NC` / `Reserved`;
- Google Drive archive relative-path rules;
- CREATE-vs-existing identity contradiction;
- obvious Topology Ready contradictions;
- explicit source-conflict review.

## What it does not prove

A PASS does **not** prove that an AI discovered every physical Port, selected the correct exact-model source, interpreted a drawing correctly, or found every available specification. Those remain evidence/reasoning responsibilities.

## Status

- `PASS`: deterministic checks allow the changeset to proceed to an authorized write boundary.
- `REVIEW_REQUIRED`: no deterministic error, but evidence/engineering review is still required.
- `REJECT`: at least one deterministic error blocks write.
- `TOOL_FAILURE`: validation input/runtime failure; no write may be inferred.

Rule IDs and their authority mapping are documented in `rules/RULES.md`.
