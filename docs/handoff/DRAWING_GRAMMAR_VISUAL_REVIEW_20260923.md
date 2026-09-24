# Task-013 Drawing Grammar Continuation

Authority: same CODEX-W1-20260922-013, same Component/Auto branches and PRs. The user's approved Drawing_Grammar_Editor_20260922.md supplements current contracts. Historical handoffs are preserved. No rollback to the source-observation SHA.

## Visual Inspection

All 17 page PNGs and all seven negative PNGs were actually inspected on 2026-09-23. PDF SHA256: `4C4CDC244BE78584D8FB7F276B5A0F00C6CCAA0D6A3CDEB946D29804D4377581`. Existing rendered pages reference-01..17 came from the same SHA-pinned PDF. All nine SOURCE_MANIFEST file hashes and sizes match. Text extraction is not the inspection evidence.

| Reference | Visually observed rule | Current gap | Repair boundary |
| --- | --- | --- | --- |
| PDF 1; negative 03/06 | Horizontal source pair; converter below; output drops downward, short taps then continuations | Generic power boxes, side exits; source/return/conversion evidence must be audited before applying roles | DrawingGroupingEvidence, electrical_drawing_plan, layout strategy, canonical anchor metadata |
| PDF 2 | One controller with explicit numbered interface rows and table | Generic box does not expose actual interface positions | representation catalog, WPF representation renderer |
| PDF 3; negative 05 | Imported horizontal potentials independent of communication links; identifiable ports and peer references | P.xx alone, no port/source/zone navigation | continuation projection, WPF labels/navigation, IR lowering |
| PDF 4 | Contact rows 1-19 with short local fan-out and confirmed distribution spine | No confirmed two-sided pairing in current project; generated single-sided list only | HD evidence and representation catalog |
| PDF 5 | Same physical connector, rows 20-38, return distribution | Stable physical identity must survive capacity pagination | HD catalog continuation, natural display ordering |
| PDF 6 | Remaining rows 39-55; engineering-intervention note; empty spare rows | Do not fabricate 56/57 or use fixed three-page count | HD catalog/capacity tests |
| PDF 7; negative 01 | Two module-centered regions; real port rows; inline cable labels | Oversized module strip, arbitrary body-edge connections, miniature cable boxes | IO layout, canonical anchors, inline cable renderer |
| PDF 8 | RIO channels to the right, K7L local connection, bottom supply | Bottom rail and title-block exclusion absent; PDF itself overlaps title block | page geometry, power roles, local routing |
| PDF 9 | Family inset plus six separate power-cable instances | Current detail lacks reusable mapping inset; never infer common mapping | CablePreview, cable template projection |
| PDF 10 | Opposite-end family; per-instance reference/length | Preserve actual end ownership, missing information remains explicit | cable endpoint headings and instance rows |
| PDF 11 | RJ45 family visual and individual lengths | Family appearance does not prove straight-through wiring or Purchased | cable projection/renderer |
| PDF 12 | M12 instance rows with explicit missing XTERM warning | Cannot complete missing endpoint from exterior appearance | localized evidence diagnostics |
| PDF 13 | Physical common sheath/split and two branches; mapping marked TBD | Physical split must remain separate from electrical graph; current project has no confirmed MultiEnd topology | existing MultiEnd projection, no fake junction |
| PDF 14 | UART 3-pin adapter with its own mapping | Never share mapping because geometry is shared | detail template vs instance mapping |
| PDF 15 | Similar RS485 adapter with different mapping | Same limitation; preserve protocol/context | detail instance mapping |
| PDF 16 | Flowmeter cable appearance with TBD mapping | Unknown mapping must remain unknown, not straight-through/NC | progressive preview incomplete boundary |
| PDF 17 | Cable-terminal-cable-terminator, same-page left-to-right flow | Generic packing and unnecessary continuations hide interface sequence | FieldDevices grouping/routing |
| negative 02 | Lexical Pin 29,3,30 and giant rectangular detours are failures | Natural display ordering and local row anchors required | DrawingPreviewCatalog, electrical_representation_catalog |
| negative 04 | Pin 1 used as whole cable-end heading | Main heading must be port/interface; pins only in mapping rows | CablePreview endpoint caption |
| negative 07 | CBL-014 missing mapping is explicitly shown | This is a source-data gap, not permission to invent mapping; presentation still needs improvement | cable evidence and detail renderer |

PDF defects excluded from authority: old sheet totals (14 versus 17 actual pages), page-8 title-block collision, small/overlapping annotations, page-6 intervention note, page-12 missing XTERM, page-13/16 TBD mappings. No old page numbers or wiring copied into the representative project.

## Status Categories Before This Continuation

