# Reusable Cable Wiring And Manufacturing Details

Authority: the owner's approved cable-import explanation on 2026-10-01.
This records that agreement; it does not mark any feature implemented.

## Intended Outcome

Archive one reusable cable with a selectable wiring drawing, a manufacturing
sketch, and an editable pin table. Place one physical cable in the schematic;
show its manufacturing details without adding another cable to the BOM.
AI-assisted archival stays outside the application. AutoCAD remains the external
shape editor, not the application's printing or PDF dependency.

## User Workflow

1. Open the cable library and choose a new template or a new revision.
2. Select wiring and manufacturing CAD drawings from any user-selected folder.
   Separate files are supported. For a combined file, explicitly select each
   role's region or named block and exclude the old static table.
3. Select connector definitions, gender, coding, pin count, or a draft custom/
   loose-lead end. Bind wiring contacts to exact source Pin IDs.
4. Fill the `P1 FUNCTION | P1 PIN | P2 PIN | P2 FUNCTION` table. Choose real Pins,
   not visual coordinates. Preserve unfinished rows as draft information.
5. Save a candidate template. Confirm mapping separately with explicit evidence.
6. Place the cable once. Select that existing cable to show its manufacturing
   detail on the same sheet or a new sheet.
7. Edit Reference, length or specification once; both views show the update.
   CAD outline changes require a new external CAD revision.

## Data And Identity

- Extend `CableArchiveEntry` with optional manufacturing CAD metadata. Its legacy
  `AssetPath`, units and template hash remain the wiring role for older archives.
- A role records an archive-relative path, original source hash, units, selection,
  and selection geometry identity. Source files are copied, never moved/rewritten.
- A combined-file selection has its own stable geometry identity in addition to
  the original file hash; a file hash alone does not identify a selected region.
- Extend the archive schema so old readers reject data they cannot preserve.
  Read existing v1-v4 archives without rewriting them just by loading.
- Clone the selected template, mapping revision and both geometries into each
  cable instance/project. Existing projects never upgrade to a new template
  automatically and remain usable if the archive folder moves or is unavailable.
- Runtime Port/Pin identities are distinct per physical cable. Extra drawings
  refer to that same `CableInstanceId`; sketches are not connectable authority.
- Keep `CablePinMapping` as the canonical complete mapping. The table projects
  those pairs and explicit unpaired Pins; row order/layout are presentation.
  Incomplete rows are draft state and never produce continuity.
- Connector/pin edits use source IDs, not row numbers. Refuse silent removal of
  a mapped, bound or externally wired Pin. An existing instance's edits never
  mutate shared/approved templates or other instances.

## Engineering States

- Blank, unknown, explicitly unused and explicitly NC remain distinguishable.
- Same pin count, name, connector appearance or a Y shape never proves continuity.
- Source evidence and an explicit confirmation action are required for confirmed
  internal mapping. Table edits invalidate that confirmation for the edited
  instance/version only. Unknown length/specification does not block drafts.
- Missing wiring geometry/contact binding permits archival preview and detail
  drafts, not pretend connectable image points. Missing mapping permits known
  external Pin wiring but visibly leaves internal paths unconfirmed.
- Existing candidate/approved/rejected/superseded rules stay in effect; candidate
  placement remains visibly a draft, not engineering approval.

## CAD And Table Presentation

Reuse the existing DXF importer and DWG/DWT staging conversion. Direct DXF works
without AutoCAD; unconverted DWG/DWT still needs the existing converter and must
produce a clear diagnostic when absent. Do not add a CAD engine or paid service.

Selections preserve units/origin and contact transforms. Intersecting entities,
unsupported objects, duplicate tags and partial selections require diagnostics;
never silently drop content or guess electrical bindings.

Connector dropdowns use archived/catalog definitions. Unknown combinations are
draft custom choices, not invented verified physical drawings. Pin count creates
an explicit Pin list, not an automatic 1-to-1 map.

Render the four-column table from the cable's saved data next to its imported
manufacturing sketch. Table/sketch positions and scale are separately editable.
The old static CAD table is explicitly excluded when enabling the editable table.
CAD attribute substitutions for Reference/length/specification reuse existing
text bindings. Moving a drawing never calculates physical manufacturing length.

Use the same presentation primitives for canvas, printing and multipage PDF.
All Pin rows remain visible: overflow is diagnosed or explicitly paginated,
never silently hidden. Unfinished mapping emits inspectable draft warnings.

## Acceptance

1. Import two synthetic DXFs and a combined-file selection; originals unchanged.
2. Choose connectors/pin counts, bind exact contacts, edit the four-column table.
3. Save/reopen a draft with unknown mapping; no confirmed internal continuity.
4. Place two cables from one template; Pins and instance edits remain independent.
5. Add/move/delete detail views without changing physical cable count or wiring.
6. Update Reference/length/specification in all views and retain them on reload.
7. Produce a same-layout multipage draft PDF with table, sketch and company frame.
8. Load old archives/projects unchanged and verify existing cable workflows.

## Delivery Boundaries

Do not edit production databases, original CAD files, approved archives or the AT
runtime during development. Use generated fixtures and isolated build outputs.
The owner performs normal mouse/window acceptance. Installer packaging, project
Save As and data-centre relocation are separate work, not prerequisites for this
cable feature and not represented as completed here.
