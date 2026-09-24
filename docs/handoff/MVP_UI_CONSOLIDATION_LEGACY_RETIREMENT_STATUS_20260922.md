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