- IMPLEMENTED_AND_NORMAL_UI_VERIFIED: none of the new section-12 grammar/editor scenarios in this continuation.
- SOURCE_OR_API_ONLY: placement/route edit methods, page reorder, lock methods, previous off-screen WPF rendering, six synthetic layout families.
- MISSING_IMPLEMENTATION: atomic drag gesture, endpoint-preserving segment edits, normal route handles, richer continuation labels/navigation, cross-page placement transaction, canonical transformed anchors, shared crossings/lane semantics, actual DWT content geometry, power presentation roles.
- MISSING_ENGINEERING_EVIDENCE: prior audit reports no Nets/Buses/PowerConversions; unconfirmed HD pairing; seven Custom cables without CoreAssignments; three ambiguous component instances; orphan Unknown cable. Fresh disposable audit must distinguish source absence from adapter loss.

## Execution Plan and Ledger

Continue inline using executing-plans and TDD; no AO/subagents. Engineering/project IDs and all original connections/mappings must remain unchanged. All UI/persistence activity uses a fresh disposable copy. Production assets and approved revisions stay read-only.

1. [x] Inspect all references, verify manifest, preserve existing branch state.
2. [ ] Fresh source/process/protected-state snapshot and disposable DB. Audit exact power source/output/return authority before the real PWR-01/02/03 scenario. Do not infer source equality from voltage text.
3. [ ] RED/GREEN endpoint-preserving route segment movement, immutable gesture draft, one commit/Undo on release, Esc zero mutation. Files: DrawingPlanEditService, DrawingPlanningWorkspaceController, DrawingPlanningWorkspaceControl gesture partial, focused edit tests. Expected: moving first/last segment adds orthogonal lead-in/out while original endpoints remain exact; Locked unchanged; no-op adds no undo.
4. [ ] Bind normal WPF route selection/handles and movement. Connect placement movement to exact catalog endpoint bindings and preserve manual middle geometry; reject locked-route conflict. Verify with normal mouse on disposable project.
5. [ ] Implement paired reference projection/navigation and atomic move-to-page using stable connection identity. Reordering is separate. Verify same-page to cross-page and back, Undo/Redo and saved reload.
6. [ ] Implement evidence-backed power roles/canonical anchors, then lanes/crossings and matching Plan/IR lowering. Missing real authority blocks only dependent scenarios, not unrelated editor fixes. Never substitute synthetic power data for the real acceptance case.
7. [ ] Correct archetype readability (HD natural display ordering, IO port layout, cable headings/insets, field flow), paper bounds and exact approved-asset preparation inventory; no approvals.
8. [ ] Regenerate all real pages; normal UI section-12 sequence; save/close/reload; focused/full .NET and Python tests; Desktop Release; hash/contract parity; protected-state recheck.
9. [ ] Publish sanitized evidence on existing Draft PRs, retain private images/project snapshot locally, exact launcher/runtime/config and continuation ledger. READY_FOR_VISUAL_REVIEW only with actual evidence; PO visual acceptance remains pending.

No present READY claim. Existing tests or an off-screen renderer are not normal UI acceptance.

## Executed Continuation Checkpoint

Disposition: PARTIAL. Not READY_FOR_VISUAL_REVIEW. No Product Owner visual acceptance claimed.

- Fresh disposable SQLite was byte-identical before launch: 101892096 bytes, SHA256 `540BFD1DD421AC0CCA6E4B66E9E2C9132464E75E363A55AC8E072E0E7C5C7C1B`.
- Fresh source and central-sync projection both contain zero Nets/Buses/PowerConversions and 180 null connection NetIds. Converter output has typed 28.8-67.2 V while its display name says 24 V. This is not merely a missing adapter field. The real conversion/receiver power-pair acceptance remains blocked on authoritative engineering evidence, not replaced with PDF wiring.
- Implemented endpoint-preserving first/last segment movement; immutable drag draft; one Undo transaction on release; no-op avoids Undo; Cancel discards draft. Locked route or attached-route conflicts fail atomically.
- Placement moves use exact representation endpoint bindings and transform existing route endpoints. Auto continuation stubs translate together; Manual middle geometry is preserved. This does NOT prove approved-symbol anchor geometry or obstacle avoidance.
- Added normal WPF route hit targets/segment handles, mouse drag, keyboard Undo/Redo/Escape, page-local selection, loaded-state correction, and refreshed endpoint bindings on preview load.
- Heavy Duty presentation order transports explicit per-endpoint display order using existing wiring-rule evidence; opaque IDs remain identities. Empty-contact representations remain visible. No engineering identity/pairing inference or approved-art cloning.

### Actual Normal Desktop Evidence

Private evidence directory alias: `task013-drawing-grammar-20260923`. Source screenshots/project files stay local, not in Git.

