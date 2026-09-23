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
