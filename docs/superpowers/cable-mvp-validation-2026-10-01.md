# Cable MVP Validation And Decisions

Approved scope: dual CAD cable roles, explicit region/block imports, connector and Pin controls,
canonical four-column mapping, library draft creation/revision, one physical instance across
wiring/manufacturing views, editable persistence and multipage PDF. Unknown data remains draft.

## Verification

- Independent source review of 7f06276..dc23863: zero Critical, six Important, zero Minor.
- All six findings addressed in one final fix pass with regression evidence.
- Original dc23863 isolated Windows fixtures reproduced lost custom gender/coding, generic catalog
  terminal labels, ignored legacy CAD selection, and missing invalid-layout recovery handles.
- Usage reselection and consecutive NC/unused edits reproduced before the fix.
- New property-overflow and positioned/rotated/wrapped text tests observed failing before fixes.
- Worktree suite: 1279 passed, zero failed/skipped. Includes three pre-existing unrelated tests.
- Real Windows STA smoke: actual WPF controls measured/rendered without showing windows,
  four exact CAD contacts, selected manufacturing block excludes old static table,
  SQLite save/reopen, independent instances, invalid layout recovery controls, two-page A3 PDF,
  failure-atomic export and unchanged project after rendering.
- PDF opened with PDFsharp, checked with pdfinfo, rendered with Poppler and visually inspected.
- This is software verification, not engineering approval of synthetic Pin mapping.
- Existing unrelated Drawing Planning changes were neither committed nor shipped.
- No formal project database, original CAD file or approved archive was rewritten.

## Decisions In Chronological Order

1. Store normalized optional CAD snapshots plus originals. Placement does not need conversion;
   archive JSON is larger.
2. Exclusive archive file guard and expected-content hash. Conflicts require reload, not auto-merge.
3. Canonical CablePinMapping owns complete rows; incomplete rows/usage are separate. Unknowns stay drafts.
4. Instances retain exact Pin inventory. Pin-count changes require a new template revision.
5. Missing wiring CAD is candidate-only pending. Table-first drafts cannot yet be placed.
6. Detail positions/sizes use saved millimetre fields. Free dragging is not implemented.
7. Reject table overflow/overlap without dropping rows. Larger sheets/manual layout may be needed;
   automatic pagination is not implemented.
8. Conservative text extents use anchor, rotation and wrapping without a CAD font engine.
   Tight source text boxes may need enlargement even when actual glyphs could fit.
9. Property edits protect previously valid layouts; existing bad drafts remain recoverable/savable.
   Their overflow/overlap must be corrected before PDF output.
10. Installer and general project Save As remain outside this six-part delivery. Owner mouse UAT,
    actual company CAD acceptance and physical printer output are not claimed by automation.

Deferred Minor review findings: none. Existing compiler/analyzer warnings remain unrelated.

## Owner Acceptance

Use the separate AU test launcher, not the AT/formal database.
Import your wiring CAD and sketch CAD, explicitly excluding any old static mapping table.
Select catalog connectors only before mapping/binding, or reconcile existing data explicitly.
Use the Pin-label editor to change labels while retaining source identity.
Bind exact CAD contacts, fill known mapping, leave unknowns pending.
Place one cable, edit its Reference/length/specification, add manufacturing detail, save/reopen,
and inspect every PDF page and its company frame. Test a second cable instance for independence.

DWG/DWT initial import needs the existing converter; DXF works directly.
Saved normalized snapshots render offline. Nothing guesses continuity from Y shape or names.