| Evidence | Observed result |
| --- | --- |
| ui-01 | Disposable SQLite path visible before entering Electrical Design |
| ui-02 | Representative project loaded normally, actual 32-page progressive preview |
| ui-03/04 | Original continuation-move defect observed; whole movement undone once |
| ui-05/06/07 | Mouse-dragged manual dogleg, normal Save Plan, full application close/reopen and project reload; dogleg retained without regeneration |
| ui-08/09/10 | Corrected converter drag moves local continuation stubs, one Undo, one Redo |
| ui-11/12 | Normal Save Plan, close/reopen/reload; manual placement and route retained; corrected loaded status and current-page selection |
| ui-13/14 | First list-selection Apply attempt did not lock; after canvas selection explicit Apply visibly locked red, drag rejected with Chinese message. First-attempt selection behavior remains a follow-up, not an unconditional UI PASS |

Candidate closed normally. No AutoCAD execution or Generate AutoCAD action in this continuation.

Read-only saved-snapshot comparison: all 180 full connection records identical; Nets, Buses, Cables, CableAssemblies, EndpointReviews, TopologyPlacements and TopologyRoutes identical. Saved plan has 32 pages, one Manual route and one Manual placement; 128 revisions present (not all created this turn). Saved DrawingPlanHash: `AC2FC52356E9DB4BF4415921C0FAE5CC9416BD0C22714FB3D54F54AD0EC9B931`.

### Verification

- Focused preview/editor tests: 14 PASS, 0 failed, 0 skipped.
- Full Component Release: 890 PASS, 0 failed, 0 skipped.
- Desktop Release: PASS, 0 errors, 16 existing warnings.
- Auto contact-order regression: 1 PASS.
- Paired Python environment full regression: 505 run, 504 PASS, 1 ERROR in existing private LRDU staging-package dependency. Full Python suite is NOT PASS. An earlier accidental system-Python run had 29 missing-dependency errors; superseded as runtime selection, not relabeled PASS.
- Both `git diff --check`: PASS.
- Production SQLite unchanged against this continuation's initial copy hash/size; central workbook and reference documents unchanged. The older Task-013 manifest's SQLite differs because it predates this continuation; do not use it as the current DB baseline. Its other 171 protected assets match. K7L rev-001 still matches approved `217A2AC8BDA3418D4E77D1A4D51015AAD7FC93FE819420EF0279EEFFFFBF3828`.

### Exact Remaining Sequence

1. Diagnose the first list-selection Apply State attempt; verify state choice reflects selection and selected IDs survive focus/refresh. Repeat normal lock/unlock, route lock, Escape, and keyboard Undo/Redo.
2. Implement/test paired readable continuation labels and navigation, atomic move-to-page plus same-page/cross-page rebuild. Preserve connection identities and edits; expose page selection, page reorder and component page move separately.
3. Resolve source power evidence conflict explicitly, then implement and verify converter output vertical -> receiver horizontal on the same real confirmed potential. No guessed NetIds or copied PDF connections.
4. Complete canonical anchors, lane separation and crossing/junction recomputation, including Plan/Preview/IR parity. Current existing-endpoint transforms do not satisfy this whole gate.
5. Finish IO/Communication/Field/Cable family rendering, per-instance mapping, paper/title-block constraints; inventory missing approved blocks without approval. Confirm HD ordering in regenerated real pages.
6. Capture all real pages and paired power pages through normal candidate UI; page move/reorder, geometry edits, lock preservation, Undo/Redo, Save/Close/Reload. Do not count synthetic or offscreen renders as these interactions.
7. Fresh final test/build/hash gates; explain or restore the external LRDU fixture without bypassing its test; publish final paired version/launcher evidence. Only then consider READY_FOR_VISUAL_REVIEW, never self-declare Product Owner acceptance.

## RSD Endpoint Voltage Adapter Correction

Same Task-013 branch and worktree, no reset. Disposition remains PARTIAL; this checkpoint is data tracing and correction, not normal UI or real power-pair acceptance.

Correction to the earlier checkpoint interpretation: the erroneous converter voltage was NOT evidence that endpoint voltage was absent from the source. Workbook ingestion preserved it in ComponentIR. `ComponentProjectBridge.BuildPowerCapability` ignored `ComponentPin.VoltageDomain` and copied the device operating/input range into output and return pins. Separately, the earlier `EndpointPowerEvidence` planning projection omitted structured voltage entirely. Both adapter defects are corrected generically, without an RSD-only planner case.

The actual local workbook matches the user-supplied SHA256 `7CD84A26F78F6FA6DF03A4C7193568C4700558C890BCF424958099B02B64CB16`. Its referenced mechanical/terminal-assignment image exists and was visually read. Source workbook and company images were not changed. Exact ComponentId plus typed SourcePortId/SourcePinId, or the already accepted full reconstructed endpoint-ID restoration, authorize power refresh; matching PinNumber alone cannot refresh Power.

