# Drawing Editing Consistency

## Approved Outcome

The user knows circuits. They approved implementing the consolidated drawing,
labeling, connector presentation, selection/deletion, and CAD refresh fixes.
The objective is fewer repetitive settings and readable, complete drawings,
not novice circuit design or unverified automatic electrical approval.

## Required Behavior

1. Moving modules jointly reroutes eligible automatic wires into distinct
   orthogonal channels. Explicit electrical endpoint IDs never change.
   Manual/locked routes remain authoritative; unavoidable conflicts are visible.
2. Captions use the user's actual wire designation, short Pin function and
   available specifications, never a display index in place of a designation.
   Cross-section text is rounded for display only. Adjacent identical specs can
   share an unambiguous group annotation. Otherwise each wire carries its spec.
   An overlong designation may use a unique reference paired with its complete
   on-sheet name; the stored designation is never replaced or renumbered.
3. No necessary annotation is silently dropped outside the sheet. Use adjacent
   spans, readable wrapping and paper-safe type, then a clearly linked on-sheet
   detail region for names/specs. Full source descriptions remain in the detail
   view and PDF appendix, directly linked from the wire body. Real missing engineering data is
   separate from layout diagnostics and cannot become approved through fallback.
4. A represented Port can expand into a compact, independently identified Pin
   fan-out outside its body. Connector expansion graphics are not conductive
   junctions. CAD source contact positions remain unchanged; persisted display
   offsets drive endpoints and are version-protected.
5. Ctrl selection and box selection support batch wire settings/deletion, mixed
   values, confirmation for consequential deletes, and one undo per batch.
6. Empty pages can be deleted. Populated page deletion offers move-contents or
   delete-representations-and-associated-routes with impact preview. Connected
   representations can be removed with their affected circuits. Cross-page
   markers/dependent routes are cleaned atomically; physical materials are kept
   unless their existing dedicated removal flow explicitly owns the deletion.
7. Refresh placed custom CAD graphics explicitly, for one or matching source
   representations. Exact stable CAD tags/Pin/Port identities retain connections.
   Missing, changed or ambiguous bindings require intervention; never infer a
   replacement by nearest geometry or screen order. Preserve placement, manual
   sizing and electrical declarations; new graphical evidence remains a draft.
8. Source-backed and manual specifications are preserved. Existing conservative
   sizing proposals remain proposals; purchased/catalog data take precedence.
   No invented current, actual cable length, pin mapping or operating mode.

## Boundaries

No AO, production DB/workbook updates, original DWG edits, active-window control,
paid providers, pushes/PRs or unrelated dirty drawing-planning changes.
Existing WPF/.NET/xUnit/SQLite/PDF patterns are retained. No framework rewrite.

## Acceptance

Tests must reproduce the reported name mismatch, multiwire movement overlap,
missing labels, absent batch/page/connected deletion, stale CAD contact positions
and nonconductive Port fan-out. Validate topology immutability, persistence,
manual overrides, one-step undo, linked PDF output, and offscreen WPF interaction.
Use synthetic data only; native mouse acceptance remains user UAT.
