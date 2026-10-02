# Task-013: MVP UI consolidation and evidence-driven cable classification

Task: `CODEX-W1-20260922-013`. Worker disposition: **DONE**, pending independent PM review. Not Product Owner UAT acceptance; no merge/release authorization.

## Authority and identity

- Parent handoff: `coordination/PM/CODEX_MVP_UI_CONSOLIDATION_LEGACY_RETIREMENT_20260922.md`.
- Amendment: `coordination/PM/AMENDMENTS/PM_TASK013_EVIDENCE_DRIVEN_CABLE_CLASSIFICATION_20260922.md`.
- Component baseline: `b01024a7ef411b9a4d3e03baa65637b50dd7ed46`; tree `0a8f2b6fe3dac4963f9e9ef5b74848aa98cab968`.
- Branch: `codex/mvp-ui-consolidation-legacy-retirement-20260922`.
- Auto executable unchanged: `dee5faadd161ac7cb93b9963da11fa7d400e212b`; tree `077f7295f79060affd674333fab1309f314e8a8c`.
- Exact final Component executable commit/tree are recorded in the publication section after the implementation commit. Subsequent evidence-only commits do not change executable source.

## Product surface and compatibility

Main has one Electrical Design entry, opening Topology. Electrical Workspace contains Topology, Layout, and Drawing Planning. Manual topology placement/routing, Cabinet Layout, common connectors, terminals, Engineering Review, Symbol Archive, and the accepted Drawing Planning executor remain.

Actual legacy XAML/handlers were deleted, not merely hidden: Components/Nets/Terminal Blocks/Wiring/Derived BOM/Pre-Export/Validation pages, PrimaryTabs workaround, Shorting Jumper creation, user Auto Arrange, Notion surface, duplicate topology entry, old Topology AutoCAD/v2 export runners, disabled Custom Harness placeholder, and overlapping connector/inline dialogs. Shared domain/export utilities needed by accepted flows remain.

Line editing has four actions: Pin Mapping, Cable Settings, Insert Interface, Delete Connection. Insert Interface uses Common Connector authority or Terminal. The hard-coded loose-wire/M12 mating shortcut is retired; Direct Mating domain support remains.

Compatibility regression round-trips historical ShortingJumper, opaque notion IDs, point-to-point Cable, CableAssembly, Multi-End Cable, topology geometry, physical cabinet/placement, and Drawing Plan through temporary SQLite. No destructive migration or project schema bump.

## Evidence-driven cable amendment

- Ordinary Wire has no CableInstance and needs no construction decision. This applies even to selected wires spanning more than two endpoint groups.
- Direct Mating does not become a Cable and does not request construction.
- Persisted Purchased/Custom is shown and retained; selected conductors expand to their complete physical cable membership.
- Optional `ComponentIR.CableProduct` carries typed `PurchasedPreassembled`/`BulkMaterial`/`Unknown` and evidence. Read-only workbook ingestion accepts optional `CableProductKind` and `CableProductEvidence` columns. Legacy absence is Unknown.
- Purchased recovery requires exact ComponentId/CableDefinitionId equality, explicit PurchasedPreassembled authority, and nonblank evidence. Manufacturer/model, category, connector shape, Y shape, and assembly IsCustom are not substitutes.
- Explicit Custom creation persists once per physical Cable/Assembly. Multi-End routing occurs only after explicit Cable choice; Wire is not forced into that editor.
- Engineering Review shows unresolved physical Cable entities only. Effective explicit catalog authority is evaluated on a copy; persistence uses normal Project Save/Revision. Cancel does not save inferred or selected state.
- No existing central workbook or production data was edited to manufacture authority.

## Real disposable WPF checks

All checks used the candidate Release build with the database override pointing to a disposable copy. The displayed SQLite path was verified before project editing.