| Source pin suffix | Raw workbook / ComponentIR VoltageDomain | Existing saved project before correction | New / synced / reloaded project | DrawingPlanningInput after correction |
| --- | --- | --- | --- | --- |
| INPUT_1 | 28.8...67.2 VDC | 28.8-67.2 VDC | Same source range retained | Input, same range; domain null |
| INPUT_2 | 0 V return | Incorrect 28.8-67.2 VDC | Nominal 0, type Unknown (no invented DC token) | Return, nominal 0; domain null |
| INPUT_3 | NotApplicable; FG | No Power capability | Unchanged, not made into a supply | No EndpointPowerEvidence row; no voltage invented |
| OUTPUT_1 | 0 VDC | Incorrect 28.8-67.2 VDC | Nominal 0 VDC | Return, nominal 0 VDC; domain null |
| OUTPUT_2 | +24 VDC | Incorrect 28.8-67.2 VDC | Nominal 24 VDC | Source, nominal 24 VDC; domain null |

Input-voltage data-quality recommendation only: official RSD-100 specification dated 2025-07-18 distinguishes 33.6-62.4 VDC continuous from 28.8-67.2 VDC for one second. The workbook omits that duration qualification. Do not rewrite the workbook or silently substitute corrected catalog authority in the planner. Official reference: https://www.meanwell.com/Upload/PDF/RSD-100/RSD-100-SPEC.PDF . A later web re-fetch timed out; it does not supersede the earlier successful source read or the actual local terminal image inspection.

### Net / Conversion Diagnosis (Separate From Voltage)

- `TopologyEndpointConnectionService.ConnectEndpoints` accepts optional NetId; when no prior explicit branching Net exists it preserves null, rather than creating a Net definition. Repository Save/Get serializes and restores these nulls. `DrawingPlanningInputBuilder` directly copies connection.NetId; it did not lose a populated NetId in this inspected snapshot.
- The export machine-net resolver derives NET-TBD identities from connection endpoint adjacency without materializing project NetIds. It does not distinguish multi-pin Port vertices from individual Pin vertices, so this is not used as conductor-level PowerDomain or source authorization. No nets or internal terminal/converter edges were created during this audit.
- The inspected project still has 180 null connection NetIds and the converter still has zero PowerConversions. Component electrical ratings and the two evidence contracts are not project-level source/output/return approval.
- The real converter ReferenceDesignator is null. Its four power pins each connect to an existing terminal-block pin; FG is unconnected. A local-only five-row confirmation table includes exact runtime/source IDs, direct peers, known facts and the actual missing decisions. No request to fill 180 NetIds.

### Fresh Verification And Evidence

- Local-only evidence alias: `task013-rsd-trace-20260923/verified`. Includes workbook raw rows, ComponentIR, before/new/synced/reloaded component, exact endpoint context, peer components, planning input, page/drawing plan, invariants and `MINIMAL_POWER_CONFIRMATION.md`. Private data is not committed.
- New disposable DB copied byte-for-byte from the current production source. All 180 full connection records remain identical through sync/Save/Reload; instance reload equality PASS. No project power-domain or conversion record synthesized.
- Production DB before/after SHA256 `540BFD1DD421AC0CCA6E4B66E9E2C9132464E75E363A55AC8E072E0E7C5C7C1B`; workbook before/after hash identical to the authority above.
- Focused RED evidence retained for explicit zero-return parsing and missing planning voltage; corrected tests are included in full Release result: 905 PASS, 0 failed, 0 skipped.
- Desktop Release build PASS. Initial build succeeded with existing warnings but its shell command had an unquoted logger separator; corrected quoted command exits 0. This shell error is not hidden as a successful command.
- Paired Auto `6bb082bfeb4298abe91484f8ebba6b802c4ae06d` successfully accepts the real corrected input and emits page/drawing plans. This proves adapter compatibility, NOT drawing quality, power-domain approval, READY or AutoCAD APPLIED. Auto source unchanged.
- No normal UI power acceptance or AutoCAD execution performed in this data-trace checkpoint. Prior UI evidence is retained, not rerun or relabeled as proof of this correction.

Continuation: keep the existing unrelated UI/grammar plan active. Obtain only the concrete source/output/return decisions in the private table before the real paired-power acceptance; preserve domain separation even though both returns are zero volts. Complete the remaining UI/page/anchor/crossing gates listed above. Do not declare READY_FOR_VISUAL_REVIEW from these adapter tests.

## Operational Delivery Continuation (In Progress)

Same Task-013, same branches and PRs. Start Component `9825ca65f50631056aed06be36322bafeb9bc240`, paired Auto `6bb082bfeb4298abe91484f8ebba6b802c4ae06d`. No reset or replacement of prior evidence. Current disposition remains PARTIAL, not a delivered candidate.

