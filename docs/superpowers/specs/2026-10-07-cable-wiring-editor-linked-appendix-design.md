# Cable Wiring Editor and Linked PDF Appendix

## Approved User Intent

The user wants to fill connector inventories in numeric Pin order, then edit
the actual cable-internal wiring visually. The software prepares the cable's
connectors, Ports and Pins from that inventory. The drawing must support
pin-to-pin connections, branches, same-connector shorts, and explicit NC.
The project remains a single engineering-canvas workflow; this is a cable-only
editor, not a return to the old whole-project topology workflow.

PDF output keeps engineering sheets first and concentrates cable drawings in
an appendix. Every representation of a physical cable links to its drawing.
A physical cable appears in the appendix once, even when represented on several
sheets. Different physical instances must not be merged by model or template.

## Scope and Approach

Add a cable-specific editor on the existing library and placed-cable settings
paths. Reuse existing exact endpoint identities, presentation/order helpers,
wire-routing utilities where applicable, persistence and PDF-link mechanisms.
Do not embed the old project topology canvas directly: its project upgrades,
material editing and connector reconciliation must not modify a user's project
when they are only editing a cable drawing.

The alternative of keeping same-row table connectivity would still force
branch wiring to disrupt numeric inventories. A second independent electrical
graph would risk disagreeing with the archived mapping. Therefore retain one
authoritative exact-Pin mapping, and treat the diagram as its editable visual
representation with separately saved layout.

No new paid service, AutoCAD automation, company-workbook modification, native
window operation, or destructive data cleanup is part of this change.

## Table and Electrical Contract

- Each Port retains its stable identity, connector type/coding/gender and exact
  Pin inventory. Display order is numeric, not lexicographic. Nonconsecutive
  physical numbers remain intact; do not invent missing Pin numbers.
- A clearly marked inventory-table mode has one independently sorted Pin list
  per connector. Two entries on the same visual row do not imply continuity.
  Functions and NC/unused declarations refer to exact Pin identities.
- Existing saved connection-group tables retain their original semantics.
  Opening, sorting, exporting or canceling must never rewrite their mapping.
- Entering the new visual workflow explicitly migrates a staged copy to
  inventory mode while preserving every existing mapping pair, Pin function,
  usage declaration and stable identity. Ambiguous/unbound old table content
  must be reported and retained, not silently discarded.
- Connections may link Pins across or within Ports. Shared nets/branches are
  created only by explicit connection actions. Geometric crossings, equal
  function names, connector bodies and collapsed Ports do not imply shorts.
- NC/unused Pins cannot simultaneously participate in electrical mapping.
  Unknown Pins remain pending. Confirmation still needs revision, evidence,
  and complete explicit treatment of the inventory.
- Mapping changes invalidate prior confirmation. Layout-only movement must
  preserve the exact mapping and its confirmation state.
- New inventory/diagram metadata requires a new archive schema version that
  older readers reject, rather than silently dropping the new semantics.

## Editing Experience

Expose an `Edit Cable Wiring` command in the cable library editor and the
settings for a placed archived cable. Prepare all connector/Port/Pin identities
without requiring CAD geometry for each Pin.

Initially place connectors with Pins in numeric order and show existing
connections. The user can move connector groups and Pin positions, draw and
delete orthogonal connections, create explicit branches/shorts, and set
pending/NC/unused status. Preserve a readable separation between labels,
contacts and lines. Moving a group or Pin reroutes affected paths without
leaving accumulated automatic bend points.

Use familiar selection/wiring/delete/undo controls with tooltips, fit-to-view,
Ctrl+wheel zoom and middle-button pan. Layout and mapping are staged together;
cancel changes nothing. Save passes domain validation before applying changes.

Library candidates update the existing candidate; approved templates create a
new revision. A placed-cable edit affects only that instance, not another
instance or the archive template. Existing external connections and CAD Port
bindings retain their identities; changing an instance's inventory is not
allowed through this editor.

## Saved Data and Rendering

Save diagram positions and deliberate route geometry using exact source IDs,
alongside the existing archived template/instance mapping and manufacturing
metadata. Use the same diagram presentation for editor preview and PDF output;
do not maintain a separate screenshot that can become stale.

The manufacturing CAD asset remains an independent optional visual role. It
does not overwrite electrical mapping, and adding a visual drawing does not
create a second cable, Reference or BOM entry. Reopening must restore diagram
layout and actual mapping. Reject stale or missing endpoint references.

## PDF Appendix and Navigation

Build an export-only snapshot/plan, without adding pages or changing revisions
in the saved project. Keep main engineering sheets in order, then emit one
appendix section per represented physical cable with usable archived inventory.
Use its current instance mapping and layout, not the latest library revision.
Unknown internal mappings are visibly draft; never invent conductors for
ordinary wires/cables without known internal inventories.

Reuse an existing dedicated detail for the same cable when possible. Preserve
other page content; do not discard mixed engineering/detail sheets. Repeated
physical-cable views link to the same section. Identical templates with distinct
physical identities or instance overrides get distinct sections.

Each cable-body hit area links to the correct appendix destination. Associated
cable line segments may also link where their cable identity is explicit. The
appendix offers return links to source engineering sheets. Existing
source/destination continuation links remain correct after export page ordering.

Fit or paginate large inventories/diagrams without clipping or silently
omitting Pins. One cable may require several pages, but receives one appendix
section rather than duplicate sections. PDF writing remains atomic and uses a
single final save; failed output does not mutate project data or replace an
existing destination.

## Acceptance and Verification

1. Existing multi-Port/legacy tables reopen with unchanged mapping.
2. Inventory columns sort independently (including 1, 2, 10 and numbering gaps)
   without deriving connectivity from row alignment.
3. Diagram editing handles cross-Port, same-Port and branch connections;
   crossings remain separate and NC conflicts are rejected.
4. Layout-only edits preserve mapping/approval; electrical edits become draft.
5. Removing/deleting connections, undo/redo, cancel and save/reopen behave
   consistently and never leak changes into another physical instance.
6. Archive schema validation and project persistence round-trip the new state;
   old fields/assets/bindings remain intact.
7. PDF contains main sheets first, one section per physical cable, and correct
   forward/return/continuation destinations and nonzero hit rectangles.
8. PDF generation does not mutate saved project JSON/revisions, handles missing
   internal inventories explicitly and never clips large inventories silently.
9. Run domain regressions, offscreen WPF gesture/edit/save smoke checks and
   inspect rendered synthetic appendix pages and actual PDF annotations. The
   user performs native-window acceptance; Codex does not take over the desktop.

## Review Focus

Pay special attention to accidental shorts during table migration, duplicate
candidate saves, stale template versus instance data, junction/crossing
ambiguity, lost Pin numbers, approval inherited by changed wiring, layout-only
approval loss, appendix deduplication by model instead of physical identity,
continuation links after page reordering, and clipped or stale PDF diagrams.