| Check | Result |
| --- | --- |
| Main -> Electrical Design -> Topology/Layout/Drawing Planning; retired surfaces absent | PASS |
| Ordinary Wire Pin Mapping Cancel and Apply | PASS |
| Point-to-point Purchased and Custom authoring | PASS |
| Two point-to-point conductors grouped into one Custom Cable | PASS |
| Exact six X1/X4/X5 connections -> unified settings -> one Custom Multi-End, common X1, branches X4/X5; save/reload/reopen | PASS |
| Original six IDs/endpoints/Net/pin mapping preserved; no star rewrite | PASS |
| Common Connector insertion with explicit contacts | PASS |
| Terminal insertion | PASS |
| Delete safety confirmation, then Cancel | PASS; destructive confirmation not exercised |
| Cancel reference marker absent after save/reload | PASS |
| Resolved Custom preselected with evidence; whole cable membership shown | PASS |
| Unknown Review row has no default choice; known Custom/Purchased excluded | PASS |
| Two ordinary wires across four physical endpoint groups: Ordinary Wire default, Apply without Cable question | PASS |
| Candidate normal close | PASS |

Disposable references: `TASK013-TEST-PURCHASED`, `TASK013-TEST-CUSTOM`, `TASK013-Y-CUSTOM`, `TASK013-TEST-CONNECTOR`, `TASK013-TEST-TERMINAL`. No production classification was changed. Amendment Cancel check kept disposable DB SHA-256 exactly `A09D18A28B60A6821D2EADD9FCE5EA0D1D3665CE31BDF754E307781EE1EB9037`.

## Final automated gates

- Full Release regression: **869 passed, 0 failed, 0 skipped**, fresh `task013-publish-full.trx`.
- Electrical focused regression: **642 passed**, `task013-final-electrical.trx`.
- Cable authority focused regression: **8 passed**, including optional metadata compatibility, exact authority/no-inference, physical decision grouping, temporary SQLite persistence, and accepted six-conductor Multi-End Custom.
- Desktop Release build: PASS. Final incremental build: 0 errors/0 warnings; earlier full compilation reported 16 existing warnings.
- Retired UI-only tests removed with their retired implementation; shared domain and accepted executor tests remain.
- An intermediate full run exposed a stale source-navigation assertion after the new explicit-choice gate. Updated that assertion; final full run above passed. Synthetic workbook test fixture was corrected to include required Ports/Pins sheets before testing the optional metadata adapter.
- Raw logs remain local to avoid publishing private absolute paths. `NOT_RUN != PASS`.

## Drawing Planning and real AutoCAD

Normal WPF bounded preview returned eligible pages `PAGE-40-001` and `PAGE-60-001`, with no issues, and saved its Drawing Plan. Fresh physical execution used the actual C# LocalDrawingExecutorClient harness over the UI-saved bounded Y slice, not a mocked backend and not a claim that the WPF Generate button was clicked.

Run: `CP3C-2EA0A65DFB9D4411BFFE7D0DAC840BC7`.
Result: `electrical-execution-result.v1`, **APPLIED**, 13 STARTED and 13 APPLIED command events; company WDP and two page DWGs under a fresh isolated staging root. Exact hashes, outputs, event evidence, and containment checks: [execution.redacted.json](task013/execution.redacted.json).

An initial preview used stale local runtime settings and failed with `CP3B_PIPELINE_ERROR` / extra `projectMetadata`; using the exact accepted Task-012 Auto runtime resolved the configuration mismatch. No Auto source change was made. Temporary user-local settings were restored to their original bytes.

`APPLIED != VERIFIED`. Semantic/visual electrical acceptance and the inherited generated-DWG readback hash anomaly are NOT_RUN/deferred to UAT or CP3-D, not silently closed here.

## Protected state

172 protected immutable files checked after real execution and again after final UI close: **0 changed**. Source company CAD/templates, libraries, approved archive revisions, workbook, and production SQLite remain unchanged. Redacted per-asset pre/post hashes: [protected-assets.redacted.json](task013/protected-assets.redacted.json).

