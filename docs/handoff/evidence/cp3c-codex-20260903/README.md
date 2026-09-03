# CP3-C Local Verification Evidence

Task: `CODEX-W1-20260903-004`. These are Windows local technical results, not cloud execution or semantic drawing verification.

## Reading Order

1. Repository's CP3C_CODEX_*_TECHNICAL_VERIFICATION_STATUS_20260903.md.
2. `final-gates.json`: exact commands, exit codes, counts and log names; each log is published in its owning repository.
3. `physical-summary.json`: real physical output identity and complete chain hashes.
4. `protected-summary.json`: scope, counts and protected pre/post hash agreement.
5. Component `ui-smoke.json`; Auto `attempt1..5/`, `synthetic/`, `procedures/`.

The identical cross-repository index `raw-evidence-index.json` names the repository containing each published artifact. Some index entries exist only in the counterpart verification branch. Raw binary diagnostics are indexed by size/hash but remain local. Production database, workbook, source archive assets, DWG/DWT binaries and full private path inventories are NOT published.

## Raw Versus Published Authority

All original raw logs, argv/process observations, input/result JSON and staging binaries remain in the local temp directory named `CODEX-W1-20260903-004` until PM review. Each indexed raw file has a task-root-relative locator, raw byte size and raw SHA-256.

Published transcripts replace machine-local prefixes, normalize UTF-8/newlines/trailing whitespace and may pretty-print JSON. They are **not** raw-byte/hash authority. Embedded canonical hashes in redacted JSON refer to original local inputs/results, not the modified public transcript. Index entries include a separate SHA-256 for each published artifact.

Aliases:
- `<COMPONENT_WORKTREE>`, `<AUTOMATION_WORKTREE>`: isolated dedicated verification worktrees.
- `<TASK_TEMP>`: task-local disposable evidence root.
- `<PYTHON_ENV>` / `<PYTHON>`: actual local disposable Python environment.
- `<LOCAL_USER>`, `<LOCAL_DRIVE_G>`: private local asset roots.

Exact executable identities and source revisions are in the status documents. No real product path is installed from a public placeholder. Procedure transcripts with redacted paths are review evidence, not commands to execute blindly. The harness observer calls the real candidate runner; only dry-run tests use a launch-forbidden sentinel.

No screenshot files are claimed: WPF observations/screenshots are retained in the original tool history. No binary output is represented as a production drawing.

## Limits

`APPLIED != VERIFIED`; `NOT_RUN != PASS`.
Detailed functional exploration remains `NOT_RUN / DEFERRED_TO_MVP_PRODUCT_OWNER_EXPLORATION`.
No CP3-D, product visual acceptance, production release, PR merge or follow-up task authorization is implied.