- First-selection root cause identified: child selection events bubbled into the workspace tab handler and reloaded the entire Drawing Plan, clearing selection/history. The handler now accepts only events from the tab control itself. Focused regression observed RED then GREEN.
- Fresh normal mouse evidence in private `task013-editor-delivery-20260923`: first list selection -> Locked -> Apply locks the intended object; locked drag rejects with Chinese feedback; Undo removes and Redo restores the lock. Screenshots `ui-01` through `ui-04`. This is fresh evidence, separate from the earlier second-attempt workaround.
- A separate stale-state dropdown after Undo was observed. Selection-state synchronization is implemented and focused tests pass; fresh candidate rebuild/UI retest remains pending. No-selection/mixed-state must not silently select Auto.
- Disposable project and archive were prepared. Archive registry and referenced approved assets were copied byte-for-byte with hash checks, not newly approved. Launcher explicitly selects the disposable DB and workbook. No archive authoring UI acceptance yet.
- Current fresh focused gesture/event tests: 10 PASS. Full final suite/build, protected-state verification, draft archive round-trip, all-page review and remaining interaction gates are NOT_RUN for this continuation.

### Remaining Delivery Gates (Dependency Order)

1. Complete selection/state UI retest, route lock/unlock, Escape and atomic multi-selection state edits.
2. Atomic representation/group page transfer, shared planner preservation of explicit page edits, same/cross-page reconstruction and readable peer references/navigation. Keep page viewing and order changes distinct.
3. Mouse bend editing, lane/crossing/junction recalculation and locked/manual conflict handling using shared Plan/IR authority.
4. Page-family readability and full representative project page inventory; no hidden difficult pages. Synthetic power grammar and blocked real power pair must be labeled separately.
5. Normal isolated archive candidate inspection/binding/draft-save/reopen/Cancel; approved asset exact revision/hash consumption. Publish A/B/C archive inventory without approving assets.
6. Complete normal editor Save/Close/Reload/regenerate workflow, fresh final test/build/diff/protection checks, paired launcher/settings and evidence publication to original PRs.

Real power-pair acceptance remains dependent on the concrete project source/return decisions, not on the now-correct component pin voltages. Product Owner visual acceptance remains PENDING.

### Implemented After The Initial Delivery Checkpoint

- Selection state now follows Undo/Redo and selected routes; mixed/unselected state cannot silently apply Auto. Multi-selection state changes use one atomic Undo entry. Focused editor/event tests: 11 PASS.
- Added a distinct normal WPF move-to-page dialog and same-group selection. The proposal is not written to the project until the paired Python planner rebuilds successfully. Missing rebuilt connections, changed representation/source identity and concurrent edits reject the whole operation. Manual/Locked attached routes require explicit resolution, never silent unlocking. Controller tests: 4 PASS, including failed-rebuild isolation and selection following Undo/Redo.
- Paired Python honors explicit Manual/Locked page/group ownership when the engineering input hash is unchanged. Changed engineering grouping still fails closed against preserved edits. Synthetic same-page -> cross-page -> same-page regression passes without changing connection or endpoint identities. This is NOT real-project normal mouse acceptance.
- Existing Block Archive now exposes separate Save Draft (Unapproved) / Open Draft actions. Its review sidecar does not create SymbolArchive revisions or write source CAD/workbook assets. Source SHA is checked at save/reload; changed/unavailable source remains blocked; confirmation/approval is never restored from the draft. Two new draft regressions plus existing batch tests: 9 PASS.
- Fresh UI opened the rebuilt archive through the normal main-window entry and visibly showed the isolated workbook/archive and draft controls. The folder-dialog interaction did not complete: helper returned `element 169 is not available in cached app state`, and coordinate focus verification continued reporting the search box instead of the folder field. A separate earlier `coordinate input geometry is unavailable` recovered with a fresh screenshot. Do not count draft Save/Close/Reopen or candidate binding inspection as PASS. Shared-desktop operation question was sent; no answer had arrived at this checkpoint.
- Existing candidate still running is the earlier build with draft buttons, NOT the later page-transfer build. The later Release output is local-only `task013-editor-delivery-20260923/candidate-next`; it has been built but NOT launched or accepted. Do not point the user at it as a tested final deliverable.

### Fresh Technical Gates

- Component full Release: 914 PASS, 0 failed/skipped. Desktop Release: PASS, 0 errors, 16 existing warnings.
- Paired Auto focused Drawing Plan + golden layout: 27 PASS. Full regression initially exposed the preserved-engineering-group compatibility defect; code corrected without weakening its existing assertion.
- The remaining historical Python failure was traced to missing private POC WDP/AEPX/three DWGs in the isolated runtime, not a pip dependency. Located the existing POC root, copied seven source files (including WDT/WDL) into the already gitignored local dependency path, refused overwrites, checked source before/after and copied SHA equality. No AutoCAD execution. Private dependency files are NOT committed.
- Final fresh Python full regression: 508 PASS, 0 failed/errors. Earlier 507-pass/1-error result is superseded only after restoring the actual dependency and rerunning, not by skipping the test.
- Component and Auto diff checks PASS. Current production SQLite, central workbook and K7L rev-001 SHA values match the preceding trace checkpoint. All 171 other protected manifest assets match size/SHA.

