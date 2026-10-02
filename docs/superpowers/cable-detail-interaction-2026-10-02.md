# Cable manufacturing detail interaction

## Approved scope

The owner approved the bounded in-chat design: independently select, drag,
resize and delete the manufacturing table and sketch; double-click a table to
edit the existing cable instance; save/reopen/PDF and Undo/Redo preserve layout.
Removing a view must not remove the physical cable or electrical connections.
Sketch outlines remain authored externally in CAD.

The owner additionally reported numeric Pin order and repeated candidate
creation on saving an existing library cable. Default table order is now actual
connector/Pin display number, not opaque IDs or mapping-first insertion order.
P1/P2 header sorting sorts complete rows, preserving all electrical pairs.
Existing candidate editing updates the same version. Approved/superseded
versions still create a new candidate; approved entries cannot be overwritten.
Previously created duplicate candidates are not automatically deleted.

## Execution boundaries

AO auto and record-native-fallback were attempted and both failed before
execution with ModuleNotFoundError: agent_orchestrator. Native execution uses
the owner's explicit direct-implementation authorization and current in-chat
approval. This is the native receipt; AO could not record one.
No policies, paid services, native windows, current workbook, user CAD or
production data were modified. Existing unrelated DrawingPlanning WIP is
excluded from this change and the clean-source release.

Antigravity received only an abstract test scenario in its already trusted
test workspace. Its advice was advisory, not implementation authority; no
repository source, BOM, CAD or personal data was sent. Source and instance
isolation, visibility rehydration and export consistency are covered locally.

## Implementation

- Saved layout stores independent table/sketch visibility and positions.
- Table corner resizing scales text and row sizes proportionally. Sketch
  corner resizing changes its maximum extent, preserving CAD aspect ratio.
- Actual presentation bounds drive transparent hit areas and selected resize
  handles. Handles are excluded from print/PDF rendering.
- Drag previews are built from the pre-gesture project; release records one
  mutation. Escape cancels without saving a preview or adding undo history.
- Delete and right-click remove only the selected view. Removing the last part
  removes its layout binding, not its source cable. Remaining parts can also
  be restored through the layout dialog.
- Double-click/table context menu/cable settings edit instance data; source
  template and other instances remain unchanged.
- Candidate updates require an existing candidate and archive concurrency
  token. Source hash verification, locked atomic save and failed-import
  staging cleanup remain enforced.

## Verification

RED: core tests reproduced nonnumeric row order, missing independent visibility,
and inability to update a candidate. Actual offscreen WPF tests reproduced
nonnumeric initial/header ordering and missing direct drag surfaces.
GREEN: the same regressions now pass.

Final full worktree suite: 1303 passed, 0 failed, 0 skipped. The suite includes three
protected pre-existing tests that are not part of this delivery.
Actual WPF smoke tests verify selected resize handles, preview isolation,
single release commit, Undo/Redo, cancel, SQLite reopen, edited-view PDF,
numeric ascending/descending header sorting and candidate version defaults.
The prior connector editor and dual-CAD/multi-page PDF smoke suites also pass.
Synthetic fixtures are not engineering-approved project evidence.

Independent review and clean committed release verification are recorded in
the final delivery manifest. Owner GUI acceptance remains pending.

The independent review found two Minor issues: editing reset FUNCTION sorting,
and canonical display order could cause a no-op instance save to rewrite
mapping order and create Undo history. Both were reproduced before correction.
FUNCTION sorting now retains its collection-view order; unchanged instance
edits return an exact clone, preserving stored mapping order and confirmation.
The latest test counts are recorded in the delivery manifest.
Focused independent re-review cleared both Minor findings with no remaining
findings in those blocks. Review was read-only; validation was run by the lead.
The stale-library-selection red-green test also verifies the constructor reloads
the latest candidate while retaining an archive concurrency guard.
Existing unrelated build warnings remain; no warning-free build is claimed.
