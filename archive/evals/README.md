# Archive Reference Evals

These cases are a regression corpus for archive procedure/semantics already accepted by the authoritative archive specification.

They are **not** substitutes for live exact-model evidence verification. The fixtures contain only the non-confidential facts needed to pin known policy behavior.

Initial references:

- OMRON F03-20 — functional role vs Passive electrical Direction.
- OMRON K7L-AT50DP — SENSING is Mixed and uses Pins endpoints.
- IFM AL1342 — IO-Link M12 A-coded whole connector remains Mixed + Connector mode.
- IFM AL5021 — fixed 18-wire field cable exposes all independently wired conductors.

Each `case.json` declares a reference component, policy focus, expected Validator status, assertions, and a machine-readable archive changeset.
