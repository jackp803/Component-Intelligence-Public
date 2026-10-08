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
