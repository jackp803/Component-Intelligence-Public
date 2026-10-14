# Connector mating and project Pin function mapping

## Intent and authorization

The user wants imported CAD cable heads to mate as whole Ports, then have
Pin functions follow the electrical path from a device through cables to a
controller. Preserve manual edits and expose missing or conflicting data.
The user previously authorized writing a plan, implementing, and testing
without further stage approvals. Do not use AO.

## Scope and model

- Extend existing DirectMating with an optional explicit one-to-one runtime
  Pin mapping and user confirmation evidence. A Port is never a conductive
  shared node.
- Support component Ports and archived cable Ports in the explicit mating
  command. Keep ordinary archived Port wiring rejected; ordinary Pin wiring
  remains supported.
- Require known compatible family, relevant coding, gender, counts and complete
  unique Pin inventories. Matching numbers populate a preview, not approval.
  Different inventories require an explicit complete mapping. Never use screen
  order or connector front-view geometry to infer numbers.
- One mating per face. Reject same-owner mating, occupied faces, duplicate or
  partial maps, and malformed persisted maps. No new physical cable, wire
  sizing, length, or BOM quantity is created.
- Preserve old DirectMating records without inventing their internal Pin map;
  report their mapping as pending.

## Function association

- Build connected exact-Pin groups from external Pin connections, confirmed
  mating pairs, confirmed archived cable internal maps, and explicit terminal
  continuity. Whole-Port connections without maps do not merge Pins.
- Keep declared Function unchanged. Store a project-only inferred function
  with exact source Pin identity separately. Display declared values first.
- Only populate blank, eligible functions. Manual overrides (including a
  deliberately blank value), NC and unused Pins are not auto-filled.
- Distinct source values produce a visible conflict; do not choose a winner.
  Recompute after structural edits and on save/load, clearing stale inferred
  functions after disconnection. Do not promote inference to electrical
  capability evidence or archive approval.
- Tables, Pin labels, wire details and PDF appendix use the same effective
  function. Manufacturing editing must not freeze project inference into
  reusable template declarations or revoke internal-map approval on a no-op.
- Expose project function review and manual override/reset-to-auto controls.
  Missing required Pins and unknown internal maps remain review items, never a
  claim that the design is electrically safe.

## UI and persistence

- Add a connector-mating mode and an exact-Pin preview dialog with confirmation.
  Draw a distinct dashed mating link, never a normal AWG conductor label.
- Per the user's follow-up, cross-page arrows are the preferred presentation.
  Reuse paired continuation markers and one formal mapped mating across both
  pages. Allow picking Ports directly in the cross-page command. Keep ordinary
  Pin-to-Pin continuation behavior intact and replace the reproducible archived
  Port "Unknown endpoint" failure with a mapping preview/actionable message.
- A mapped connection can be reviewed, changed, or deleted with existing undo.
  Symbol movement reanchors its link using the existing retained drag path.
- Add a function-mapping review dialog, with source and conflict/missing state.
- Add an additive guarded schema version for these fields; older files retain
  their existing facts and gain no inferred mating approval.
- Use synthetic data for validation. Do not edit central workbooks, original
  DWGs, the user's active windows, or unrelated worktree changes.

## Acceptance

Prove compatible CAD Port mating, pin-number preview (including non-sequential
numbers), incompatible/partial/duplicate rejection, capacity, independent
branches, multi-cable propagation, manual and blank overrides, conflicting
sources, unknown internal mapping, disconnect, JSON/SQLite save/load, unchanged
BOM and AWG, undo/redo, visible WPF UI and PDF labels. Run the full test suite,
desktop build, focused WPF smoke and independent source review before delivery.
