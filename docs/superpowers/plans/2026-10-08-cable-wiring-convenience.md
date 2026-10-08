# Cable Wiring Convenience Extension

Approved bounded extension to the 2026-10-07 wiring editor plan: non-junction jump symbols, contact-side mirroring, and editable function association. The user approved direct implementation; no AO, native-window operation, production-data changes or paid services.

## Contracts

- Exact `CablePinMapping` edges are the only electrical authority. Reuse `SchematicCrossingService` to render bridge arcs at unconnected crossings and preserve intentional branch dots. WPF and PDF use the same primitives; selected-wire overlays must not fill bridge gaps.
- Mirror each selected Port's contact X coordinates within its own bounds. Keep IDs, Y positions, Pin order, functions and mapping approval. Clear affected deliberate routes and regenerate outward routes; undo restores the complete draft.
- Propagate a nonblank manual function only through explicit connected components with agreeing manual sources. Track the exact source Pin and last generated value; generated cells cannot become sources. Conflicting manual labels warn without overwrite. Manual overrides, including explicit blanks, persist. Rewiring, deletion and source changes recalculate derived labels without modifying siblings or archive snapshots.
- Save provenance in archive v8 and project 0.9; retain older schemas for records without new semantics. Older releases must reject new schemas rather than silently discarding metadata.

## Execution

1. Domain metadata, association service, mirroring and crossings: seven new tests observed RED then GREEN.
2. WPF toolbar, shared arc renderer, transparent hit paths, staged provenance copying and table commit synchronization: offscreen command/toolbar/render regression observed RED then GREEN.
3. Persistence and removal: three regressions observed RED for source-Port deletion, illegal legacy association metadata and external mapping overrides, then GREEN. Synthetic archive/SQLite round-trip and cycle test passed. Existing invalid-geometry test caught cloning before validation; fixed validation order, full suite GREEN 1399/1399.
4. Shared renderer/PDF and connector regression: wiring/editor smoke, manufacturing `all`, connector `all` passed. Rendered jump appendix and selected-wire editor PNG inspected; bridge gaps, branch dots and labels are visible. Real PDF destinations verified with pypdf: main `[[3,4],[3],[1,2],[1]]`, relocated `[[3,4,5],[4],[1],[1,2],[1]]`, inline-only `[[3],[],[1]]`.
5. Independent read-only review found three Important issues: an unrelated net passing through a junction dot, same-value manual table edits losing intent, and passive mirror saves filling legacy blanks/revoking approval. All three had failing reproductions before the single repair pass. Rendering now tracks explicit-network ownership and bridges unrelated horizontal/vertical conductors at junctions; table commit captures explicit edit intent; passive layout/render/save does not initialize associations. Thirteen domain convenience tests and the real offscreen same-value commit/save/reopen test passed. No second review pass and no Minor findings deferred.
6. Clean committed-source verification, BB packaging and launcher validation pending.

## Scope and Review Focus

Antigravity received abstract invariants only in a separate trusted workspace, no company files. Codex made all edits. Preserve the seven preexisting unrelated drawing-planning files and exclude them from delivery.

Review multi-net crossing/junction semantics, table edit event routing, manual blank/source provenance, instance overrides, stale metadata rejection, mirror routing and editor/PDF parity. Remaining native interaction acceptance belongs to the user.