Production SQLite: size `64823296`, SHA-256 `4D74B2C59B1A7079508BD2FB371FC232D42B4F16D68535299AC67D33E8B6C33A` before/after. Private source paths are intentionally omitted.

## Review boundary

No Auto source edit, CP3-D, merge, release, or production save. Existing unresolved engineering identities and legacy Unknown cables remain for explicit Product Owner review; names resembling Custom are not authority. No independent reviewer result is claimed by this worker. Feature expansion freeze takes effect after PM acceptance, not merely worker DONE.

## Publication

- Final executable implementation commit: `614a3bd2f124a55a4357c084521083ccab162e0d`.
- Tree: `2b0a609cf35ee64626d11a7bfa4cd017594b049e`.
- Remote Git object SHA/tree confirmed through GitHub API after push.
- Stacked Draft PR: https://github.com/jackp803/Component-Intelligence-Public/pull/33 ; base `codex/custom-y-multi-end-cable-authoring-20260921`. No existing PR was retargeted or merged.
- Staged `git diff --check`: PASS, including new files. Final implementation includes 61 changed/added/deleted files. The complete inventory is `git diff --name-status b01024a7ef411b9a4d3e03baa65637b50dd7ed46 614a3bd2f124a55a4357c084521083ccab162e0d`.
- This publication follow-up is documentation-only. GitHub PR head is the final durable evidence revision; the executable identity above remains unchanged.

## 2026-09-29 schematic Port spacing and move follow-up

Task identity and Draft PR #33 are unchanged. The Product Owner's disposable-candidate screenshots showed evenly redistributed collapsed Ports and stale detours after moving a module. Collapsed Port contacts now use their archived/local Pin-anchor positions projected onto the displayed body; dragging one Port in Edit Module no longer redistributes every other same-edge Port. Top/bottom Port labels are oriented along the edge in the WPF canvas and DXF draft, while the module title remains upright.

Moving a module or its Port recalculates an unlocked, unedited Port-to-Port or Port-to-free-tail route. The exact endpoints, free tail, electrical ConnectionId, and Pin/Port identities remain unchanged. Explicit route edits set `ManualRoute` and preserve their interior bends; locked routes still reject anchor moves. Existing records without this field deserialize with the default automatic behavior. This is a local routing correction, not a claim of complete obstacle-aware routing or visual approval.

Catalog images now drop near-white or transparent outer margins in a memory-only cached bitmap, then use the existing body-size layout on every render. This applies to every generic component after Port/Pin-driven body resizing, including the reported IFM AL1342; the unmodified source picture remains in the isolated Documents snapshot. The WPF canvas/PDF renderer and DXF draft consume the same cropped dimensions. This is visual presentation only, not a change to electrical contact authority or approved archive assets.

Fresh Component regression: 1173 passed, 0 failed, 0 skipped. Desktop Release: 0 errors, 17 existing warnings. `git diff --check`: PASS. The Product Owner reported that PDF saving subsequently worked in the existing candidate; no PDF code change or independent PDF retest is claimed here. New candidate identity and disposable DB/archive are recorded in local `build-identity.json`; normal WPF acceptance of spacing, moved routes, and resized images remains **PENDING Product Owner**. Separate pre-existing Drawing worktree modifications remain uncommitted and are not represented as published source.

Follow-up in the same task: Port interfaces without Pin definitions can now be positioned on a module edge and survive project reload without manufacturing Pin IDs. Cross-page symbol captions use the company-grid-oriented `page.column-row` notation (for example `2.2-B`); full signal and remote endpoint remain available in the canvas tooltip, while PDF page links and DXF draft captions use the matching short code. A missing/unconfirmed template grid remains explicitly `格位待校準`, not an invented coordinate. Fresh final Component regression: 1174 passed, 0 failed, 0 skipped; Desktop Release: 0 errors, 17 existing warnings. The AM local package predates this follow-up and is superseded by the AN disposable candidate. Native WPF visual review remains **PENDING Product Owner**; PDF link activation on the AN build is not independently verified.

