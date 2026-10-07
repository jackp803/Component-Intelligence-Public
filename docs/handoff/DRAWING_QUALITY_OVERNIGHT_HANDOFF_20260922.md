# Task-013 Drawing Quality Continuation

Disposition: **BLOCKED**. `READY_FOR_BLOCK_ARCHIVE = NO`.
This is not final drawing acceptance, not a new task, and not an AutoCAD run.
Prior progress records remain historical evidence and are not replaced.

## Authority and Exact Implementation

- Coordination main: `bca573888b18c903b29fd90856f90618289dffde`.
- User overnight continuation: missions A-I, latest PROJECT_SPEC takes precedence over the historical ACADE README/PDF.
- Component branch: `codex/mvp-ui-consolidation-legacy-retirement-20260922` (existing Draft PR #33).
- Component code commit: `16653c37bc0ae5fe33fe539c5e6fb29ab229d48d`.
- Component code tree: `1e3e951bbd09d3eb63c0679fcb2769d75d37594b`.
- Auto branch: `codex/task013-drawing-space-issue-fix-20260922` (existing Draft PR #36).
- Auto code commit: `6f3a58e42e8d3b07e5b9bacddb3e0f20ed0f682b`.
- Auto code tree: `9a71523c7f7978333fe3785576d0dd96ca384461`.
- The subsequent documentation commit is the evidence wrapper; its final SHA/tree are recorded on the existing PRs after push. Neither PR is merged or retargeted.

## Decisive External Engineering Dependencies

The read-only representative project/catalog audit still emits 11 engineering blockers. These are not missing attractive block artwork and cannot be resolved by inventing identities, core assignments, or construction choices.

| Required confirmation | Exact evidence | Safe next action |
| --- | --- | --- |
| Two GRUNDFOS instances displayed as `3-3 MO-FGJ-A-N` | `ENGINEERING_COMPONENT_REVIEW_REQUIRED`; no exact catalog identity/endpoint authority | PO confirms exact component identity and required endpoint correspondence through Engineering Review; do not match by displayed model text. |
| One FUJI instance displayed as `FLYF003` | Same identity/endpoint blocker | Confirm authoritative identity and endpoints, or retain unresolved. |
| CBL-014, CBL-018, CBL-019, CBL-020, CBL-021, CBL-022, CBL-024 | Each is explicitly Custom, has one connection, and has **zero CoreAssignments** | Supply actual Pin/Core mapping. Do not invent cores or infer continuity from connector shape. CBL-014's display text `IFM_EVC927` does not override its stored construction authority. |
| One unnamed physical cable | Construction Unknown, zero current connections, zero core assignments | PO decides whether it is a real required physical cable and confirms construction, or explicitly removes it through normal editing. No automatic deletion/reclassification. |

Additional source limitations: the project has no explicit Nets/Buses, no assigned PowerDomainIds, and no component PowerConversions. Known individual pin power roles are carried without inventing domain equality, source-output bonding, or conversion continuity. The Heavy Duty catalog has individual contacts on two ports but no authoritative two-side pairing in this model. Its generated preview lists individual contacts, not fabricated paired rows. The historical PDF is not substituted for current wiring authority.

## Implemented Evidence Flow

`ElectricalProject -> DrawingPlanningInputBuilder -> validated planning input -> Python page planner -> Drawing Plan -> real WPF preview control` was exercised on an in-memory synchronized project loaded from a byte-for-byte disposable SQLite copy.

- `DrawingGroupingEvidence.cs`: exact catalog category, exact endpoint ownership and explicitly equal protocols; real IO-Link master/module membership; explicit network/power membership; cable owner grouping; Heavy Duty contact evidence; pin power roles in typed wiring-rule payload.
- Layout-only protocol groups are explicitly named `LAYOUT-NETWORK:*`. They do not create a project Bus/Net or electrically bond ports. Independent RS-485 ports remain separate; only an explicitly categorized switch/master provides the layout hub boundary.
- Ambiguous multi-master ownership stays unset with a warning. Duplicate endpoint identities produce a blocker rather than arbitrary ownership or an exception-only crash.
- MachineZoneId and SeriesChainId remain unknown where the source has no such authority. No name/model/filename/position-based inference was added.
- `DrawingPreviewCatalog.cs` mirrors generated Heavy Duty continuation identities for WPF. Exact approved assets are not cloned into invented parent/child assets.

## Placement and Routing

- Explicit power functions receive MainPower pages without changing approved SymbolRole.
- IO pages use the actual module as a wide anchor, with its devices grouped above/below and compact cable labels.
- Explicit communication hubs anchor their network layout. Cable details use measured instance rows, not generic graph boxes. Heavy Duty uses individual-contact capacity and stable continuation identity with one physical owner.
- Shared obstacle router uses orthogonal visibility paths, length/bend/crossing costs. Same-source rails require explicit source evidence; intersections do not create junctions.
- Cross-page relationships now use local escaped stubs and page-reference arrows rather than long trips to the sheet edge. Heavy Duty stubs align to their contact rows.
- A Manual/Locked placement whose page/group authority would change fails closed rather than being silently reassigned. This is not a claim of exhaustive manual-layout editing acceptance.

## WPF and Visual Review

- Readable component/cable labels, compact cable captions, point-to-point cable detail, separate physical Y split and electrical mapping list, Heavy Duty contact rows, and local continuation page captions.
- Preview drawings are not Approved Symbol authority. The Y split renderer creates no electrical junction marker or endpoint.
- Actual WPF `DrawingPlanningWorkspaceControl` rendered all 32 representative pages using its real canvas, not an alternative SVG drawing implementation. Power, IO, communication, cable and Heavy Duty images were inspected against the reference grammar.
- This was an off-screen STA WPF render harness. Normal interactive application navigation/editing in this continuation is **NOT_RUN**, not PASS.
- The current protected project contains no MultiEnd PhysicalTopology; its two legacy assemblies do not establish a Y topology. Y structure is verified in synthetic golden cases, not claimed as an actual current-project Y UI result.
- Overall visual acceptance is **not established**: the source/potential distribution and incomplete engineering details remain unresolved. Zero collision metrics alone are not visual or engineering acceptance.

## Quality Metrics

Representative final plan: 134 placements, 320 page-local route legs, 32 pages.

| Archetype | Pages |
| --- | ---: |
| MainPower | 1 |
| PlcIo | 2 |
| Communication | 2 |
| CustomCableDetail | 5 |
| FieldDevices | 15 |
| HeavyDuty | 7 |

Heavy Duty uses the existing generated-preview capacity of 16 individual contacts per page. This is **not** approval of a company block's row capacity or its contact-pairing semantics.

- Route through representation interior: **0**.
- Route outside drawable bounds / into reserved title-block region: **0**.
- Placement overlaps: **0**.
- Cross-page relation/route page mismatches: **0**; WPF page-local route filtering also has a regression test.
- ElectricalConnection ID/FromEndpointId/ToEndpointId/NetId/CableInstanceId unchanged: **PASS**.
- Cable/CoreAssignments and CableAssemblies unchanged: **PASS**.
- Before/after cross-page synthetic regression: previous 712-unit leg now at most 60 units. Other comparable original screenshot geometry metrics were not captured; do not invent baseline measurements.

## Verification

- Full Component Release tests: **879 passed, 0 failed, 0 skipped**.
- Desktop Release build: **PASS**, final incremental invocation reports 0 errors/0 warnings; earlier compile warnings remain in raw logs and were not treated as defects fixed by this task.
- Focused Python planner/layout/IR/golden tests: **42 passed**.
- Full Python regression: **504 run, 503 passed, 1 ERROR**. The existing private external LRDU audit-fixture dependency still prevents `test_package_is_staging_only_and_has_hd1_and_all_gaps`; the suite is **not PASS**.
- Six golden families are represented: source/conversion, communication hub, IO/module, point-to-point cable, 1-to-2/3/4 physical multi-end with unchanged electrical chain, Heavy Duty 1/19/20/55/76-contact continuation.
- RED/GREEN: missing preview continuation catalog test first failed; local cross-page regression first failed with length 712. Both passed after implementation.
- Full regression caught duplicate-endpoint input throwing during grouping; implementation now preserves fail-closed diagnostic behavior and full .NET rerun passes.
- Both repositories `git diff --check`: **PASS**.
- AutoCAD/accoreconsole/DWG generation: **NOT_RUN**, intentionally prohibited in this continuation.

## Gap Manifest and Evidence

`DRAWING_QUALITY_BLOCK_SYMBOL_GAP_20260922.json` is an engineering-blocked inventory, **not** a ready-to-approve archive queue. It includes exact endpoint/source identities, roles, page usage, known catalog manufacturer/model, registered candidate asset revisions/SHA, and missing evidence.

Per representation: 1 APPROVED_EXACT, 52 SAFE_GENERATED_PREVIEW, 63 NEEDS_BLOCK_ARCHIVE_FOR_FINAL_VISUAL, 18 ENGINEERING_EVIDENCE_BLOCKED. Eighteen representation entries correspond to eleven owner-level blockers, not eighteen distinct components. The exact K7L approval remains unchanged.

Candidate search is limited to existing SymbolArchive exact ComponentId/Role records. Unbound library filenames are not claimed as candidate identity evidence. No broad library migration or approval was performed.

Private local evidence directory basename: `task013-drawing-quality-overnight-20260922` (under the PO's local evidence folder; absolute paths intentionally not published).

- `block-symbol-gap-manifest.json`: SHA256 `69D9E02DB2C0F8B3277E584826D40ED021A52C9DEC882B98D85E6EBB2CAE8A39`.
- `quality-metrics.json`: SHA256 `3639493D0A009EFF7DC0A07B16B9F2A54D7CC7828F94352578D4D7A584195CC8`.
- Planning input file SHA256: `091C72D696D574745020BFB0973C3B1A9694DD6C090BE6059B82F50A39A10312`.
- Drawing Plan file SHA256: `FD8D67B236A26D594F5B20997923B349599CF03CB6EA43855A7B9E106B3347E2`.
- Raw test/build logs, protected pre/post evidence, 32 WPF page PNGs, local audit harness and metrics script retained locally. These hashes identify file bytes, not contract canonical hashes.

## Protected State

The four snapshotted immutable inputs retained exact size/SHA256:

| Input | Bytes | SHA256 |
| --- | ---: | --- |
| Production SQLite | 101892096 | `540BFD1DD421AC0CCA6E4B66E9E2C9132464E75E363A55AC8E072E0E7C5C7C1B` |
| Central workbook | 68130 | `7CD84A26F78F6FA6DF03A4C7193568C4700558C890BCF424958099B02B64CB16` |
| ACADE handoff README | 22672 | `A1F6135B1BFBD33CEC288401D75A2156AF39A8796CE50F64793B30F22B80DCA0` |
| Reference PDF | 3085865 | `4C4CDC244BE78584D8FB7F276B5A0F00C6CCAA0D6A3CDEB946D29804D4377581` |

K7L rev-001 current SHA matches accepted authority: `217A2AC8BDA3418D4E77D1A4D51015AAD7FC93FE819420EF0279EEFFFFBF3828`. No formal CAD/library file was written. There was no new comprehensive hash inventory of every formal DWG; do not misstate that as measured pre/post equality.

## Complete Continuation Path After Engineering Confirmation

1. Resolve the exact three component instances and required endpoints through existing Engineering Review; leave ambiguous choices unset. Supply seven real Custom Pin/Core mappings and explicitly handle the orphan Unknown cable. Do not change protected SQLite directly.
2. Establish explicit current power distribution/bonding/conversion evidence and the intended Heavy Duty contact-row/pairing authority where required. Do not substitute the old PDF's circuits.
3. Repeat the adapter audit on a disposable copy. Verify all endpoint identities, cable mappings and physical/electrical separation. Recheck grouping evidence without inventing zones, buses or series chains.
4. Rerun all six golden families, focused/full regressions and Desktop build. Continue correcting any implementation failures; the external engineering gate is not an excuse to accept code defects.
5. Regenerate and visually inspect every actual WPF archetype. Finish the power source/distribution/consumer visual acceptance, port/interface captions and real Y-detail check when the project contains confirmed physical Y data. Reconcile preserved Manual/Locked edits explicitly.
6. Recheck geometry, route locality, readability, source invariance and protected hashes. Zero geometric collisions must not substitute for engineering reading order.
7. Regenerate the gap inventory with approved/candidate identities pinned, keep generated Preview separate from approval, publish final evidence on these same branches, confirm remote SHA/tree and existing Draft PR status.
8. Only if all readiness gates genuinely pass, set `READY_FOR_BLOCK_ARCHIVE = YES` and stop for the PO's Symbol/Block Archive work. No final AutoCAD generation or block approval is authorized by this overnight continuation.

`NOT_RUN != PASS`. `APPLIED != VERIFIED`. No final UAT acceptance claimed.