Overall remains PARTIAL. Remaining implementation includes complete human-readable shared cross-reference rendering/navigation, mouse bend controls, crossing/lane/junction rules, all-family visual refinement and archive appearance/inventory/binding workflow. Real normal UI page-transfer/reload/regenerate and draft lifecycle remain unproven. The earlier dependency-ordered delivery list still governs continuation; no READY_FOR_VISUAL_REVIEW or visual-acceptance claim.

## Bounded Draft And Bend UI Verification (2026-09-23)

This entry supersedes only the earlier folder-picker and bend-control status, not the remaining grammar or power gates. Same Task-013, Component branch and Draft PR33; Auto PR36/source unchanged. Disposition: PARTIAL pending the active-drag Escape UI check and the previously recorded broader delivery gates.

### Exact Candidate And Preservation

- Before verification, HEAD was `7d4e9690f250a73ee5f92504d1755451adfac4ed`, tree `58ce0f9a1823d011811b0d49fed1f95d43e4dccb`. Three modified source files and the untracked `DrawingBendEditingTests.cs` were preserved in a local recoverable checkpoint. They were NOT in that remote head.
- The actual tested Release candidate is local alias `task013-editor-delivery-20260923/candidate-bend`, not `candidate-next`. Desktop DLL SHA256: `CEE4DE2F6939EFDC58665E9C77B0EE46D9E6E724AE9FE64C5F71F72B90AC71EE`. It contains the preserved bend changes published with this entry. The private launcher now explicitly selects this candidate and both disposable DB/workbook overrides.
- Paired Auto remains `5b9d68da5da7ee1cb9b5c3c6ca0632a4c7388c12`, tree `9f8787cf17c7d38f230602cfcf32442e6ac4f431`; clean, no AutoCAD execution or Auto source changes in this bounded verification.
- A prior reopened process displayed the formal archive root; it was normally closed without Save/Approve. The subsequently launched process visibly showed the disposable DB and archive root before authoring. Formal assets remained unchanged. This incident is not hidden as a successful isolated run.

### Archive Draft Chain

- HUMAN_ASSISTED folder selection resolved the native dialog obstacle. Subsequent actions used the normal WPF main-window Block Archive entry and controls, not service calls or database mutation.
- Scanned the disposable copy of the existing approved K7L asset. UI displayed its source SHA and exact duplicate revision. Saved one **unapproved, incomplete** review draft with one existing catalog PinId/connection-point binding.
- Normally closed archive and application, restarted the same candidate through the isolated launcher, opened Block Archive and used Open Draft. Source SHA, binding text and ReviewRequired status were retained; UI rechecked source integrity and reported zero rows requiring rescan.
- Changed the binding text without saving, used Close, reopened the archive and loaded the draft again. The original binding returned. Draft SHA remained `99324DB7E0187BC8AA88A5AD207ACF75403D537F1226AB998FBFBFCEB7D4BF5A`; isolated approval registry SHA remained `3E8E9D80DA4B6EB1C94AA2A3B6C8C1558DEC0643CE1C36900126128941E58E29`.
- The existing draft format has **no separate DraftId**: identity was checked using draft location and candidate source SHA. Component/Role/SourceType remained unset and explicitly ReviewRequired. This does not prove completed engineering mapping, appearance/deep inspection, or new approval. Close-without-Save is the existing discard action; there is no separate Cancel button here.
- Local screenshots: `archive-draft-saved`, `archive-draft-reopened`, `archive-unsaved-edit`, `archive-cancel-reopened` in `human-folder-checkpoint-20260923-125807`. Private source files/screenshots are not published.

### Bend Editing Chain

- Normal WPF representative project: loaded the saved 32-page plan; selected an existing route between XB4BD25 and LC1D09BNE on the first page; right-clicked Add Bend; dragged the new circular handle into an orthogonal detour.
- One normal Undo restored the pre-drag route including its inserted point; one Redo restored the detour. Applied Locked, attempted a mouse drag and observed Chinese lock rejection with unchanged geometry.
- Used Save Plan and normal project Save, closed the Electrical workspace, reopened it from the main product entry, loaded the same project and opened Drawing Planning. Detour and Locked state survived. This is actual UI Save/Close/Reload, not only a serializer test.
- Independent read-only SQLite comparison confirms all **180 full ElectricalConnection records unchanged**, including IDs, endpoints, NetIds and mappings. Exactly one DrawingRoute changed: intermediate vertices and control state only. Its page, route/connection IDs, endpoint IDs and first/last coordinates remain identical; all legs are orthogonal. This proves preservation of existing anchors, not approval of every formal symbol anchor in the project.
- Screenshots: `bend-added`, `bend-dragged`, `bend-undo`, `bend-redo`, `bend-locked-rejected`, `bend-saved`, `bend-reloaded-locked`; machine readback `readback-result.json` and a read-only verification script are local in the same evidence checkpoint.
- **Escape during an active mouse drag remains NOT_RUN by automation.** The available desktop tool exposes an indivisible drag, not a held-button gesture with an intervening key. A single precise human-assisted gesture was requested after unlocking the route to Manual. Do not substitute the controller Cancel unit test or Escape after mouse release for this UI result. No additional mouse/keyboard events are sent while awaiting that response.
- Current physical drawing style, readable cross-references, page transfer, crossing/lane/junction reconstruction, full grammar quality and real project power binding are not accepted by this bounded test. Corner deletion still rejects a diagonal shortcut; no general corner-deletion UI PASS is claimed.

