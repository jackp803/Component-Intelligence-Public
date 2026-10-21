# Transparent CAD and Display Scale Plan

> **For agentic workers:** Use superpowers:executing-plans for inline implementation.

**Goal:** Remove opaque empty CAD backgrounds and allow uniform module display resizing with attached Pins, Ports, and wires following correctly.

**Architecture:** Preserve immutable native CAD geometry/contact coordinates and engineering identities. Derive display scale from saved symbol dimensions relative to native geometry, project scaled geometry only at render/export boundaries, and reanchor attached wires through existing authoring helpers. Store scaled projects as schema 0.10 so older versions reject rather than silently misplace contacts.

**Tech Stack:** C#, WPF, xUnit, SQLite, PDFsharp, netDxf, offscreen bitmap checks.

**Spec:** User approved transparent vector CAD (lines/arcs/text/contacts), selection-only outline, and uniform scale without losing Port/Pin data. Original import units remain explicit: no guessing DWG dimensions or rewriting source CAD.

## Constraints and Review Focus
- No AO, live windows, central archive/workbook edits, or live project-data writes. Preserve seven unrelated dirty files; do not ship them.
- Generic/photo modules keep existing backgrounds and resizing. CAD holes pass pointer hits to underlying geometry; native SOLID_HATCH fills remain intact.
- Rotation, confirmed contacts, source hashes, binding IDs, approvals, and sibling representations must survive display scale.
- Locked attached wires reject contact movement; branches and auto-routing use existing reanchor helpers.
- Text, arcs, hatch contours, and DXF/PDF outputs must scale once, not twice.
- Rebinding and save/reopen retain native contact evidence; scaled schema never downgrades.
- Extremely small/large/NaN scale input must fail predictably; no unit conversion inferred from screenshots.

## Task 1: Rendering and Scaling
Files: SchematicSymbolPresentation.cs; SchematicAuthoringService.CadScale.cs (new); SchematicAuthoringService.cs; SchematicPortPresentation.cs; ElectricalProjectMigrator.cs; SchematicWorkspaceControl.xaml/.xaml.cs/.Cad.cs/.CadScale.cs (new).
- [x] Add domain and offscreen regression fixtures; observe failures for native-size rendering despite changed display dimensions and opaque overlap.
- [x] Add CadDisplayScale/CadDisplayPoint helpers and uniformly scaled view primitives. Native asset, anchors, and CAD bindings remain unchanged.
- [x] Add ScaleCadSymbol(project,id,scale) and ResizeCadSymbol(project,id,displayedWidth,displayedHeight), with 0.05..20 scale, locked-contact protection, and wire reanchoring.
- [x] Add project 0.10 migration guard for scaled CAD without introducing new archive state.
- [x] Make only CAD bodies transparent and line-hit-testable. Add selected/edit-mode CAD resize handle, sidebar percent/apply/reset and native/display-mm readout. Export hides selection controls.
- [x] Verify four rotations, Port/Pin wires, rebind, SQLite reopen, undo/redo, invalid values, DXF geometry, and real WPF pixel/hit tests.

## Task 2: Review and Delivery
- [x] Run full domain suite, focused CAD WPF/PDF smoke, and existing wiring/manufacturing/connector regressions.
- [x] Obtain bounded independent review and fix verified issues with RED/GREEN tests.
- [x] Inspect rendered PDF, verify unchanged data hashes, and publish a clean-source BD bundle with manifest and ValidateOnly launcher. No push/merge/window launch.

## Ledger
- Root cause: RenderSymbol gives every CAD body a solid white background; CAD cannot use the generic-only resize handle. Import currently uses explicit CAD-unit-to-mm ratio and native geometry size, not an independently configurable view scale.
- Localized code/rendering work; no bulk Antigravity task or company-file delegation needed. Validation uses synthetic CAD only.
- RED: two initial domain regressions (unscaled view / missing scale API), plus real WPF output pixel showing upper CAD blank area hides the lower line. GREEN: 19 focused domain cases and CAD offscreen pixel/hit/percentage/reset/gesture/history/output checks.
- Full working-tree suite: 1420 passed. Five older label tests initially failed because their synthetic CAD native dimensions were 30x20 while symbol dimensions were set to 50x30. Corrected fixtures to matching native dimensions, preserving every expected position/spacing assertion; scaled native-coordinate behavior has separate four-rotation tests.
- Existing fit/wiring/manufacturing (all and interaction)/connector smoke passed. Rendered CAD PDF and fitted appendix visually inspected. No native windows were shown.
- Independent review found four edge cases: locked stationary Pin routes were still rewritten; CAD Port drag wrote display coordinates as native; active/history schema promotion lagged behind saving; 5% CAD text was clamped to one DIP. Reproduced each with focused RED tests, then fixed locked-route skip, inverse CAD contact/placement projection, immediate scale-command migration, and exact positive text height. Bounded follow-up review reported no findings.
- Post-review verification: 24 CAD cases and 1425 full working-tree tests passed; tiny-text WPF regression and existing manufacturing/interaction/appendix-fit smoke passed.
- Delivered code c67fb689ed9bf90fa54afcea933fed1c90209583 from git archive; 1422 clean-source tests and clean CAD/fit/manufacturing WPF/PDF smokes passed. Release publish succeeded; 222 runtime SHA256 entries verified by BD ValidateOnly, StartedSoftware=false. Five live data file hashes unchanged. Seven unrelated dirty files preserved/excluded. Native user acceptance remains outstanding.
