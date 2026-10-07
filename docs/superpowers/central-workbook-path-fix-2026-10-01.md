# Central Workbook Selection Fix

## Cause

The isolated launcher supplies COMPONENT_INTELLIGENCE_WORKBOOK pointing at the
empty test workbook. The chooser previously saved the operator's selected path
in shared preferences, but loading always preferred the launch environment value.
The success message and actual BOM lookup therefore used different paths.

## Fix And Boundaries

Explicit selection now overrides the launch default immediately and on reopen.
Environment-seeded launches save the selection beside that launch's SQLite data,
not in shared production preferences. No-environment launches retain the existing
shared preference location. Missing explicitly selected files do not silently
fall back to a different workbook. BOM lookup and the electrical workspace reuse
the same MainWindow resolver.

Central workbooks remain read-only. Existing project databases and the active
operator window are not modified, replaced or closed by this repair.
The AV launcher reuses the existing AU data rather than creating another blank
project database. On first AV launch, explicitly select the intended workbook.

## Verified Evidence

- Six settings tests: four failed with the legacy priority, then all six passed.
- Full working-tree suite: 1285 passed, zero failed/skipped, including protected
  unrelated Drawing Planning tests.
- Real Windows headless smoke: actual MainWindow chooser-save/load methods and
  path tooltip, synthetic BOM lookup resolved, reopen retains selection, separate
  isolated contexts do not share choices, selected workbook and shared preferences
  hashes unchanged. No windows shown.
- Independent read-only review: no concrete Critical, Important or Minor findings.
- Existing unrelated worktree changes are not staged or included in this runtime.
- Actual operator workbook/BOM matching and ordinary mouse UAT remain owner checks.

## Native Fallback Receipt

On 2026-10-01 the prescribed AO bridge help invocation returned ModuleNotFoundError
for agent_orchestrator. The owner separately approved native implementation and
regression testing for this specific bug. The enabled AO configuration remains
unchanged. No paid provider, installation, billing change or policy disabling was
used; this is native Codex work, not AO-approved execution.