### Fresh Gates And Exact Continuation

- Full Component Release: **919 PASS, 0 failed, 0 skipped**; TRX stored locally. Desktop Release: **PASS, 0 errors, 16 existing warnings**. Diff check PASS. Auto suite was not rerun in this bounded unchanged-Auto pass; retain the prior 508 PASS as historical only.
- Production SQLite SHA stays `540BFD1DD421AC0CCA6E4B66E9E2C9132464E75E363A55AC8E072E0E7C5C7C1B`; workbook and K7L SHA stay as recorded above. The other 171 protected manifest assets match size/SHA. No formal draft, new archive revision, AutoCAD output, merge or release.
- Resume at the single pending active-drag Escape check. Observe the user's result and fresh screenshot; verify the restored path, then save/reload and compare if needed. Mark HUMAN_ASSISTED only if actually performed. Preserve the currently saved Locked detour and all evidence; do not repeat the folder selection or completed draft chain.
- `EDITOR_WORKFLOW`: the explicitly listed add/drag/Undo/Redo/Locked/Save/Close/Reload steps PASS; active-drag Escape pending. `BLOCK_ARCHIVE_WORKFLOW`: incomplete unapproved draft save/reopen/discard PASS, HUMAN_ASSISTED folder selection. `DRAWING_GRAMMAR`: overall PARTIAL. `REAL_POWER_PAIR`: awaiting explicit project binding. `PRODUCT_OWNER_VISUAL_ACCEPTANCE`: PENDING.

### Active-Drag Escape Follow-Up And Page Transfer (2026-09-23)

- Supersedes the active-drag Escape pending item only. The Product Owner held the bend drag, pressed Escape before mouse release, observed restoration of the bend/incident segments, then released without a second move. Result: **HUMAN_ASSISTED PASS**.
- Follow-up normal UI: one Undo returned to the preceding Locked state with unchanged geometry; one Redo restored Manual with unchanged geometry. Thus the cancelled drag did not add an Undo entry. Independent read-only persistence comparison still finds the saved Locked detour and all 180 ElectricalConnection records unchanged. No new project Save was used to manufacture this result. Local screenshot: `escape-undo-lock.png` in the existing private checkpoint.
- Actual page transfer of XB4BS8442 from the first page to the selected Communication page failed closed: `CP3B_PIPELINE_ERROR: Cannot silently reassign preserved placement ... after engineering input changed; explicit reconciliation required.` The source plan was not replaced. Local screenshot: `page-transfer-stale-input.png`. This is NOT a successful atomic transfer or transfer-back UI gate.
- Investigation: the normal move dialog builds fresh planning input but supplies the saved plan as prior. Python explicitly rejects changed-input page/group reassignment; the controller also requires source hash equality. The source of the hash drift must be established before deciding on reconciliation. Neither gate has been bypassed.
- Latest observation found the user on the I/O page with `Route geometry must remain orthogonal.` visible. This subsequent interaction is not part of the earlier Escape proof. Preserve in-memory edits; do not blindly regenerate or reload the project over them.
- Remaining existing page-transfer/cross-reference/crossing/page-quality gates remain PARTIAL. Real project power binding remains awaiting explicit authority. No symbol approval or Product Owner visual acceptance follows from Escape PASS.

### Real Topology / Existing Plan / Reference I/O Comparison (2026-09-24)

Same Task-013 and worktree; this is a bounded I/O grammar iteration, not overall delivery. The saved representative project's explicit AL1342 X01-X08 endpoint connections, its existing 32-page plan, target PDF page 7, and negative example 01 were compared together. The PDF supplies reading structure only; its old device counts, page numbers and wiring are not project authority.