## 2026-09-29 expanded Port and compact-frame correction

The same Task-013 branch fixes three issues reported from the AN disposable candidate. Expanded Port controls now remain visible in ordinary selection mode (but not in wire mode or printed output), and Edit Module single-click records the expanded handle as a toggle target so it can collapse again. Generic compact-body sizing no longer treats a long Port name as vertical contact spacing. Collapsed contact groups stay centered on their displayed edge while preserving relative manual spacing. Visible exact Pin anchors constrain the corresponding body edge, including after 90/180/270-degree rotation; Pin and Port identities and wiring are not rewritten. WPF canvas and DXF draft use the shared `GenericBodyBounds` calculation.

New red/green regression cases cover four long-named right-side Ports and expanded bottom Pins in all four orthogonal orientations. Focused schematic Port tests: 39 passed. Fresh full Component tests: **1179 passed, 0 failed, 0 skipped**. Desktop Release build: **0 errors, 17 warnings**. This is code/build verification, not a normal-mouse PASS. A fresh disposable AP candidate, with exact commit and binary hashes in its local `build-identity.json`, is prepared separately for Product Owner visual testing. The AN build and data remain intact; pre-existing Drawing WIP remains uncommitted. Product Owner acceptance of collapse/re-expand, body size, and border contact placement is **PENDING**.

## 2026-09-29 manual module frame, contact spacing, and route follow-up

The Product Owner's AP screenshots showed overlapping expanded Pin/Port contacts, insufficient freedom to place contacts, and stale automatic wire bends after dragging. Edit Module now exposes a bottom-right resize handle on generic modules. Resize persists a manual frame size, redistributes visible contacts along their selected edges with a minimum contact gap, preserves the relative order of hidden collapsed Pins for later expansion, and reanchors incident wires. A Pin can be dragged onto any frame edge; the first Pin drag fixes the currently displayed frame automatically. Port drag also checks for overlapping visible contacts on a manual frame. Invalid drops report the conflict without committing a partial move. Archived CAD body geometry remains source-controlled rather than being resized in this generic-frame editor.

For unlocked automatic Pin/Port routes with no dependent junction, moving a module or contact discards stale intermediate bends and chooses a fresh orthogonal route without retracing its own segments. Exact Pin/Port attachments, free tail, wire identity, and electrical connection remain unchanged. Explicit `ManualRoute` bends are retained, and a locked incident wire blocks an anchor move. This is local route simplification, not complete multi-wire obstacle avoidance or an electrical connectivity inference. WPF canvas and DXF draft both honor the persisted manual frame; the project JSON reload preserves it.

Fresh Component Release regression: **1187 passed, 0 failed, 0 skipped** after a red/green same-facing-contact retrace test. Desktop Release build: **0 errors, 16 existing warnings**. Code/build verification is complete; normal mouse behavior and visual quality remain **PENDING Product Owner**, as requested. The previous AP candidate is not evidence for these new changes. Pre-existing Drawing worktree modifications and untracked files remain separate and uncommitted. No production database, approved symbol, or company source asset was edited.

Implementation commit `74b65f08082491c62c6ec931f24325932ed21232` was pushed to the same Draft PR #33. The disposable AQ package is started with local `C:\Users\jackp\Documents\_codex_evidence\task013-schematic-mvp-20260924-160248\Launch-Candidate-AQ.cmd`; its isolated `ui-20260929-port-aq\project.db`, `SymbolArchive.json`, paired Auto commit, full executable hashes, and snapshot source are recorded in adjacent `build-identity.json`. AQ was built in the dirty worktree and therefore also contains the separate, uncommitted Drawing WIP listed in that identity file; those files are **not** part of the pushed implementation commit. Product Owner must close any older candidate normally before launching AQ. AQ normal-mouse acceptance and PDF/visual output are **NOT_RUN**, not PASS.

