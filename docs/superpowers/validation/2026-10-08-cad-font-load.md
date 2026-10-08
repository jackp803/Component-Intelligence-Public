# CAD Font Load Regression

## Diagnosis

- User project-load error: WPF rejects FontSize `8.999999977500001E-06`. Local UI log repeats the same value.
- BD removed the one-DIP floor to preserve CAD text scale, but assumed every positive font size was accepted by WPF. Native microtext violates that assumption.
- Exact synthetic RED reproduces the user's value and exception at CadCanvas TEXT creation. No source project, workbook, archive or CAD edits are required.

## Fix

- Validate the requested font size with WPF's FontSize dependency property. Valid sizes keep their original font size.
- For rejected positive sizes, measure using a valid one-DIP font and render-transform back to the exact CAD size. Scale the anchor offset before rotation.
- Compensate MTEXT layout width in measurement coordinates so wrapping and attachment remain proportional.
- Nonpositive or nonfinite source text heights retain the previous one-DIP fallback. Original CAD primitives and source evidence remain untouched.
- Scope: shared CAD WPF renderer only; no unit conversion, wiring, archive, schema or topology changes. Seven unrelated dirty files preserved.

## Verification

- 72 TEXT/MTEXT cases: exact failing value, normal 9 DIP, 5% CAD font at .45 DIP; four rotations; top-left, middle-center and bottom-right attachments. Effective rendered font size, width factor, wrapping ratio, anchor offsets and immutable source evidence passed.
- Both micro TEXT and MTEXT included in full CAD rendering/bitmap/PDF smoke; transparent overlap, hit testing, percentage/reset, gesture commit, undo/redo and PDF selection exclusion passed.
- Saved database inspected locally through SQLite ReadOnly: two projects, four pages rendered offscreen without source mutation or native windows. No project names/content exported or delegated.
- Full working-tree tests: 1425 passed. Clean-source suite and BE packaging verification recorded after publish.
- Wiring appendix fit/PDF smoke passed. Bounded independent static review: no findings.
- Native window acceptance remains with user. Existing data remains in its original location.