- Existing PAGE-20-001 ordered devices by model/opaque representation ID. Actual explicit X01/X03/X05 connect to three TA2115 instances; X02/X04/X06 to three PL1514 instances; X08 to SBY246. X07 has no connection in this inspected module. A second AL1342 has its own explicit module group. The first module also has separate X23/X31 cross-page connections, which were not collapsed into X01-X08 or removed.
- Paired Auto planner now orders I/O devices by the explicit connected module Port binding, keeps complete module groups together when they fit, places functional cable labels in each corresponding column, and routes between matching placeholder Port anchors. Only the selected connection's own cable label is allowed inline on its route; other cable placements remain obstacles. No electrical graph, Net, Pin mapping or approved block point is fabricated.
- Private diagnostic used the same Component DrawingPlanningInput builder against a read-only disposable snapshot: 128 representations, 180 connections. Revised paired planner produced 32 pages, including exactly PAGE-20-001 and PAGE-20-002 for I/O. Normal WPF Generate Preview on a second isolated DB copy showed both I/O pages with one complete AL1342 group each; Save Plan succeeded. The saved copy still has 32 pages, 180 identical full ElectricalConnection records, identical component and cable ID sets, and I/O placement counts 16/15. All seven direct X01-X08 connections on PAGE-20-001 have two-point same-column routes.
- Private visual evidence: `task013-editor-delivery-20260923/io-page-review-20260924/before-io-page.jpg`, `after-io-page1.jpg`, `after-io-page2.jpg`; paired PDF page-7 render is `reference-io-page7.png`. The normal WPF action and read-only DB comparison are fresh; this is not only a synthetic fixture.
- Overall Generate Preview remains BLOCKED by unrelated project engineering issues, including Custom Cable Detail Pin/Core Mapping and Unknown Cable review. The complete output is not READY. The visual still uses large unapproved generic module/device boxes and bordered cable labels, lacks readable X01-X08 labels and real approved symbol connection-point geometry, and separates the two modules across pages rather than matching the PDF's two-up sheet. These are open design/asset gaps, not hidden PASS items. No AutoCAD execution or formal-asset change occurred.
- Paired Auto focused planner tests 26 PASS; full Python regression 511 PASS, 0 failed; Auto diff check PASS. Protected production SQLite, central workbook and approved K7L rev-001 retain the exact previously recorded sizes/SHA-256. Component source was not changed in this I/O iteration. Product Owner visual acceptance remains PENDING; real power-pair approval and page-transfer/crossing/page-family gates remain open.

### Conditional Terminal And Port-Orientation Continuation (2026-09-24)

Same Task-013/branch/PR; earlier normal UI evidence remains historical. Overall disposition stays PARTIAL.

- Visually compared the reference drawing's power-conversion and field-wiring pages. The conversion page uses branch/junction presentation without a terminal-block box; the field-wiring page explicitly shows a physical TB1 interface. These are reading conventions, not authority to copy that PDF's wiring, page numbers, or terminal identity into the current project. The coordination `ENGINEERING_DRAWING_STYLE_BASELINE` permits ordinary terminal transparency only with accepted evidence and never creates a new electrical edge.
- Read-only representative-project audit: 18 instances of the exact Phoenix Contact 3209578 terminal type; all have at least one bridge-port connection, while the project has no typed `TerminalBlocks`, terminal-strip sections, or `Nets`. Their responsibility scope is Unknown. Current evidence cannot safely classify every instance as a transparent distribution branch versus a physical field interface. No terminal representation or connection was hidden, removed, merged, or synthesized.
- Existing project data carries explicit cardinal `PhysicalPortLocation.Side`: the RSD-100C-24 instance has INPUT=Left (three pins), OUTPUT=Right (two pins). The builder now transports only exact cardinal sides, with each Pin inheriting its own Port side, as optional presentation metadata. Noncardinal strings remain unknown. Stable endpoint and connection IDs are unchanged.
- Paired planner uses these sides only for non-approved generic/no-asset geometry and rotates them with legal placement rotation. In a synthetic 90-degree test, Left becomes Top and Right becomes Bottom. This does not claim an audited company-block anchor. Existing I/O and Heavy Duty special anchor families retain their own logic.
- Saved-plan migration accepts only additive labels/physical-side metadata after hash verification; other engineering changes still fail closed. A preserved Locked or Manual route whose endpoint would move under the new side does not get silently reanchored. In-memory replan of the actual disposable project identified one such Locked route; no project data was changed. A diagnostic-only copy with that lock cleared yielded the same 32 pages/320 routes/71 issues, but is not a UI pass or authorization to unlock the saved project.
- The prior uncommitted paired-label/continuation work remains included. Fresh C# Release: 926 PASS; Desktop Release build: PASS with 16 existing warnings and 0 errors. Paired Python full regression: 519 tests OK. `git diff --check`: PASS. Production SQLite, central workbook and approved K7L rev-001 retained their previously recorded SHA-256 values; no formal CAD, AutoCAD, or approved archive write occurred.
- The latest indexed cross-page caption rendering and new rotated-port presentation have **not** passed normal WPF mouse/visual review in this continuation. The desktop computer-use surface was unavailable for native app control. Earlier pre-index caption navigation evidence is not relabeled as proof of this build. No terminal-transparency or current-project power-pair PASS is claimed.
- Next bounded steps: reconcile the one saved Locked route explicitly before adopting new physical-side anchors; inspect all representative pages in the real disposable UI; implement terminal branch presentation only where exact project contact/continuity and physical-interface evidence can be established; keep unknown field-interface cases visible and blocked for review. Product Owner visual acceptance remains PENDING.