## 2026-09-30 company title block and reusable module layout

The single schematic canvas now has a **標題欄** dialog. It stores project name, drawing number, drawn by, checked by, and revision in the editable project, plus a per-page content override. With the override off, each page suggests its own content from the main placed component representations; page number/total follows current page order. Preview, printed/PDF canvas, and DXF draft read the same saved fields and detected template cells. The isolated company-template snapshot has seven recognized lower title-block cells. The source DWT and production project were not edited. Different or attribute-only templates still require a visual/field-mapping check; an unrecognized template shows a warning instead of silently implying the values are visible.

The existing Symbol Archive now stores optional versioned **SchematicLayouts** (schema v4). In **編輯模塊**, the operator adjusts a complete generic module, then explicitly clicks **保存模塊版型**. Its frame dimensions, rotation, relative Pin/Port positions, and collapsed Port state are saved under the exact ComponentId. A new placement in another project uses the active layout only when exact SourcePortId/SourcePinId inventories match; otherwise it leaves the new component unchanged and reports the mismatch. Older layout revisions remain inactive for audit. Approved CAD revisions and their bindings are untouched; this preference is not CAD geometry approval or engineering pin-mapping authority. The first saved profile is not copied into existing placements automatically. Special archived-CAD representations and split module sections remain outside this generic-layout action.

Fresh full Component Release tests: **1192 passed, 0 failed, 0 skipped**. Desktop Release: **0 errors, 17 warnings**. Isolated template geometry was read-only checked for the seven title-block labels and cell positions. No normal-mouse run of this new build, no printed/PDF visual review, and no Product Owner acceptance are claimed. The separate pre-existing Drawing WIP remains unstaged and uncommitted; do not represent it as part of this branch publication. A fresh isolated candidate package and its binary/database/archive identity are recorded locally, without uploading company drawing data.

## 2026-10-01 project-specific BOM palette

The schematic sidebar now displays **專案 BOM**, not every component in the central library. Opening Electrical Design without a main-window BOM import leaves a new project's palette empty. Imported rows, installed/total/spare quantities, resolved definition identity, structured category/subcategory, and stable physical-instance references are saved inside that project's editable JSON/SQLite snapshot. Loading another project restores its own BOM; it no longer merges the main window's currently imported rows into the loaded project. An initial asynchronous import cannot repaint a project that has since been switched.

Rows show a thumbnail (from the existing image resolver), manufacturer/model, classification, installed quantity, placed physical count, remaining physical count, total quantity and spares when known. Categories are grouped and filterable; search covers identity and classification. Missing photos/categories remain explicit rather than being guessed. Quantity unknown remains unknown; spare-only rows do not create installed component instances. Connection-material rows remain visible but direct placement asks the operator to use the cable library/settings. BOM placement references the next existing unplaced physical instance, applies its saved generic module layout when compatible, and does not increase component/BOM quantity or change electrical connections. Multiple representations count once. Escape cancels the preview; selection/filter changes cannot substitute a different item for a pending placement.

Older saved projects lack the original imported rows. Their palette uses only their own physical Component instances, labeled **既有專案實體數量**; original purchased/spare totals cannot be recovered from those records and are not fabricated. Central definitions provide appearance and classification, not inventory authority.

An independent code review found that a cancelled import could leave new physical objects with the previous BOM snapshot, and spare-only rows omitted saved definition/classification metadata. Both were reproduced with three new failing cases before correction. Imports now stage all physical additions and metadata and publish them together only after successful lookups and a final cancellation check. Spare-only rows resolve metadata without creating physical objects; the older no-lookup assertion was updated to verify the new metadata-only behavior rather than dropping that test.

