# Manufacturing Sketch Fit And Wiring Colors

## Scope

- Fix false manufacturing-sketch text overflow at the user's X=20, Y=110, maximum180x70mm settings, including existing archived geometry.
- Give every wiring-diagram connection a distinct display-only identification color. The editor and linked PDF appendices use the same renderer; jump arcs inherit the connection color and connector borders/junction dots remain black.
- Keep original CAD import coordinates, asset/selection hashes, saved connections, physical wire colors, page limits, table overflow guards and source files unchanged. No source reimport/migration is required.

## Evidence

- Baseline xUnit:1425 passed. New sketch regression suite first failed10 cases, including the exact reported source-text overflow exception. After fitting,37 focused cases and1442 total working-tree tests passed.
- Fitting resolves dynamic fields in copied primitives before measuring lines, circle/arc extrema, hatch contours and conservative complete TEXT/MTEXT envelopes. It translates visible content to the requested top-left and uniformly fits with a0.5mm stroke margin. The margin adapts for tiny rectangles. Invalid/nonfinite geometry is rejected, not silently hidden.
- Offscreen WPF caught multiline TEXT's last-line baseline differing from the previous first-line estimate. A literal regression failed with133.5mm instead of168mm before the fix. The final envelope passed170 actual-ink cases across TEXT/MTEXT, attachment, rotation, width factors, word-boundary wrapping and glyphs wider than the wrapping box.
- Synthetic placement, layout edit, SQLite save/reopen and3-page PDF passed without source-authority mutation. Four existing manufacturing sketches from two saved projects were checked through readonly SQLite at180x70mm. The user's branching connector sketch was visually inspected with its outer labels/connector faces retained.
- Color smoke first failed against the black-only renderer. Seven distinct edges, matching jump arcs, move/selection/undo, save/reopen and unchanged draft passed. A1000-edge regression exposed rounded HSV collisions; collision handling now retains1000 distinct colors and dictionary-order independence.
- Color identity is derived from sorted endpoint keys for the current graph, not saved into engineering/CAD data. Movement/reordering/reopen/PDF preserve colors. Adding/deleting a connection can reassign palette positions; colors are tracing aids, never insulation specifications.
- Existing wiring interaction regression passed: mirror, selected jump arcs, function association, manual override, staging, undo/redo, zoom/pan, exact nonconsecutive Pin identity. Manufacturing all/interaction checks and company-frame appendix/link regression are included in final verification.
- Poppler rendered the fitted sketch PDF and colored wiring appendix; both were visually inspected. Existing PDF forward/return links, physical deduplication, page ordering and failed-export atomicity passed.
- Independent local review found three material cases: MTEXT word-boundary/glyph overflow, empty-label invalid dimensions, and large finite origins losing small radii through floating-point rounding. Four domain regressions and a30-case WPF extension reproduced the failures first. Measure in local coordinates, validate empty text properties, and account for word-boundary wrapping/overflow; all findings are addressed with RED/GREEN coverage.
- No AO, native-window operation, company data sent to an external model, original CAD edits, live-data replacement, push or merge. Antigravity reviewed only an abstract no-files bounds problem.
- The two workbooks and two archives retained their original hashes during validation. The live project.db hash changed during the session; it was opened only via readonly SQLite by the validation tool, never saved/replaced. Do not overwrite it with a test snapshot or claim its hash is unchanged.

## Delivery

- BG bundle: `2026-10-12_BG示意圖與彩色接線版` under the existing project collection.
- Runtime built from a clean Git archive, excluding the seven unrelated dirty files. Launcher validation does not start the app. Native-window acceptance remains with the user.
- Final clean-source test/build, review and data-preservation results are recorded after packaging.
