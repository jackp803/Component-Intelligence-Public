# CAD Content Frame Regression

## Scope

- Fit the existing CAD editor's selected outline and resize corner to rendered lines, curves, hatches, readable CAD text and displayed Port/Pin contacts.
- Leave native CAD extents, units, pinned geometry/evidence, identities and stored position unchanged. Fitting is automatic on load and presentation only, not a destructive crop or source migration.
- Reference labels and cable captions follow the content frame. Ordinary photo/generic modules retain their existing frame/layout behavior. The selected outline stays absent from PDF/print output.

## Diagnosis And Fix

- RED: a 120 x 100 mm source whose body is 80 x 20 mm retains a 60 x 50 mm frame at 50% scale, including its empty source margins.
- Measure actual WPF ink after layout, transform body bounds through the symbol's orthogonal pose, and union upright CAD text and visible contacts. Add a 1 mm editor margin.
- Source annotations rendered smaller than .01 DIP remain in the renderer and source evidence but do not inflate the frame. Whitespace is not ink; genuinely empty geometry/contact presentations fall back to native bounds.
- Frame positions do not move the drawing or its anchors. Contact layout still uses the native coordinate frame.
- Resize uses mouse displacement from the original fitted corner and a bounded four-step fitted-corner adjustment. Fixed marker radii/stroke widths/margins are not scaled. CAD resizing still uses the existing authoring service and one gesture commit, preserving routing, locks and history.

## Verification

- TDD RED/GREEN for empty-source-margin fitting, fixed-padding corner drift, and contacts-only fitting.
- 12 WPF cases: four rotations x three display scales, immutable source rendering, displayed contacts, nearby reference/caption, fitted corner, no-jump pickup, independently calculated drag destination, existing routed Port attachment/free end, unchanged engineering cable/nets, one commit, undo and output exclusion.
- Five additional cases: circle/arc/solid hatch, wrapped right-attached MTEXT, empty text, unsupported geometry fallback, and visible contacts with only filtered microtext.
- 72 existing TEXT/MTEXT font-limit/attachment/wrapping/rotation cases passed. Existing transparent overlap, hit testing, percent/reset, gesture/history and PDF smoke passed.
- Overlap hit test now skips noninteractive visuals, matching editor input; nearby noninteractive captions are not blockers. Offscreen InputHitTest itself requires a shown window, which the validation deliberately does not create.
- Existing SQLite read-only check: two saved projects/four pages loaded without source mutation or native windows; four CAD selections rendered, two automatically smaller than native margins.
- Working-tree domain suite: 1,425 passed. Seven unrelated dirty files preserved and excluded from release archive.
- Initial independent review findings addressed with regression tests. Clean-source release and final review results are recorded after delivery verification.
- Final independent review identified expanded Port edit handles being moved out of their native drag coordinate contract. Retained their original native placement (this request changes the CAD selection frame, not Port authoring coordinates); exact native DropTarget reproduction went RED, then GREEN. CAD source bindings remain unchanged on render.
- Delivery verified 2026-10-09 after interruption/resume. Code head `92425623879af4ca23b77a3bb4947be664d513c2`; BF bundle built from clean Git archive, not the dirty checkout. Clean-source suite 1,422 passed; 18 frame/interaction cases, 72 font cases and saved-project read-only checks passed again. Transparent CAD/scale/history/PDF smoke and company-frame appendix/link regression passed on the same clean code. PDF rendered with Poppler and visually inspected.
- BF ValidateOnly verified 222 runtime file hashes and existing data/central archive paths; StartedSoftware=false. Source ZIP SHA256 `8FC7384A81EB11D3DEAACE4A2522EAF60C41C3BE4C8DB1E5BF7A7D7C67FF483B`. Five original project/workbook/archive hashes unchanged. All three independent review findings addressed with regression coverage. Native-window acceptance remains with the user. No push, merge, AO, native-window automation, source CAD edits or live-data replacement.