Fresh final Component Release regression: **1202 passed, 0 failed, 0 skipped** (focused inventory checks: 24 passed). Desktop Release rebuild: **0 errors, 17 existing warnings**. A local offscreen WPF audit with an explicitly synthetic fixture renders the real control at 1440x900 and 1024x768, checks empty/imported/project-switch palettes, grouping/filtering, thumbnail loading, preserved pending placement, cancellation without adding a representation, and absence of WPF binding errors. This is **OFFSCREEN_WPF_AUDIT**, not normal-mouse acceptance or a real-project visual PASS. Product Owner reported earlier changes usable; normal operation/visual acceptance of this new BOM feature remains **PENDING Product Owner**.

The new AS candidate uses a fresh disposable snapshot of AR's project DB/archive/workbook/Documents/template; AR and its data are retained. Its exact Component/paired Auto commit, binary hashes, isolated paths and remaining Drawing WIP source checkpoint are recorded locally in `build-identity.json`. Existing Drawing WIP is still uncommitted and is not represented as included in the published implementation commit. No production DB, central workbook, approved symbol, company CAD source, merge, release or new paid tooling is authorized by this change. Private project data and rendered evidence remain local. Draft PR #33 and Task-013 identity remain unchanged.

## 2026-10-01 Port cable settings and route model captions

The Product Owner reported that a completed collapsed Port-to-Port route was missing from **線材設定**. A synthetic offscreen instance of the real WPF picker reproduced zero rows: the picker accepted only Pin endpoints, although the schematic authoring service had already created the exact Port connection. Schematic cable settings/detail selection now explicitly includes uniquely identified Port endpoints, labeled as Ports. Pin-only multi-end picker callers retain their previous default. Direct Mating, missing and ambiguous endpoints remain excluded.

The operator can select the existing route, open cable settings, confirm **外購成品線 / Purchased**, choose its known model and set Reference/length. Project BOM material snapshots take precedence over matching central-catalog options, retaining available quantity and classification evidence. Applying the choice stores a readable model snapshot on the physical CableInstance. Changing to a different definition without a known model clears the previous model label. This does not invent connector/core assignments, Pin mapping or Net membership. Existing ConnectionId, endpoints, NetId and saved route geometry stay unchanged; Cancel still discards the cloned edit. Assigning a material is not electrical mapping approval.

Assigned cable models such as `EVC014` now appear once per displayed route, aligned horizontally or vertically on a suitable straight span. Caption placement follows the current saved geometry on each page and avoids analyzed crossings, branch junctions and conflict markers. Text masks only its local line background, does not intercept wire gestures, and fits within page bounds, wrapping unusually long model names rather than cropping them. Unknown models are not fabricated from internal definition IDs. Canvas, print and the PDF page renderer use this same caption path; no route re-layout is performed for output. Ordinary/unassigned wires do not acquire a cable-model caption.

Fresh full Component Release regression: **1213 passed, 0 failed, 0 skipped**; focused selection/settings/Port/caption checks: **33 passed**. Temporary SQLite round-trip retains Purchased type, exact cable/connection identity, model, Reference, length and original route, without adding Core/Net records. Additional crossing/branch cases were reproduced failing before correction. Desktop Release rebuild: **0 errors, 17 existing warnings**. Synthetic **OFFSCREEN_WPF_AUDIT** verifies the real picker now has one selected Port row, restores the exact selected material, renders horizontal/vertical captions through the real 300 DPI PDF/print page renderer, and checks oversized captions near sheet edges. Independent source review identified a long-caption page overflow; it was corrected and verified with the real WPF layout engine. No normal-mouse test, real-project PDF export or Product Owner visual acceptance is claimed for this change.

The AT runtime uses a new binary directory and deliberately reuses AS's isolated project DB/workbook/archive/Documents so the operator can save the current project in AS, close it normally and continue in AT. It neither overwrites nor copies the live project data; the launcher refuses simultaneous candidate instances. Exact Component/paired Auto identities, binary hashes and the separate uncommitted Drawing WIP checkpoint remain local in the AT package metadata. The existing AS runtime is retained and not closed by the agent. Production data, approved CAD assets, Auto source, task/branch/Draft PR #33 and merge/release boundaries are unchanged.
