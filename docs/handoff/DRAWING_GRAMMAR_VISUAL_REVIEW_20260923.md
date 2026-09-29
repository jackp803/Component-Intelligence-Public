# Task-013 Drawing Grammar Continuation

Authority: same CODEX-W1-20260922-013, same Component/Auto branches and PRs. The user's approved Drawing_Grammar_Editor_20260922.md supplements current contracts. Historical handoffs are preserved. No rollback to the source-observation SHA.

## Direct Schematic MVP WIP Checkpoint (2026-09-24)

### Owner Decision Update: 2026-09-29

AH native direction retest (d1985a9 plus preserved WIP): AG overlap reproduction
was saved through normal UI before normal process close. AH uses a fresh isolated
copy; DB/workbook paths were visibly verified. In a new synthetic project, normal
Cable Library import/place -> common Pin -> bend -> double-click free end -> select
and drag cable now leaves the common contact outward, without the previously
observed inward/body-hidden lead. R rotation to 90 degrees kept the attached lead
outward above the rotated cable. Normal Save completed with schema 0.7. Local
screenshots ah-outward-route-after-move.png and ah-outward-route-after-rotation.png
are fresh AH evidence, not AF/AG images. This bounded synthetic geometry retest is
PASS; independent saved endpoint readback, complete route/alternate-representation
workflow, company-template PDF/print and overall product acceptance remain open.
AH is the currently running candidate; its saved synthetic project is recorded in
the private checkpoint. No company asset or mapping approval was made.

AG continuation (416128d plus preserved WIP): AF was normally closed, then AG was
launched with a fresh disposable copy and visibly verified isolated DB/workbook.
The old two-bound-contact project now shows 2/3 in the normal selection sidebar;
local screenshot ag-missing-contact-coverage.png. The saved AF three-contact
project loaded in the new process with its text and contacts intact.

Normal Wire tool: common contact -> orthogonal bend -> double click at blank space
created an unfinished wire. Moving the cable exposed a route entering its body;
this is NOT a route-quality PASS. Screenshot ag-cable-move-route-overlap.png.
Root cause: INSERT attributes lost the CAD contact exit direction while standalone
ATTDEF preserved it; cable binding defaulted missing direction to Right. A new
import regression failed in five cardinal/mirrored cases. INSERT contact direction
now respects scale/rotation; non-cardinal/non-planar cases remain unknown, not a
guessed source identity. All six cases pass; full Release 1123 PASS; Desktop Release
zero errors/16 warnings. Existing saved geometry is not silently rewritten.
Next: freshly import the fixture in the next candidate, verify outward route after
move/rotation, and explicitly preserve/read back endpoint identity. AG remains the
pre-direction-fix running build with the unsaved overlap reproduction. Save that
disposable checkpoint before replacing it. Native direction-fix, PDF and complete
cable workflow remain NOT_RUN/PARTIAL. No company engineering approval is implied.

Native AF (71bc1ca plus preserved Drawing WIP) supersedes the pending cable
text/contact checks below only. The owner handed back the isolated window. Normal
UI set Reference, length and specification, saved, and loaded the synthetic project.
The imported tagged CAD text displayed the saved values instead of template text.
Selecting the omitted third contact then Cancel left it unbound on dialog reopen,
with no Undo entry. Explicit binding Apply added the third contact; one Undo removed
it and exhausted the Undo stack, one Redo restored it. Save -> close editor -> reopen
from the main window -> load the same project retained all three contacts and text.
Internal mapping remained unconfirmed and the asset remained unapproved. Local
screenshots: af-three-contacts-applied.png and af-close-reopen-loaded.png in the AF
private evidence directory. This is editor close/reopen, not process restart proof.

The same native check exposed misleading 2/2 contact coverage for a three-Pin cable.
After extracting the existing summary calculation, a regression failed with expected
(2,3), actual (2,2). Cable coverage now counts all external instance PinIds and only
distinct confirmed matching anchors; missing/foreign/duplicate anchors cannot inflate
coverage. Component partial-representation coverage is unchanged. Fresh full Release:
1117 PASS, zero failed/skipped; Desktop Release zero errors/16 warnings; diff check
PASS. This summary fix is newer than AF and still needs candidate UI verification.
Production DB/workbook/K7L/company DWT hashes match recorded protection baselines.
Overall PARTIAL: native cable wire attachment/alternate representation, process
restart, complete company-template PDF/print and contained-material inventory remain
open. No Auto execution, new asset approval, merge, release or visual acceptance.

Native AE continuation: owner closed AD and explicitly handed back the test
window. AE visibly used isolated DB/workbook. Normal Cable Library showed the
synthetic unapproved Y fixture without preselection, 2/3 contact bindings and
unconfirmed internal mapping. Explicit fixture Custom authority was displayed.
Placement preview -> Escape returned to an empty page with Undo disabled. A fresh
selection and canvas click placed the CAD geometry through normal UI.

This exposed a real defect: INSERT.Explode converted attribute text into plain
text, losing AttributeTag, so archived cable text fields could not bind. A fresh
regression failed with expected TAG1, actual null. Import now preserves visible
tagged attributes before flattening and suppresses duplicate exploded text;
hidden attributes remain contact inventory only. Full Release 1116 PASS; Desktop
Release zero errors/16 warnings. AE remains the pre-fix build; fixed-build native
text/Save/reload/PDF checks are still required. No completed cable workflow claim.

Physical cable count checkpoint: DerivedBomEngine previously returned zero metres
for an archived cable instance with unknown length. Fresh regression reproduced
this failure. Archived physical cables now each contribute one EA, independent of
length or representation count. Pending length remains LengthPending; a filled
length does not assert engineering completion. Historical bulk cable metre rules
are unchanged. Tests cover two physical instances, three representations of one
instance and SQLite reload without duplication. Full Release 1116 PASS; Desktop
Release zero errors/16 warnings. Production DB, central workbook and approved K7L
hashes match the recorded protection baselines.

Native observation this checkpoint found old candidate AD still in the PDF Save
dialog, with SearchEditBox focus. No input was sent while awaiting the requested
human save action. This observation is not native PDF completion, nor current-build
UI verification. Full contained-material inventory/accounting remains unfinished;
this change only fixes physical cable quantity. Overall PARTIAL.

Cable contact/text checkpoint: the existing binding action now lists every
archived cable external Pin, including omitted bindings, with Port/Pin labels and
explicit CAD-contact choices. Removing a contact with an attached wire is rejected;
internal mapping is unchanged. Clearing a grid choice commits cell and row edits
before refreshing. Native interaction remains NOT_RUN.

Explicit archived text-field bindings now render Reference, length and specification
from the physical cable instance in the shared canvas/print geometry and DXF path.
Unbound CAD text is preserved. Missing length/spec remain draft placeholders;
missing text fields use a separate caption rather than guessing CAD attribute intent.
Caption collision/rotation appearance still needs current-build native visual review.

Fresh full Release regression: 1115 passed, zero failed/skipped. Desktop Release:
zero errors, 16 warnings. Verification includes the preserved older dirty Drawing
Planning WIP, not a claim of clean-commit isolation. Current changes are not in old
candidate AD. Native PDF save assistance remains unconfirmed; no new native PASS
is claimed. Material accounting and complete native cable/print workflows remain
open. Overall PARTIAL; no production or approved archive writes authorized.

Archived cable instance editor continuation: selecting a cable-owned CAD symbol
and invoking Cable Settings (C) or Cable Detail now opens a direct physical-cable
draft editor, not the historical conductor picker. Reference, nullable length,
specification, construction and exact source-Pin mapping are editable. Pin choices
show Port/name/number context, not IDs alone. Mapping rows begin without endpoint
preselection. Invalid/self/duplicate mapping is rejected by the domain factory.
Changing mapping marks only that instance pending; the shared archive is untouched.
Unchanged length preserves Imported/Mechanical provenance; changed length becomes
User, empty length remains Unknown. Cancel has no Apply call; unchanged Apply does
not add an Undo operation. These are code/service guarantees pending native proof.

Shift+P on a cable now places another representation of the same physical cable,
including its saved CAD geometry/contact bindings, with a new representation ID
and unchanged cable count. The placement is cancellable and uses normal Apply.
Fresh tests include same-cable representation identity and instance edit isolation.
Full Release 1114 PASS; Desktop Release zero errors/16 warnings; diff check PASS.
Native Cable Settings / mapping / Cancel / another-representation operation chains
remain NOT_RUN, not PASS. Remaining: omitted-contact binding UI, geometry text
field presentation for cable length/spec, material accounting and native full
workflow/PDF/print checks. Existing AD is not the current tested build.


Normal cable-library entry is now wired (native verification NOT_RUN): toolbar
線材庫 / Ctrl+Shift+C reads CableTemplates in the existing SymbolArchive.json.
The archive becomes ci-symbol-archive.v3 only for cable-template content (or an
already-v3 document); v1/v2 histories and component bindings remain supported.
Each entry carries an ArchivedCableTemplate, archive-relative AssetPath, explicit
MillimetresPerUnit, Candidate/Approved status, explicit construction authority with
source evidence, and exact source-Pin-to-CAD-contact bindings. This is NOT a second
archive or an approval action. No production archive was upgraded or populated.

Read-only consumption rejects missing/changed assets, root escape and linked
descendants; selection and placement both check the pinned revision/hash and
manifest content. Rejected/Superseded entries cannot create new instances. The
picker has no template preselection; Unknown remains an allowed draft choice,
while explicit sourced Purchased/Custom is reused without another conductor-level
decision. Imported geometry is previewed before canvas placement. Cancel clears
pending placement; no physical instance is added until the normal Apply operation.
Asynchronous placement is guarded against duplicate clicks, cancellation and page
changes. Approved asset revision/path is carried into the symbol; Candidate stays
unapproved. Internal mapping confirmation remains separate.

Fresh full Release 1113 PASS; Desktop Release zero errors/16 warnings; diff check
PASS. Resolver regression checks read-only manifest bytes, source hash mismatch,
path escape and duplicate template revisions. Still required before delivery:
disposable native library placement/Escape/Undo/save/reload proof; normal archived
cable property/mapping editing and another-representation UI; connection-binding
editing for omitted contacts; material accounting; native PDF/print verification.
Existing candidate AD does not contain these changes. Overall PARTIAL.


Cable-owned CAD representation continuation: SchematicSymbol can now reference
exactly one existing ComponentInstance or archived CableInstance. Shared owner
resolution supports Reference, labels, pins, validation, continuation text, WPF
rendering and saved print/DXF rendering without fake components. AddArchivedCable
is one cloned-project operation; PlaceCableRepresentation references the existing
cable without adding material quantity. Missing CAD contacts remain visible draft
issues rather than invented positions; cable symbols require pinned CAD geometry,
not a generic box. CAD contact tags are persisted alongside runtime/source Pin IDs.
Manual anchor relocation clears its obsolete contact-tag claim and approval claim;
geometry reimport clears stale contact identities. Existing component tests remain
green, including manual anchor override behavior.

Tests cover same-cable multiple representations/shared Reference, rotation with
wire anchor following, attached-representation deletion rejection, deleting another
representation without deleting the cable, full schematic SQLite reload, wrong
asset hash/dual-owner rejection, incomplete contact drafts and input-project
immutability. Fresh full Release 1112 PASS; Desktop Release zero errors/16 warnings.
Native cable placement remains NOT_RUN: the normal cable-library picker and archive
template consumption are still required. Component-only archive actions explicitly
decline cable selection instead of treating a cable as ComponentIR. This is an
integration checkpoint, not a usable cable-library delivery or final acceptance.


Archived physical cable domain continuation (not a completed cable-library UI):
CableInstance now optionally stores a private template snapshot, exact source
Port/Pin bindings with independent runtime endpoint IDs, pinned template/mapping
revisions, explicit mapping evidence/status and instance-only mapping overrides.
External Pin connections resolve without fake ComponentIR and persist through the
normal temporary-SQLite project repository. Whole cable Ports are not conductive
endpoints. No internal ElectricalConnection or Net is inferred from the Y shape,
common ownership or incomplete mapping. Unknown length remains allowed.
Overrides clone only the selected instance and clear mapping confirmation; the
original template and other instances remain unchanged. Invalid source bindings
are rejected by the save/load validation boundary. Missing mapping produces a
review-required engineering diagnostic, not an automatic READY result.

Ruling: ElectricalProject 0.7 is used because older 0.6 readers would otherwise
silently omit cable-owned endpoints/template authority when resaving. Migration
from 0.6 preserves existing objects and leaves legacy ArchivedCable null; no
production migration was executed. Five fresh failing legacy/current-schema tests
proved only expected 0.6 vs actual 0.7 differences: CableAssemblyEvidenceTests
(three cases), SchematicAuthoringTests.LegacyProjectLoadsWithoutInventingPagesOrConnections,
and DrawingPlanPersistenceAndRevisionTests.NewProjectAndV04Migration. Only their
current-version assertions changed, not legacy fixtures or engineering checks.
Six new tests cover independent instances, no inferred mapping, real repository
round-trip, exact external wiring, instance override isolation, malformed mapping,
whole-Port rejection, invalid bindings and 0.6 migration. Full Release 1110 PASS;
Desktop Release zero errors/16 warnings. Older drawing WIP remains included in the
tested worktree and preserved separately.

Still required: reuse the archive UI/storage to supply these templates and CAD
contact bindings, add cable-owner representations to the canvas (without abusing
ComponentInstanceId), normal library add/place/edit/reload UI, material-count
coverage and actual archived asset path/hash verification. This domain DTO/factory
does NOT approve an asset, prove a real company mapping, or make the cable-library
workflow usable yet. Native tests for that workflow remain NOT_RUN.


Human-readable representation labels: archive review now accepts a display name
separate from the stable RepresentationId. Draft save/reload retains the name;
new approved revision creation transports it to the binding, and alternate
representation placement shows the name, stable key, revision and contact count.
Historical records without a name retain the existing default/key fallback.
An exact duplicate approval does not rename an existing approved binding.
TDD: the existing draft round-trip regression first failed compilation for the
missing property, then passed with name retention and archive transport assertions.
Fresh full Release: 1104 passed, zero skipped/failed; Desktop Release: zero errors,
16 warnings. Tests include the preserved older dirty drawing worktree changes.
Native label entry/placement verification: NOT_RUN. Overall remains PARTIAL;
AD is an older running candidate and is not evidence for this UI change.
Remaining priorities are cable-owned endpoints/template authoring, native saved
multi-page PDF/print proof, and controlled standards-backed identification.


Draft gap visibility continuation: F8 / 草稿缺口 opens a read-only page-grouped list
of missing company template, unapproved representation, unconfirmed anchors and
each unfinished wire end. Reference/display/model and endpoint labels are used;
normal navigation selects the corresponding sheet/object. Sorting cannot redirect
navigation by stale row index. The list explicitly does not confer engineering
approval and is not yet exhaustive for cable internal mapping. Service tests prove
both free tails remain draft and project bytes are unchanged by inspection.
Fresh full Release: 1104 passed; Desktop Release zero errors, 16 warnings; diff check
PASS. Native F8/navigation remains NOT_RUN; running AD is not this build.

Cable-authoring source audit (not implementation completion): CableInstance currently
has identity/reference/length/construction/core assignments, but no own external
Port/Pin set or pinned template/mapping revision. SchematicSymbol and authoring
validation currently require a ComponentInstance; endpoint connection resolution
enumerates only component pins and terminal points. A physical cable therefore
cannot be made connectable merely by rendering a CAD asset or creating fake
ComponentIR. Next work must add cable-owned endpoint authority, extend exact
endpoint lookup/validation and schematic owner handling, pin template/mapping
snapshots, then expose normal cable-library placement and instance-only edits.
Reuse existing archive and CableInstance ownership, preserve old projects, and
prove material counts and unknown mapping do not imply internal continuity.

Archive draft variant continuation: existing review grid now exposes representation
identity; review details include that identity. Save/reload preserves it while
resetting confirmation and retaining Unapproved status. Approval batch preflight
validates the stable key and passes it to the scoped approval service. Variant
drafts use review-draft version 2; old default drafts remain version 1 and both are
readable. A temporary fixture proves round-trip and explicit approval routing;
no real symbol was approved. Fresh full Release 1101 passed, zero failed/skipped;
Desktop Release zero errors, 16 warnings. Native archive draft/variant selection
and placement acceptance remain NOT_RUN. Human-readable representation naming
can still improve beyond the current stable-key field; this is not final UX signoff.

Another-representation placement continuation: the direct editor now has a separate
selection-panel action and Shift+P. It requires a selected existing physical
component and explicitly selected Approved archive representation (no preselection).
The existing CAD import path loads geometry; placement preview is a cloned project.
Click commits one representation through the normal Apply/Undo path; Escape clears
the pending object. Before commit, the resolver rechecks exact representation,
revision/path/hash/bindings. The new service retains the physical instance and
exposes only pins included in that approved representation's explicit mapping.
ArchiveRepresentationId persists with schematic symbol revision/hash/geometry.

The default Drawing asset bridge was corrected after a failing regression: a
coil-only archive must not count as an available default asset. Explicit variant
resolution is separate. Fresh full Release: 1100 passed, no failed/skipped;
Desktop Release: zero errors, 17 warnings (includes the existing core nullable
warning on this build); diff check PASS. Native selection/placement/Escape/Undo/
save-reload remains NOT_RUN. Running AD has not been replaced, PDF assistance is
still pending, and the requested output file remains absent. Archive draft UI
variant naming/selection and remaining cable authoring are still incomplete.

Multi-representation authority continuation: archive bindings now have an explicit
RepresentationId (legacy default = `default`). Approval/reapproval and resolver
selection are scoped to ComponentId + Role + RepresentationId. Nondefault assets
use separate immutable representation folders; updating coil does not supersede
contact/default. Missing explicit variants never fall back to GeneratedGeneric.
No actual company approval or production manifest write was performed.

Compatibility: old manifests without the field remain v1/default and read without
rewriting bytes. Saving nondefault variants uses ci-symbol-archive.v2 so old readers
reject rather than misinterpret a coil-only archive as a default symbol. Electrical
project and Drawing IR schema versions are unchanged. New representation keys are
explicit lowercase stable identifiers, not inferred from model/filename/geometry.
Tests observed missing API failure, then isolated-revision/resolver PASS; explicit
schema-boundary assertion failed before the v2 guard and passed after correction.
Six added cases cover independent revisions, unknown selection, historical input,
and invalid/path-like keys. Fresh full Release: 1097 passed, zero failed/skipped;
Desktop Release: zero errors, 16 warnings.

This is service-layer progress ONLY. Remaining: archive draft/editor variant fields,
human-readable variant selection, propagation through approved schematic geometry
authority, normal placement of another representation, saved-document reload and
native UI verification. Do not mark the owner's multiple-representation workflow
complete. Running AD remains unchanged with the owner-assisted PDF dialog pending.

Latest PDF continuation (local WIP, supersedes the earlier NOT_IMPLEMENTED
checkpoint below): schematic PDF/print commands are now connected to awaited
normal project Save/Revision and repository reload, then the same RenderPage
implementation used by the canvas. Output retains saved page order and dimensions,
suppresses editing handles, and carries a draft/incomplete footer. Current PDF
implementation uses 300-DPI raster pages, not selectable-text/vector output.
Physical printer margin compatibility and complete missing-evidence reporting
remain open; neither print nor PDF has native PASS yet.

Subsequent print-margin correction: query oriented media and imageable area from
the selected print ticket. Require matching actual-size paper/orientation, then
check rendered ink outside the imageable bounds instead of rejecting blank paper
margins. Missing capabilities or any clipped content fail before PrintDocument;
no automatic scaling/cropping. Seven new raster safety tests cover all margins,
white/transparent pixels, stride padding, and invalid inputs. Initial missing-helper
compile failure was followed by seven passing tests. Full Release now 1091 passed,
zero failed/skipped; Desktop Release zero errors, 16 warnings. This correction is
NOT in the running AD copy and has no native printer PASS. Do not replace AD while
the owner is assisting its pending PDF dialog.
Production DB, central workbook, approved K7L source, and company DWT SHA256 values
were rechecked and match the recorded protected baseline. Output file was absent
at last check; native PDF remains pending, not completed.

Candidate AD was launched against a fresh disposable copy, not production.
Desktop DLL SHA256: A9EAE96293A095ACAE8111316797D8F1F6F472D5D53264DFC8976BFB1043DECB.
Core DLL SHA256: 64A60A6E03F877EB35BA5B0EBF647924FD87E3368492092444E2AD4426AAE37A.
These identify a dirty-worktree build based on dc779da; they are not a clean remote
commit identity. The pre-existing launcher still points at AC, not AD.

Normal Desktop Load successfully opened the existing four-page disposable test
project, followed by the PDF Save dialog. Native automation then reported
`element 249 is not available in cached app state`; a refreshed coordinate focus
attempt still reported the search field rather than the filename field. Input
was stopped and the owner was asked to enter the isolated output filename and
press Save once. Pending assistance/output verification: NOT_RUN, not PASS.
If completed by the owner, record the export interaction as HUMAN_ASSISTED.
No direct database/API substitution for this native export test is permitted.

Fresh narrow tests after the latest shared-footer build: 13 passed, zero failed
or skipped (print integration, approval service, navigation filters).
git diff --check: PASS (checkout line-ending notices only). Earlier full Release
gate: 1084 passed; Desktop Release build: zero errors, 16 warnings. These are
automated evidence, not native PDF/print or overall engineering acceptance.

PDF prerequisite (local WIP): normal SaveProject_Click now awaits a shared
Task<bool> SaveProjectAsync, returning false for unfinished gesture refusal or
caught save failure and true only after the existing save/revision path finishes.
No PDF command is connected yet. Source-boundary test observed RED then GREEN;
navigation suite 5 passed; Desktop Release build zero errors, 16 warnings.
This is not runtime failure-injection proof or completed PDF/print behavior.

Archive mapping identity correction (local WIP): identical file hash alone is
no longer an exact duplicate approval. Source type and normalized explicit
endpoint/contact pairs must also match. An explicitly confirmed different mapping
uses the existing new-revision path; prior asset bytes and binding records remain
unchanged (normal approval-status supersession still applies). Synthetic temporary
archive regression observed RED with old behavior, then verified distinct mapping
revision and exact-repeat reuse. This does not approve any company asset, implement
cable instance overrides, or solve simultaneous coil/contact representation variants.
Additional regression verifies reordered identical bindings reuse authority without
rewriting the manifest, and unconfirmed changed mappings leave manifest bytes and
revision count unchanged. Fresh SymbolArchive suite: 37 passed, zero failed/skipped.
PDF dependency audit: Desktop already references PDFsharp-WPF 6.2.4; no new paid
dependency is necessary. Existing TopologyPdfExporter is not the new schematic
output and must not be used to regenerate topology layout. Multipage schematic
PDF/print remains NOT_IMPLEMENTED, not satisfied by this dependency discovery.

Fresh regression checkpoint: full Release initially 1078 passed / 1 failed in
ConsolidationRebindsExistingDetailPagesWithoutDeletingPagesOrElectricalTruth.
Root cause: the historical consolidation service removed old CableInstances but
left detail-page bindings pointing at those retired IDs. It now rebinds only pages
whose cable is actually retired, preserving page identity/title and connection
truth. This is legacy compatibility, not adoption of generated cable appearance
as the new authoring workflow. Focused multi-end tests: 21 passed. Fresh full
Release after repair: 1079 passed, zero failed/skipped. No native UI acceptance
is claimed. Earlier unresolved-regression notes below are historical, superseded
by this checkpoint; remaining user-workflow gaps are not superseded.

Multi-representation baseline audit:
- Added DeleteRepresentation command and Delete-key dispatch for selected symbols.
  Confirmation explicitly retains the physical instance and other representations.
  Locked symbols or any attached draft/connected wire prevent deletion before
  mutation; native status explains the need to resolve attachments first.
  Service regression covers original-project immutability and retained identities.
  Tests initially could not compile because the command did not exist; after
  implementation, all 34 SchematicAuthoringTests passed. Desktop Release build:
  zero errors, 16 warnings. Native deletion/Cancel/Undo verification NOT_RUN;
  this remains local WIP, not a published or UI-accepted candidate.
- Existing PlaceSymbol can reference one physical ComponentInstance from two
  pages using distinct confirmed Pin subsets. New regression verifies shared
  Reference after JSON round-trip, unchanged physical component count, no added
  Connections/Nets and unchanged input project. This is existing service behavior,
  not a newly completed user workflow or approved-asset consumption proof.
- Current normal palette placement still calls AddCatalogComponent, creating a
  new physical instance. It does not yet expose the required distinct action to
  place another archived representation of an existing instance. That UI/archive
  selection gap is open; do not present the passing service test as native PASS.

Standards discovery (official public metadata only, no purchase):
- IEC 60445:2021, edition 7.0, conductor/terminal identification:
  https://webstore.iec.ch/en/publication/66712
- Its official consolidated successor is IEC 60445:2021+AMD1:2026 CSV,
  edition 7.1, published 2026-01-28:
  https://webstore.iec.ch/en/publication/111816
- Machinery-specific scope: IEC 60204-1:2016 and AMD1:2021:
  https://webstore.iec.ch/en/publication/26037 and
  https://webstore.iec.ch/en/publication/66124
- Diagram/document presentation: IEC 61082-1:2014:
  https://webstore.iec.ch/en/publication/4469
Exact colour clauses and amendment effects are NOT_VERIFIED from these public
catalogue pages. No installation compliance or normative palette is claimed.
The misleading IEC60445-2021-DC-PREVIEW identifier was replaced by
PRODUCT-SCHEMATIC-PREVIEW-V1; existing colours and electrical data are unchanged.
Regression observed RED before the identifier correction. This does not complete
the controlled identification rules or monochrome readability acceptance.

The six owner answers now supersede the unanswered proposals recorded below.
Full requirements are preserved in the existing direct-schematic MVP plan's
2026-09-29 section. Cable instances versus representations, mapping-version
authority, explicit wire/continuation semantics, shared component representations,
and editable project plus draft-capable multipage PDF/print are required scope.
Terminal branch circles need one narrow clarification about presentation versus
automatic physical terminal creation. No new implementation or native acceptance
is claimed by this requirements checkpoint. Existing WIP remains preserved.

### Latest Continuation: 2026-09-28 (PARTIAL)

#### Owner Workflow Correction And Unanswered Decisions

- Bounded navigation correction: startup now always selects SchematicTab, even
  when historical projects have no Schematic document. No automatic topology
  conversion or electrical-data mutation was introduced. Source-level regression
  observed RED (missing unconditional selection), then navigation suite GREEN
  (4 tests). Native startup verification is NOT_RUN for this edit. Legacy tabs
  are still present pending decoupling; this is not complete UI consolidation.
- Owner explicitly requires direct electrical schematic authoring only: no
  Topology authoring workflow and no second Planner rearranging the user's work.
  Internal engineering connectivity and historical data remain preserved.
- Custom cable appearance comes from owner-authored, archived CAD blocks. The
  conductor ownership picker and generated generic Y illustration are not the
  required primary authoring experience. Do not continue expanding that path as
  though it satisfied this requirement. AutoCAD remains auxiliary asset authoring,
  not a required project output/execution gate.
- Pending questions are proposals, NOT approved requirements: physical identity
  on each cable-block insertion; reusable versus per-instance mapping; explicit
  branch creation when terminating on an existing wire; multiple representations
  of one physical component; terminal insertion interaction; initial print/PDF
  delivery behavior. Do not silently implement the assistant's suggested answers.
- Current HEAD rechecked: ae85c6dd8f0606cb5a9e1d66a95cb6db570dbeb3.
  Uncommitted draft-route helper/UI changes and the consolidation/detail-page
  regression remain local WIP, along with seven older preserved WIP paths.
  They are not represented as published branch functionality.
- Fresh focused Release check: SchematicDraftRouteTests, 6 passed, zero failed
  or skipped. This covers draft orthogonal tail/retrace behavior only, not native
  interaction or product readiness. No fresh full-suite/build/UI claim here.
- The added consolidation/detail-page regression is still unresolved; earlier
  full-suite PASS records below do not establish current full-suite PASS.
- Native input remains stopped after the owner's conductor-picker clarification.
  No additional UI operation, production mutation, or asset approval occurred in
  this checkpoint. Overall PARTIAL; owner visual acceptance remains PENDING.

#### Candidate AB: Approved K7L Native Consumption And Selection Safety

- Normal WPF on the existing disposable project: add company-frame page ->
  place catalog K7L -> choose existing approved Schematic rev-001 -> read isolated
  CAD copy -> 8/8 confirmed contacts. No new approval or archive revision.
  Exact asset SHA-256 remains
  217A2AC8BDA3418D4E77D1A4D51015AAD7FC93FE819420EF0279EEFFFFBF3828.
- Set instance Reference, draw from approved OUTPUT_5 to a free endpoint, move,
  rotate 90, Save, normal process close/restart and Load. Candidate AA and AB
  both reopened the same geometry, revision and eight explicit source PinIds.
  This is a pin-bound unfinished draft, not a complete ElectricalConnection;
  no false claim of complete circuit validation.
- Native Z exposed duplicate Reference (CAD TAG1 plus external label). Shared
  presentation now omits the external label only for rendered TEXT/MTEXT TAG1.
  Catalog/unmapped/unsupported primitives retain the label. Source CAD untouched.
  Regression red: two assertion failures; green: all five cases.
- Native AA exposed a selection bug: focusing the sheet scrolls its origin,
  and a plain click could become a movement with an unwanted Undo entry.
  AB gates dragging on viewport pointer movement using Windows drag thresholds.
  Fresh native AB: same load/page/zoom/click sequence leaves Undo at the prior
  synchronization operation; actual drag moves symbol and attached route;
  one Undo restores both. Lock -> attempted drag leaves geometry/history intact.
  Save succeeded. This WPF-specific fix was reproduced and verified natively.
- Private screenshots: k7l-approved-eight-anchors-z.png,
  k7l-approved-bound-wire-move-z.png, k7l-approved-bound-wire-rotate-z.png,
  k7l-single-reference-restart-aa.png, k7l-click-no-undo-ab.png,
  k7l-locked-rejects-drag-ab.png. No private images/assets uploaded.
- Candidate AB Desktop SHA-256:
  1A88CDAB05395568D6C8C2D06B8D795389B3D4E6E59B95144469B36C55208A49;
  core: 34C89202DBAF279334304A2789E61B74417E7A01409A630FF756E30F505147DC.
  Existing private launcher pins these hashes and the isolated DB/workbook.
  Build includes the seven preserved older WIP paths; not a clean-commit build.
- Fresh full Release regression: 1067 passed, zero failed/skipped. Desktop
  Release: zero errors, 16 existing warnings. Diff check PASS (CRLF advisories).
  Protected production DB, workbook, source K7L and company DWT match the
  previously recorded SHA-256 and sizes. Auto runtime unchanged, no CAD output.
- Overall remains PARTIAL. Multi-end cable-detail native workflow/company
  appearance, remaining editing/route-quality gates and owner visual acceptance
  remain open. The small route spur observed while drafting near grid/anchor
  alignment still needs investigation; no whole-product READY claim.

#### Candidate Z: Rotated Pin Labels And Locked Manual Route (Fresh Native Proof)

- Scope remains direct schematic authoring. Owner reconfirmed no AutoCAD project
  output gate; AutoCAD is auxiliary template/block authoring/import only.
- Completed the pending x near-pin retest below: a click four displayed pixels
  beside the first catalog pin bound the exact endpoint. Move -> rotate 90 ->
  one Undo -> one Redo -> Save -> normal process close/restart -> Load retained
  the attachment, symbol pose and route. Private anchor-move-x.png,
  anchor-rotate-redo-x.png and anchor-reload-x.png record the normal UI path.
  No source-location approval: all four catalog anchors remain unconfirmed.
- Native x exposed overlapping labels after rotation. Shared label pose now
  rotates the offset/text with the symbol, reversing the attachment at 180/270
  so text is not upside down. Existing optional DXF draft uses the same pin-label
  pose; this is not a CAD execution/output acceptance gate.
- Native y proved separate labels at 90/180, then found a 270-degree Reference
  collision. Desktop now measures the displayed labels and places Reference
  above their top extent. Native z loaded the saved 270-degree symbol and proved
  the reference/label clearance. Optional DXF Reference clearance is not covered
  by this measured WPF adjustment; no full export visual-equivalence claim.
- TDD: missing method, then four actual assertion failures with the original
  fixed-horizontal pose; corrected pose green. Added four measured-top cases.
  Fresh full Release: 1062 passed, zero failed/skipped. Desktop Release: zero
  errors, 16 existing warnings. git diff --check PASS (CRLF advisories only).
- Native z normal canvas: select pin-bound/free-ended draft -> drag middle
  horizontal segment -> one Undo -> one Redo. Both ends stayed fixed. Lock ->
  attempted drag caused no movement; one Undo then undid Lock, proving no extra
  rejected-drag history entry; Redo restored Lock. Normal Save -> close editor
  -> reopen editor from main -> Load -> select third page/wire retained the
  manual geometry and locked state. This z reload was same-process/new-editor;
  x independently exercised complete process restart. Do not conflate them.
- Read-only disposable revision inspection confirmed identical WireId and Pin
  attachment, no ElectricalConnection creation (unfinished draft), seven route
  points and Locked=true in Save and subsequent reload/sync snapshots.
- Private z evidence: anchor-label-reference-clearance-z.png,
  anchor-bound-route-drag-z.png, locked-wire-rejects-drag-z.png,
  locked-wire-editor-reload-z.png. No screenshots/private assets published.
- Launcher now pins candidate-20260928-z; Desktop SHA-256
  5BFEB566592DA28C246A578C4F7AF5C2ED2CFBCED2EF08EC28EB5A547CFF3309;
  core 08224C3BAF9A106B95AE4A4D78F8FBBDB56D72258500A3C0B947CAF41ADB4A22.
  Built from b906c74 plus this correction and the seven retained unrelated WIP
  paths, not a clean commit-only build. Auto executable unchanged/not required.
- Production DB, workbook, approved K7L and source company DWT fresh hashes/sizes
  match the protected baseline. No CAD launch, engineering approval or formal
  data mutation. Overall PARTIAL; this does not approve whole-product usability,
  approved-asset consumption, cable-detail authoring or owner visual acceptance.
  Next continue those original normal-UI gates, plus the remaining route/anchor
  editing checks. Preserve existing WIP and the current isolated test project.

#### Pin Hit Target Correction (Built, Native Retest Pending)

- Native w on company-frame third page: placed IFM PL1514 from normal catalog,
  clicked near its first pin at zoom 0.65, drew an L draft, then moved the device.
  The wire stayed behind with a free-end square. FAIL: tiny hit target allowed a
  near miss to silently create a free attachment. Private anchor-near-miss-w.png
  records this. These latest w UI changes are unsaved disposable state.
- Root: only the 7-canvas-pixel marker handler bound a Pin; surrounding canvas
  unconditionally used Free with grid rounding. Added same-page nearest-anchor
  hit resolution from the unrounded pointer, within 8 displayed pixels adjusted
  for zoom. Equidistant hits fail with an explicit prompt; no list-order choice.
  Exact endpoint/anchor identity is retained, including rotation; Confirmed is
  never promoted by snapping. Free points outside tolerance remain supported.
- TDD RED missing resolver, then four tests GREEN; full Release 1053 passed,
  zero failed/skipped; Desktop Release zero errors, 16 existing warnings.
  The running launcher still selects w, which does NOT include this correction.
  Next: save/close or discard only authorized disposable w test state, package
  new candidate, repeat near-pin click -> wire -> move/rotate -> Undo/Redo ->
  Save/Close/Load with normal UI. Do not report native PASS before this rerun.
  All prior unrelated WIP remains. Overall PARTIAL.

#### Candidate W: New Page Inherits Active Format

- Fixed the native v usability gap below. AddPage accepts an explicit source
  page, clones the project, reuses its format with a new page identity/title and
  clears CableDetail. It does not duplicate any symbols/wires/continuations.
  Desktop passes the active page. No source specified retains default first-page
  behavior; a missing explicit source fails closed instead of silently defaulting.
- TDD RED: missing overload; then one assertion incorrectly compared nested
  collection object identities after JSON cloning. Corrected to serialized asset
  content equality without weakening format/source independence checks.
  Fresh full Release 1049 passed, zero failed/skipped. Desktop Release zero errors,
  16 existing warnings. Existing unrelated WIP is retained.
- Native w: closed v normally, verified isolated DB on w main window, loaded the
  existing test project, selected company-frame page, clicked Add Page. New third
  page visibly retained frame/logo and had no duplicated device or wires. Normal
  Save -> Load retained the empty company-format page. Same-process reload PASS;
  w new-process reload NOT_RUN. Screenshots new-page-company-format-w.png and
  new-page-company-format-reload-w.png remain private.
- Launcher selects w; Desktop SHA-256
  A79B09252BCBDC6D2BD168D9698F002B9677D0D442D49E8659D26C79B93A9DC1;
  core 708C27FABD95A96E0370A3F5DD1E5E8819A96C0D41AFB93DB98E05E14D3DC784.
  Next continue anchor-bound routing/editing and archived block consumption
  gates. Overall PARTIAL; no whole-product readiness or visual acceptance claim.

#### Candidate V: Native Wire / Cross-Page / New-Process Reload

- Fresh normal WPF test on existing disposable company-frame project: Wire
  tool -> first point -> bend -> double-click end created an orthogonal L route.
  One Undo removed the complete route; one Redo restored it. No API mutation.
- Added second page and drew a straight open wire. Cross-page tool -> select
  second-page wire -> switch page -> select first-page wire -> enter explicitly
  synthetic UI-TEST-SIGNAL -> confirm produced a paired continuation. The wires
  remain unfinished at their outer ends, truthfully marked pending connection.
  This is interaction evidence, not confirmed real-project electrical wiring.
- Select arrow -> Go To Other End navigated to page 2. Page Up reordered page 2
  to position 1 and changed its reference from 1/B5 to 2/B5. Save -> normal close
  editor/main -> relaunch identical hash-pinned v -> visibly verify isolated DB
  -> Load retained both routes, new page order, template and both references
  (plain page points to 2/B5; company-frame page points to 1/B5).
- Private screenshots: wire-crosspage-reordered-v.png,
  wire-crosspage-new-process-reload-v.png,
  wire-company-frame-new-process-reload-v.png. No production writes or CAD launch.
- Remaining usability gap observed: Add Page uses a generic frame instead of
  carrying the active page's company template/settings. Next bounded correction:
  reuse the active page format for new empty pages, not its symbols/wires/IDs;
  cover source independence and default blank-project behavior, then native test.
  Full anchor-bound route editing, company-block readiness and overall visual
  acceptance remain separate pending gates. Do not claim the entire MVP READY.

#### Company Template Symbol Display (Candidate V)

- Owner reconfirmed: this application is the final schematic editing surface;
  AutoCAD is auxiliary block/template authoring only. No WDP/DWG execution gate.
- TEXT display now decodes case-insensitive %%p/%%d/%%c and literal %%% in one
  pass. Raw Text remains unchanged; computed display is excluded from JSON.
  Unknown codes remain visible, not silently discarded. MTEXT is unchanged.
  This does not claim full SHX or CAD rich-format fidelity.
- TDD: missing DisplayText produced CS1061, then all seven symbol/source tests
  passed. Fresh full Release: 1046 passed, zero failed/skipped. Desktop Release:
  zero errors, 16 existing warnings. git diff --check PASS (CRLF advisories).
- Native: closed u normally, launched hash-pinned v, visibly verified disposable
  SQLite, opened Electrical Design and loaded the existing company-frame test
  project. Existing saved template now displays plus/minus and degree symbols
  without reimport. Screenshot company-template-symbols-native-v.png is private.
  No source template or engineering identity changes. Full company-frame wire /
  cross-page / edit / save workflow remains pending; this is not overall READY.
- Candidate v Desktop SHA-256
  BC9E73F43B6838FF36D7F6B2DB3DF02AD3F9F15FC15860FE01A7D8A8FBE91E28;
  core 4BD1E3EA229B7A916443F255799F95B46B2A9C10EC5CCAFCFE85C99F8069CAAE.
  Existing local launcher selects v. Preserved older unrelated WIP is included
  in the build but is not part of this scoped commit. Production SQLite,
  workbook, approved K7L and company DWT hashes/sizes remain at baseline.
  Continue native company-frame authoring next; do not restart import work.

#### Company Template Text And Hidden Attributes (Candidate U)

- Read-only source inspection found company grid labels use MiddleCenter and
  hidden INSERT attributes carry internal CAD configuration outside the frame.
  netDxf Explode copies IsVisible but loses the independent Hidden attribute flag.
  Import now suppresses those hidden glyphs before explosion, excludes unrelated
  attribute positions from bounds, and retains hidden X?TERM inventory without
  guessing catalog bindings. TDD reproduced incorrect width 1030 instead of 30;
  fixed regression also proves visible text, contact inventory and source bytes.
- Single-line text/ATTDEF now retains alignment and width factor. WPF measures
  text and applies local alignment, width scale, then rotation at the original
  anchor. Legacy missing alignment defaults to BaselineLeft and width factor 1.
  New alignment/offset tests RED (missing members) then GREEN. This is not SHX
  font fidelity, Fit/Aligned support, or CAD control-code interpretation; those
  and MTEXT rich formatting remain explicit visual gaps.
- Native u: closed t normally, launched hash-pinned u, visibly verified disposable
  DB, loaded same test project, replaced template via normal DWT picker/unit1,
  acknowledged text-format limitations, Apply -> Save -> Load. The bottom-edge
  internal configuration text disappeared; logo retained and grid-label alignment
  visibly changed. Same-process reload PASS; u new-process reload NOT_RUN.
  Local screenshots company-template-text-native-u.png and
  company-template-text-load-native-u.png remain private.
- Fresh full Release: 1039 passed, zero failed/skipped; Desktop build 0 errors,
  16 existing warnings; diff check PASS. Protected DB/workbook/K7L/source DWT
  hashes and sizes unchanged. Earlier unrelated WIP remains uncommitted.
- Launcher now selects u, Desktop SHA-256
  A79B7363AA7D6661E7A823FB609295C919D7DC943F193D80138055C9803A3F3D,
  core 9D5E5FCDB647377630D882652FA0BBDF3C6556C2B1F4EDD02430F8530CF5BAA3.
  Next: supported CAD text symbols/font fidelity and full page editing on the
  company frame, using the existing remaining checklist. Overall still PARTIAL.

#### Company Template Closure Repair (Candidate T, Native Save/Reopen PASS)

- Supersedes the hatch root-cause uncertainty below, not the remaining MVP list.
  Read-only ASCII DXF inspection proved all 19 used company logo paths have
  group 73 = 1. netDxf 3.0.1 loses this flag during reading; its polyline clone
  also omits IsClosed. The apparent closing gaps were adapter loss, not missing
  company engineering evidence. Upstream evidence:
  https://github.com/haplokuon/netDxf/blob/master/netDxf/IO/DxfReader.cs
  and https://github.com/haplokuon/netDxf/blob/master/netDxf/Entities/HatchBoundaryPath.cs.
- Added bounded ASCII HATCH flag restoration by exact entity handle/path order
  before block explosion; linear boundaries become explicit edges before clone.
  No proximity-based closure, source rewrite, or blanket closed-loop assumption.
  Binary DXF does not use this repair. Unsupported curved/style cases remain
  diagnostics. Synthetic direct/block closed cases failed before the fix;
  open-boundary rejection and the closed cases now pass (4 focused cases).
- Real converted company template now imports 1 solid fill with 19 contours.
  Candidate t normal UI: r closed normally, t launched with visible disposable
  DB path; load existing test project, Frame/Page -> Replace -> disposable DWT
  -> unit 1 -> acknowledge remaining TEXT/MTEXT formatting warning -> Apply ->
  Save -> close editor/main -> launch same t -> Load same project. Company logo
  fill visibly retained after new-process reload. No DB/service injection.
  Local evidence: company-template-hatch-native-t.png and
  company-template-hatch-reload-native-t.png in the existing private evidence
  directory. This proves template geometry persistence, not full drawing quality.
- Fresh full Release: 1030 passed, 0 failed/skipped. Desktop t build: 0 errors,
  16 existing warnings. Diff check passed. Production DB, central workbook,
  approved K7L and source DWT size/SHA-256 match existing protected baselines.
- Local launcher pins t: Desktop SHA-256
  A0168B5ED6652C81B61DB89071EA71E7D1698CFE13862C8DCF6A766CB7782313;
  core FC1993EF88B652FC0F8B7489200244D84DC0EB930075140B93A64420C75EC57D.
  The earlier seven dirty/untracked Drawing Planning files remain preserved and
  uncommitted; this candidate is a working-tree build, not a clean-commit build.
- Next: template text alignment/font fidelity and usable native page editing
  on the imported company frame; retain the existing full remaining checklist.
  No AutoCAD project generation/executor gate is required by current owner scope.

#### Solid Hatch Investigation (Candidate S, Incomplete)

- Actual disposable company template DXF contains one SOLID/Normal Hatch with
  19 linear paths. Read-only netDxf inspection found continuous interior edges,
  but all closing edges have endpoint gaps, approximately 0.0076 to 0.1316 mm.
  Local inspection source/output remains private. No source asset was repaired.
- Added exact closed-linear-loop SOLID Hatch primitives with separate contours,
  even-odd WPF fill (holes preserved) and optional draft DXF representation.
  Unsupported styles/curves/open boundaries remain diagnostics, not silently
  dropped partial fills. New synthetic closed outer/hole test passed; fresh
  full Release 1027 passed, zero failed/skipped. Candidate s build 0 errors,
  16 warnings, native NOT_RUN; launcher remains r.
- Actual company fill is STILL NOT IMPORTED because the closure check rejects
  those gaps. This is not a company-template Hatch fix or visual acceptance.
  Next: verify authoritative CAD hatch boundary closure semantics before any
  implicit closing rule; retain final edge endpoints and diagnostics. Do not
  simply increase tolerance or claim the synthetic fixture proves this asset.

#### Candidate R Native Settings Checkpoint

- Fresh r launched after p editor/main closed normally. Disposable SQLite path
  visibly confirmed before entering Electrical Design. Existing company-template
  test project loaded normally. Frame/page action opened settings directly with
  no file picker or CAD conversion. Independent-grid toggle enabled its fields.
- Cancel path: checked the toggle, closed without Apply, reopened; toggle was
  still unchecked. No settings undo entry appeared. This is observed UI cancel
  behavior, not a fresh binary DB comparison.
- Apply/Save/Load/reopen settings: toggle persisted checked with X=10,Y=10,
  width=400,height=277. These are test values, NOT approved company grid mapping.
  Same-process Load PASS; new-process reload and replacement-picker Cancel still
  pending. Local screenshots settings-cancel-native-r.png and
  settings-reload-native-r.png. No broad workflow acceptance is implied.
- Local launcher now pins r Desktop SHA-256
  09B01CB0774E4F504F18BAE2DE5A0E7E2FD3122A54146BCC8F3B86892E52D2C3
  and core BD652F60F8AAAE3969EC8E720C36F18D0129F6A3DCD0BDE0118CA97608A95E75.

#### Page Settings Without Reimport (Candidate R, Native NOT_RUN)

- Frame/page action now opens settings directly. Explicit Choose/Replace
  Template invokes the existing CAD picker; pending geometry is applied only
  with the settings Apply action. Closing the dialog leaves the project intact.
  Conversion blocks concurrent settings input. Nested import dialogs use the
  settings window as owner rather than competing with the main window.
- Added transactional SetPageSettings retaining embedded geometry, source path
  and hash even when the original file is unavailable. Regression demonstrates
  original project immutability and unchanged template geometry; TDD missing
  method RED then GREEN. Full Release 1026 passed, zero failed/skipped; candidate
  r Desktop Release 0 errors, 16 warnings. Native r is NOT_RUN. Do not use p's
  successful reload as proof of these newer dialog changes.
- Next native candidate should be r (includes q grid work). Verify settings
  edit/Cancel, explicit bounds Apply, Save/new-process Reload, and replacement
  picker Cancel. Verified launcher remains p until r hashes are pinned.

#### Explicit Company Coordinate Grid (Candidate Q, Native NOT_RUN)

- Added optional page CoordinateGrid X/Y/Width/Height in sheet millimetres.
  Legacy pages retain the original margin-derived rectangle. ReferenceFor now
  uses the explicit bounds; no net, pin or connection identity is changed.
- Template dialog exposes an opt-in independent grid rectangle with disabled
  fields when not selected and scrolling for small displays. Invalid/nonfinite,
  zero or out-of-sheet rectangles fail validation. No company grid values were
  inferred or approved; the currently saved test page remains unchanged.
- TDD: seven tests for bounds, legacy defaults, exact grid cells, serialization
  and invalid rectangles. Fresh full Release 1025 passed, zero failed/skipped.
  Candidate q Release build: zero errors, 16 warnings. Native q NOT_RUN;
  launcher remains verified p. Next: normal dialog edit/cancel/save/reload and
  paired continuation grid verification, before claiming coordinate acceptance.

#### Owner Scope Correction: Editor Is The Product

- AutoCAD now serves only as the external block/template authoring tool. WDP/DWG
  output and a real execution pipeline are not required delivery gates. Stop
  direct executor development; preserve existing editor and archive work.
- Unpublished execution-compiler preparation and its tests were preserved in a
  local checkpoint, then withdrawn from the worktree. They were never connected
  to product UI. The prior 1020-test run included those four tests; it must not
  be reported as the current editor suite count.
- Priority remains normal company-template import, new-project authoring and
  Save/Close/Reload, with genuine UI evidence. Earlier output requirements below
  are historical. Overall completion and owner visual acceptance remain pending.
- Candidate o native placement/save was exercised after the older checkpoint
  below: both pin columns start independently and fit the test sheet. This is
  fallback readability evidence, not confirmation of physical pin positions.
  Native company-template import/reload remains to be exercised.

#### Fresh Native Template Import (Candidate O)

- Candidate p native follow-up: PASS for the narrow frame-overlay correction
  and normal new-process Reload of the previously saved template/placed module.
  Candidate o editor and main window closed normally. Candidate p launched with
  the disposable DB/workbook/archive, visible SQLite path checked before opening
  Electrical Design. Normal Project ID entry and Load restored the template and
  module; generated grid/footer overlay is absent. Local evidence:
  company-template-reload-native-p.png. This supersedes p NOT_RUN below only for
  this specific flow, not full authoring or company grid/title-block acceptance.
- Candidate p Desktop SHA-256:
  567EEE597CBAA1FF555FEE2B72F934AD210739CEC3F226F10BA15A81D68B07A9;
  core: 525272EB1D76E820D1102E35C44BAD51F921D80CCFBE28EA7D64977DE3C01BF6.
  Local hash-guarded launcher now selects p (implementation e20653c).
- Production DB, central workbook, source company DWT and K7L rev-001 SHA-256
  were rechecked after native reload and match the previously recorded protected
  baselines. No source-template modification or new approval was performed.

- Follow-up correction: WPF and retained optional DXF exchange now omit generic
  frame labels/footer when imported template geometry exists. Default blank
  pages retain their labels. Grid metadata and engineering identities are not
  changed. Regression first failed for imported template, then passed; fresh
  full Release 1018 passed, zero failed/skipped. Candidate p Release build:
  zero errors, 17 warnings. Native p verification remains NOT_RUN; candidate o
  screenshots above/below do not verify the correction. Company grid mapping,
  imported title-field editing and Hatch remain separate open work.

- Normal WPF file picker selected an isolated byte-for-byte company DWT copy;
  source/copy SHA-256 both D5DB332E88F16F18ED82FB3A2982EF2E4970F2438F09522DDEEE51E9F17A03DF.
  CAD conversion was import-only, not project generation. Units 1 mm, test page
  420 x 297 mm, existing default margin/grid values used for visual inspection,
  not asserted as approved company coordinate mapping.
- Import completed with explicit text/font review and unsupported Hatch warnings.
  Template appeared on the existing disposable page and normal Save succeeded.
  Screenshot retained locally as company-template-native-o.png.
- FAIL: built-in frame coordinate labels and footer are still overlaid on the
  imported company frame. Imported grid alignment, hatch fidelity and reload
  are not accepted. Next: shared frame-presentation correction with regression,
  then fresh candidate native import/reload. Do not declare template-ready yet.
- Fresh current Release regression after withdrawing superseded compiler:
  1016 passed, zero failed/skipped. No new Desktop build was needed for this
  documentation-only checkpoint; native candidate remains o, code 817236e.

#### Catalog Fallback Row Correction

- Native n evidence led to a specific root cause: generic left/right anchors used
  one global row index and total pin count for body height. The left side started
  below all right-side pins. New representations now number rows independently
  on each side and size to the larger side. Existing saved representations are
  not relaid out; all endpoint/source IDs and unconfirmed flags are preserved.
- Regression first failed expected 165 versus actual 245 mm for 32 right plus
  16 left contacts, then passed. Fresh full Release **1016 PASS / 0 FAIL / 0 SKIP**,
  `tests/catalog-rows-20260928.trx`; candidate o Release build **0 errors / 16 warnings**.
- Native o placement retest: **NOT_RUN** at this checkpoint. Launcher remains n.
  This correction alone does not prove arbitrary page fit, approved physical
  contact positions or final module presentation quality. Next native step:
  close disposable n normally, pin launcher to o hashes, place the same catalog
  module on a new test page and compare both columns without editing saved data.

#### Portable Catalog Pictures And Candidate N Native Export

- Catalog pictures now use a shared preview/DXF layout, including cardinal rotation.
  The draft ZIP carries decoded preview PNG bytes at content-addressed relative
  paths, deduplicates images and records SHA-256. Missing images remain diagnostics.
  Selection highlighting no longer shifts body geometry away from its anchors.
- TDD covered exact rotated placement, aspect ratio, portable package references,
  hashes and source immutability. Fresh full Release: **1015 PASS / 0 FAIL / 0 SKIP**,
  `tests/raster-native-final-20260928.trx`. Candidate n Release build: 0 errors,
  16 warnings. `git diff --check` PASS.
- Candidate l closed normally. Candidate n launched through the isolated launcher;
  visible main-window DB path confirmed disposable before Electrical Design.
  Normal UI placed a catalog AL1342 and exported a new draft ZIP successfully.
  Native evidence: `ui-20260928/catalog-raster-native-n.png` and corresponding ZIP,
  local only. One page DXF plus one 576x576 PNG and manifest; image hash
  `8DC7E33FA4E23C6E508F4E6D58AA971B8D0CA5455DAE7C703DA08A0294DE5F3C`.
  Export remained `DRAFT_NOT_VERIFIED`, with unconfirmed anchors/template warnings.
- Native observation also exposed a remaining usability defect: the default
  AL1342 pin list extends beyond the visible paper when placed mid-page. Its
  catalog image is present, but default compact layout is NOT accepted. Do not
  infer approved physical anchor placement from this fallback. Address the
  existing placement/layout scope next; preserve all endpoint identities.
- Candidate n Desktop SHA:
  `2036A3DDC51265222ACD1BAE81660DE7BBAAC7FA818AF3B4160138AC21E5BD30`;
  core SHA `0B59EEF3BB4A2A1BAD354B31484DDBC2A128BD7366AE6584E57944881265D342`.
  Local launcher now pins n, paired Auto remains `eb84bee61d562881d861eb17ce990e44f56aff76`.
- Production DB, central workbook, approved K7L rev-001 and company DWT SHA-256
  rechecked unchanged against recorded baselines. No source asset writes.
  CAD opening of the new raster package: NOT_RUN. Full authored-canvas IR/executor
  closure, remaining native editor workflows and visual acceptance remain pending.

#### CAD Text Coordinate Fidelity

- Exact TEXT baseline/rotation reproduction initially failed all four cardinal
  rotations: draft DXF used TopLeft while the imported primitive stores a baseline.
  Export now retains BaselineLeft and fractional insertion coordinates. WPF rotates
  the nominal text origin around the same baseline, rather than an offset corner.
- Three failing MTEXT regressions established that multiline text was degraded to
  single-line TEXT. Export now preserves multiline entity kind, rectangle width,
  attachment and rotation; plain-text braces/backslashes/newlines are escaped.
  Font/format review diagnostics remain: this is not full CAD font equivalence.
- Fresh full Release **1009 PASS / 0 FAIL / 0 SKIP**, Desktop Release candidate m
  **0 errors / 16 warnings**, evidence `tests/text-fidelity-20260928.trx`.
  Desktop SHA `2E30CAD3528E4A1C1C06E8BABBC65D0329FCE63A330F955E750F649BF29D62B9`;
  core SHA `DDBA2469943385E54E364B4DF1162B237457E9E123DF57AD0A2CD1F2F0102B17`.
- Native m retest **NOT_RUN**: the still-open l window returned black capture;
  fresh window selection and one activation recovery returned
  `failed to activate captured window`. Input stopped; owner asked to confirm
  unlocked desktop. Last normal-UI verified launcher remains l. Do not use its
  screenshots as evidence for m's text changes. Other implementation can continue.

#### Linked Cable Detail And Native Persistence

- Added optional linked cable-detail pages in the existing schematic document.
  They reference the existing physical cable/assembly, not duplicated engineering
  entities. Preview and DXF consume the same presentation. Physical sheath/split
  primitives are separate from electrical wires; the mapping table preserves the
  original conductor endpoints, including daisy-chain multi-end graphs.
- Validates ownership, exact endpoint groups, branch indexes, missing sources,
  conflicting AWG and paper overflow. Unknown length/core/construction stays
  unknown. Ordinary Wire does not acquire cable authority. Legacy assemblies
  without explicit physical topology remain blocked rather than guessed.
- TDD: missing service/type RED, then six focused regressions GREEN, including
  1-to-2/1-to-3, noncontiguous branch indexes, JSON reload, shared DXF projection,
  ordinary-Wire rejection, missing source and overflow. Fresh full Release:
  **998 PASS / 0 FAIL / 0 SKIP**. Desktop Release l: **0 errors / 16 warnings**.
- Native candidate l: loaded the existing representative disposable project,
  selected CBL-020 through the normal detail picker, created the linked page,
  saved, closed both app windows, restarted the same candidate, and loaded the
  same page successfully. The original 180 connections, 28 cables and two
  assemblies retained identical serialized hashes across the detail operation.
  Normal Cable Settings reopened the original Custom cable. Entered test length
  999, clicked Cancel, then saved: length remained null and all three engineering
  collections retained their hashes. No injected database writes were used.
- Local evidence: `ui-20260928/cable-detail-native-l.png`,
  `cable-detail-reload-l.png`, `cable-detail-cancel-before-l.png`,
  `cable-detail-cancel-after-l.png`; `tests/cable-detail-20260928.trx`.
  Desktop SHA `BCF957EC7706332B98E405DCC7CDDCC56FDAF5F14BEA335BD1ADBEB5766F5A20`;
  core SHA `B6C63CC62211AD7C838859F216E038357153EC3942A2A712D68C06856652935A`.
  Local launcher now pins l. Auto remains `eb84bee61d562881d861eb17ce990e44f56aff76`.
- Protected DB/workbook/K7L/DWT hashes rechecked unchanged; diff check PASS.
  No production edits, archive approval or Auto execution in this checkpoint.
- This is a generic, truthful detail representation, NOT company CAD cable
  template acceptance. Native multi-end detail, complete CAD template authoring,
  direct authored-canvas IR/executor closure and broader multi-page UI acceptance
  remain open. Overall **PARTIAL**, owner visual acceptance **PENDING**.

#### Native Approved-Asset Editing Retest

- Native capture recovered. Normal candidate g navigation used the disposable
  workbook/archive and selected existing approved K7L rev-001 through the geometry
  action. Actual CAD appearance and eight confirmed contacts were visible. A wire
  started at power Pin 8 and ended freely; normal Save/Close/new-process Load
  restored it. Independent read-only snapshot inspection confirms zero invented
  ElectricalConnections, eight bindings and unchanged asset revision/hash.
- Fresh native testing exposed duplicate contact labels and an anchor lead hidden
  underneath the moved CAD body. Fixed shared Preview/DXF label projection and
  directional reanchoring (including orthogonal rotation). The old adjacent bend
  is replaced rather than retained as a retraced stub; remaining route is retained.
  Standalone ATTDEF tags are preserved, allowing exact TAG1 instance-reference
  substitution without altering source geometry. Existing untagged snapshots are
  not guessed or text-matched. TAG1 rendering has automated proof, not native proof.
- Candidate h: move/one Undo/one Redo and g-saved project reload exercised.
  Candidate i: outward lead/rotation/Undo/Redo exercised; retraced-stub defect
  observed and corrected afterward. Candidate j: fresh process, same project Load,
  native move/rotate/Save exercised with corrected leads. Do not substitute i
  screenshots for j acceptance. Local screenshots: `ui-20260928/k7l-native-import-g.png`,
  `k7l-native-wire-saved-g.png`, `k7l-native-rotate-i.png`,
  `k7l-native-move-rotate-j.png`, `k7l-native-reload-locked-j.png`.
  Candidate j additionally passed lock/rejected mouse drag/Save/normal window
  Close/reopen/Load: orientation, route, lock and 8/8 approved contacts persisted.
  All private evidence stays local.
- Final fresh full Release regression: **992 PASS, 0 FAIL, 0 SKIP**;
  Desktop Release j: **0 errors, 16 existing warnings**; diff check PASS.
  Desktop DLL SHA: `A547F7B7318320AD88C1BFC8E4873236F160BD73E4DE4D9EDB4C9F309BB19F71`.
  Core DLL SHA: `66E0E91789EB15373570B4479D0956696CE6566F7068C47590BBA260DB8B3F1C`.
  Local launcher now pins both assemblies and selects candidate j with explicit
  disposable DB/workbook/archive. Preserved unrelated terminal WIP is still in
  the local build and is not claimed as part of this commit.
- Fresh protected hashes unchanged: DB E6708879..., workbook 7CD84A26...,
  K7L 217A2AC8..., DWT D5DB332E.... No source approval or production mutation.
- **PARTIAL**, not whole-MVP acceptance. Broad multi-page editing, complete
  cable detail authoring, and direct canvas-to-IR/executor
  closure remain open. Existing synthetic/service tests are not native acceptance.
  K7L Pin 8 has typed Positive but no typed voltage/type in the saved catalog-derived
  snapshot; neutral preview remains truthful rather than inferring DC from its name.

#### Approved Asset Import And Standalone Contact Repair

- Existing canvas Image/Geometry action now offers the exact approved Schematic
  revision through the existing CP3-A resolver. It verifies path confinement and
  source SHA, asks for explicit CAD unit scale, then maps exact catalog SourcePinId
  to runtime PinId and exact approved contact tag to imported contact coordinates.
  No list-index, number/name or geometry-based identity fallback. Missing/duplicate
  bindings fail without replacing the original drawing. Wired/locked replacements
  remain blocked pending explicit reconciliation.
- Manual anchor overrides retire the approved binding revision claim; Reference-only
  edits keep it. Source file hashes remain as provenance. No archive record is edited.
- A real isolated K7L conversion exposed zero contacts despite eight source ATTDEFs.
  Added model-space ATTDEF ingestion, including invisible electrical contacts and
  visible default attribute text. The original failing conversion is retained locally.
  Fresh corrected conversion: 55 primitives, 8 contacts, original SHA unchanged.
- Existing approved K7L rev-001 and a disposable catalog/asset copy: **8/8 explicit
  bindings** survived new instance -> import -> JSON save/reload -> validation.
  ElectricalConnections stayed empty. Exact X1/X2/X4/X8 attribute orientation follows
  [Autodesk connection orientation documentation](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-Electrical/files/GUID-B6609B09-1850-48B4-BA44-BFE987B6889D.htm).
  This parses geometry direction only, never electrical identity. Nested INSERT
  orientation handling remains limited; the exercised K7L is a standalone definition.
- This is **SERVICE_IMPORT_AND_SERIALIZATION_ONLY_NOT_NATIVE_UI**. It is not new
  approval, visual acceptance, project execution or a completed native UI chain.
- Fresh Release regression: **986 PASS, zero failed/skipped**. Candidate g Desktop
  Release build: **0 errors, 16 existing warnings**. DLL SHA-256:
  `83773C6B294826E96C2482F00CAE78F9B71423DC4323CCB633C7B9108695E812`.
  Candidate g includes preserved unrelated terminal WIP; do not identify that WIP
  as part of this commit. Native retest still blocked by black window captures.
- Local evidence aliases: `k7l-import-20260928` (original failure),
  `k7l-import-20260928-final` (conversion and binding proof), `ui-20260928`
  (disposable DB/workbook/Documents/archive registry). Private assets stay local.

#### Typed Display Evidence And Metric Preservation

- Continued from published `fe2b48da62a87df7dc64b70bfb48b70b35d66dff`.
  Exact PinId power evidence now drives a bounded DC display profile and endpoint
  rating text. Explicit continuation pairs propagate that same evidence across
  draft sheets. Names, colour and geometry never establish connectivity or voltage.
  Zero-volt Return is not silently classified as negative or neutral. See
  `docs/specs/SCHEMATIC_DISPLAY_IDENTIFICATION.md` for scope and standard limitations.
- Clearing an AWG choice preserves an existing metric conductor area; regression
  failed before correction and passes afterward. The shared DXF colour projection
  uses the same resolver; export manifests now also pin the full source project.
- Fresh full Release tests: **976 PASS, 0 FAIL/SKIP**. Candidate e Release build:
  **0 errors, 16 existing warnings**. This count includes preserved unrelated WIP.
- Native candidate d remained open. Two refreshed native captures returned entirely
  black images although the accessibility tree remained readable. No blind input
  or old-build-as-new acceptance was performed. Candidate e UI verification is
  **NOT_RUN / native capture unavailable**, not PASS. The earlier candidate d DXF
  workflow evidence remains historical and does not verify the new display changes.
- Fresh protected hashes match the reconciled production DB baseline E6708879...,
  workbook 7CD84A26..., approved K7L 217A2AC8... and company DWT D5DB332E....
  No production/source/archive mutation, Auto source change, merge or approval.

#### Draft Exchange And Keyboard Retest

- Continuation of pushed checkpoint d825acd, not a new task. Conversation
  requirements remain the current product authority; unrelated terminal WIP is preserved.
- Diagnostic native WPF retest confirmed Chinese-IME W/G dispatch and P placement
  preview followed by Escape cancellation. UIA's reported focused textbox was stale;
  temporary WPF focus logging established actual canvas focus. Diagnostic hooks were
  removed from the source and the following candidate build. This bounded retest is
  not a claim that every shortcut/route gesture passed.
- Added a separate explicitly labeled **DXF draft exchange** action (Ctrl+E).
  It projects existing sheet order, millimetre coordinates, rotation, confirmed
  anchor positions, continuation captions and the shared crossing primitives without
  invoking the automatic planner. It does not create WDP, ACADE electrical authority,
  or an APPLIED/VERIFIED execution result. Incomplete wires, missing templates,
  unconfirmed anchors, catalog raster images not embedded and imported text limitations
  remain explicit diagnostics. This is NOT completion of the direct Plan/IR executor gate.
- Candidate d Desktop DLL SHA-256:
  `25443E9B091683035A731587881BE8F95FD131A9A812877CC3D59BD0A80A3E4F`.
  Native UI: loaded the same saved two-page disposable project, invoked DXF draft,
  observed diagnostics, chose a new isolated local ZIP and saved successfully.
  Local `native-draft-export.zip` has two ordered DXFs and a manifest containing
  source schematic hash and each DXF hash. No formal folder/source file was written.
- TDD: exporter tests initially failed because the exporter did not exist; all three
  passed after implementation (coordinates/order/no mutation, nonconnecting arc,
  cross-page label). Fresh combined-worktree full Release: **972 PASS, 0 FAIL/SKIP**.
  Release Desktop build: 0 errors, 17 existing warnings. Older terminal WIP tests are
  included in the combined-worktree count, not represented as committed here.
- Production SQLite fresh SHA remains `E67088796366875E9DEF97B307D9D44D8AE5DE8084CAD327E50159F7A30E63F1`.
  No new approved asset or Auto runtime change. Overall still PARTIAL; direct final
  output, automatic catalog presentation, native connected-pin/cable workflow and
  visual acceptance are not claimed complete.

The 2026-09-24 source checkpoint was pushed as de7c312. This continuation adds
ordered all-pages viewing, pairing two existing draft wires across pages,
shortcut dispatch, placement preview and explicit AWG. Exact CableDefinitionId
and CoreId lookup can read catalog core size/color; no model/voltage inference.
This is not yet an assertion that every runtime catalog adapter supplies cores.

Native UI on isolated DB/workbook copies: new project, click-start/bend and
double-click-finish, select AWG, create second page, select one wire on each
page, label TEST-SIGNAL, pair, Ctrl+S, F6 all-pages, normal close, restart and
reload all passed for the bounded draft case. The reloaded same page IDs,
paired label and AWG 40 / 0.005 mm2 were visible. AWG 40 is a disposable UI
test value, not an engineering recommendation. No new electrical endpoints
were invented to make these free-ended drafts appear complete.

Candidate b Desktop DLL SHA-256:
710C320EC45A1E1C67EC05FBA3888DC607EE17B223A02A74C301E35C6A3561AD.
Local evidence folder: task013-schematic-mvp-20260924-160248/ui-20260928.
Private absolute paths and project payloads are not published.

Observed issue: mouse canvas selection loses reliable keyboard focus on
subsequent letter commands. A further focus repair after candidate b awaits
native retest. Low zoom made thin strokes unreadable; minimum preview screen
weight was repaired and visibly verified after restart. All-keyboard workflow
is NOT PASS yet. Placement cancellation and connected-pin pairing are not
covered by this draft-only native pass; domain tests are separate evidence.

Fresh full Release tests: 969 passed, 0 failed/skipped. Release Desktop build:
0 errors, 16 existing warnings. Diff check PASS. Production SQLite SHA-256
remains E67088796366875E9DEF97B307D9D44D8AE5DE8084CAD327E50159F7A30E63F1.
Only that fresh comparison is claimed; preserve the earlier write history.
Auto source unchanged in this segment; no Auto execution or new symbol approval.

Continue: finish native keyboard/placement/route-edit proof, direct same-geometry
Plan/IR/export, catalog geometry/color integration and cable detail authoring.
Overall MVP PARTIAL; PRODUCT_OWNER_VISUAL_ACCEPTANCE=PENDING.

The Product Owner changed the primary workflow to a single manually authored,
multi-page schematic canvas and will create a new acceptance project. This is
the same Task-013; older terminal-preview output remains visually rejected.
The execution ledger is `../superpowers/plans/2026-09-24-direct-schematic-mvp.md`.

- Source base: Component `d5b18bcc05f0d003a469a76ee8b21421f041b9aa` plus
  uncommitted WIP; Auto `eb84bee61d562881d861eb17ce990e44f56aff76` plus preserved
  earlier terminal WIP. The new features are NOT present in those commits alone.
- Implemented WIP: versioned schematic persistence, explicit catalog identity,
  page authoring/rename/order, rotation, anchor binding, incomplete free wires,
  paired continuations, segment/bend edits, locks, transactional page transfers,
  exact completed-connection invariants, existing cable-editor integration,
  and fixed CAD geometry import. New image-based anchor positions stay unconfirmed.
- CAD source conversion used a disposable DWT copy through real accoreconsole.
  Source before/after SHA matched. MText now retains plain text and attachment;
  font/formatting fidelity and Hatch remain incomplete, explicitly diagnosed.
  No new symbol revision or approval was created.
- Fresh full Component Release tests: **962 PASS, 0 FAIL, 0 SKIP**.
  A separate snapshot of only the staged checkpoint passed **959/959** and its
  Desktop Release build passed with 16 warnings/0 errors. The difference is the
  preserved, unstaged legacy terminal-preview work, not skipped/deleted tests.
  Fresh Desktop Release build: **PASS, 0 errors, 16 warnings**. Build output is
  separate from the already running candidate. Current anchor-notification,
  multiline-text, page-rename, route-style and ordinary-wire deletion changes have NOT been mouse-tested in that
  running older candidate.
- Native Windows evidence establishes only startup, visible disposable DB and
  workbook isolation, and new schematic-tab loading. During resumed observation
  an OMRON F03-20 had been placed since the last recorded UI state; this is not
  attributed to automated testing. Input was paused and takeover requested.
- No final launcher, complete UI acceptance, direct canvas-to-Plan/IR export,
  or full company-template fidelity is claimed. Further work remains on explicit
  branching, lane separation, cable-detail presentation and no-replanning export.
  Automated tests do not substitute for those gates.
- Local recovery artifacts preserve tracked/untracked source and candidate/data
  identities; private source CAD, project DB and workbook are not published.
- This checkpoint publishes the bounded direct-authoring WIP only; the captured
  base SHA above is not its resulting commit. Exact commit/tree authority is the
  Git commit containing this record. Earlier terminal-preview WIP stays local.
- Overall **PARTIAL**. `PRODUCT_OWNER_VISUAL_ACCEPTANCE=PENDING`.

## Earlier Visual Inspection Record

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

### First Collaborative Engineering / Asset Confirmation Packet (2026-09-24)

This is a read-only intake checkpoint in the same Task-013 branch. A detailed project-specific packet, including exact endpoint/connection IDs, is local-only under the existing `task013-rsd-trace-20260923/verified` evidence directory as `FIRST_CONFIRMATION_PACKET_20260924.md`; it is not published to GitHub.

- The existing K7L Schematic `ApprovedCustom / rev-001` remains the sole exact approved reuse in this first batch, with eight explicit PinId bindings and unchanged SHA-256 `217A2AC8BDA3418D4E77D1A4D51015AAD7FC93FE819420EF0279EEFFFFBF3828`. No new revision or approval was made.
- The RSD-100C-24 manufacturer terminal-assignment image is present and SHA-pinned locally; it establishes the five terminal functions, not an approved ACADE Schematic asset or this project's PowerConversion. No exact approved RSD Schematic DWG was found in the searched archive root.
- The existing AL1342 archive intake has 42 distinct source-named DWG objects (21 symbol, 21 AUDIT). Same basenames have materially different hashes. These are unapproved candidates until visual, attribute, XTERM, and exact endpoint binding review; no filename/timestamp selection is permitted.
- The 18 Phoenix Contact 3209578 instances remain unclassified as transparent distribution or physical field interface. Their boxes were not hidden, and no common-Net or bridge connectivity was invented.
- A direct RSD input-side trace exposed a concrete adjacent terminal/contact configuration requiring Product Owner engineering confirmation before the real source bus is attributed. The exact private topology and one bounded question are in the local packet. INPUT return, OUTPUT pair, and FG remain separate pending decisions. This checkpoint did not modify any project connection, NetId, PowerDomainId, PowerConversion, workbook, source CAD, or SymbolArchive record.
- Overall disposition remains **PARTIAL**. This asset triage is not normal UI drawing acceptance, not AutoCAD execution, and not Product Owner visual approval. The existing editor/grammar verification backlog remains active without reset.

### Contactor / Countable Terminal Clarification And Display Continuity Guard (2026-09-24)

The Product Owner's annotated drawing confirms the separation between a contactor's A1/A2 coil, its mechanically actuated but electrically separate main poles, and the external terminal distribution. The open white terminal-circle symbol is a worker-facing countable terminal position in this drawing convention; it is not a filled wire junction or nonconnecting crossover. Physical SKU quantity still requires the exact selected part/configuration. The private project-specific contact trace and source screenshot hashes were added to the existing local confirmation packet, not GitHub. No RSD PowerConversion, NetId, conductor colour policy, or bridge edge was approved from this visual explanation.

Focused TDD found that topology display continuity could be synthesized from model-looking DisplayName/EquipmentTag or generic IN/OUT PinName text. That unsafe inference is removed. Archived PT 2,5-QUATTRO continuity now requires exact `ComponentDefinitionId = PHOENIX_CONTACT_3209578` plus terminal type; structured `TerminalBlock` internal connections/jumpers continue to work. [Phoenix Contact's 3209578 product data](https://www.phoenixcontact.com/en-gb/products/multi-conductor-terminal-block-pt-25-quattro-3209578) states four connections and one potential. This change affects only inferred topology potential/layer display grouping and does not modify saved ElectricalConnections or create project Nets.

- Focused regression: 2 tests RED before the correction; 3/3 PASS afterward (exact identity positive case, display-name-only alias fail-closed, generic pin-name fail-closed).
- Full Component Release tests: **927 PASS**, 0 failed/skipped. Desktop Release build: first attempt BLOCKED by an already running candidate process locking the normal output DLL; that process was not stopped. Rebuild to a separate local-only output directory: **PASS**, 0 errors, 16 existing warnings. No normal mouse/UI acceptance of this new build is claimed.
- Auto source unchanged in this checkpoint. Real power-pair acceptance, terminal visual replacement, white-circle count readback, complete page-quality review, and Product Owner visual acceptance remain **PENDING**.
- Protected-state reconciliation is required: the production SQLite SHA-256 was `540BFD1DD421AC0CCA6E4B66E9E2C9132464E75E363A55AC8E072E0E7C5C7C1B` at this intake's first check, but a later read-only hash check returned `E67088796366875E9DEF97B307D9D44D8AE5DE8084CAD327E50159F7A30E63F1` (102,715,392 bytes; 2026-09-24 05:50:34 UTC last write). An existing Desktop process was observed during the interval and was not stopped by this work; the cause of the DB write has not been established. Do **not** claim protected production state unchanged or run real-project verification on that DB until reconciled. The workbook, approved K7L asset, and viewed source DWG retain their previously pinned SHA-256 values. No production DB restore or edit was attempted.

Read-only reconciliation after the Product Owner confirmed they did not operate formal data: no Component Intelligence/testhost process remained, and a byte-for-byte copy of the observed production DB was preserved in the local-only Task-013 evidence area with matching SHA-256 `E67088796366875E9DEF97B307D9D44D8AE5DE8084CAD327E50159F7A30E63F1`; SQLite `quick_check` returned `ok`. The current DB contains one new revision, `REV-F5365891A01D09B34B2EDCB3`, timestamped 2026-09-24 05:50:34 UTC, trigger `MajorImport`, label `Before central archive project synchronization`. Its project snapshot SHA-256 `915D68CAAF7276161458C1493E0954547200F1032F79E80C4F3C0DC0B78D3329` exactly matches the existing 2026-09-22 `GeneratePreview` revision and the current persisted project snapshot. Against the 2026-09-23 disposable DB, the same 37 Component IR rows exist; 30 `updated_at` fields changed at the same time, while all 37 Component IR JSON payloads and the knowledge/sync tables match. The Desktop load path calls `SynchronizeCentralArchiveOnLoadAsync(recordMutation: true)`, which creates this labeled revision and rewrites Component IR timestamps when a central archive update is detected. This is a strong mechanism match, not proof of the exact process that initiated the load. The production file was in fact written; no protected-state PASS is claimed, and the older disposable is not a byte-identical pre-write production snapshot. Further Desktop verification must explicitly bind a disposable DB before launch.

The Product Owner additionally confirmed **one open terminal circle per physically used conductor hole; unused holes are not drawn**. Official 3209578 data identifies four conductor connections/one potential, while the typed catalog distinguishes those four conductor PinIds from two BRIDGE shaft PinIds. The private read-only project audit found exact used conductor PinIds but also BRIDGE PortId-level connection selectors that cannot be assigned to a particular hole or bridge accessory without further evidence. Do not turn the six modeled PinIds into six circles or infer a missing hole from PortId geometry. Current Planning Input emits each archived terminal as one Component/Schematic representation; WPF Preview and CP3-C execution still draw a generic rectangle, and the separate terminal command has only one circle per representation. Shared Plan/IR/execution per-used-contact projection, port-level unresolved presentation, and visual review remain **NOT_DONE**. This clarification does not approve a new Net, jumper, terminal accessory, or physical SKU count.

The Product Owner then confirmed the existing BRIDGE-Port-to-BRIDGE-Port connections are installed physical shorting bridges whose drawing meaning is shared potential. That authorizes displaying the *existing explicit bridge relation* as common potential between only those connected terminals; bridge shafts do not add conductor-hole circles. It does not authorize fabricating a new connection, bridge accessory SKU, or arbitrary shaft A/B placement. A focused regression now proves potential propagates across an explicit bridge connection to a second exact Quattro instance while an unbridged third instance stays Unknown (4/4 focused Release tests PASS; fresh full Release tests 928/928 PASS, both with disposable DB override and unchanged production SHA across each run). Target PDF page 1 visually distinguishes open terminal circles, filled junction dots, and crossover arcs; page 17 retains a boxed field-wiring terminal interface between cables. Transparent distribution and required physical field interface must not be collapsed into one rendering rule. Real project drawing, Preview/IR parity, and normal UI acceptance remain **NOT_DONE**.

### Editable Module Port And Reference Presentation Candidate (2026-09-29)

Same Task-013 branch. This bounded candidate responds to the Product Owner's three screenshots and follow-up requirement for an explicit `編輯模塊` control. It does not approve any asset or engineering connection.

- Editor code now has an opt-in module-edit mode (`E`): select a module, drag a Port group to a displayed module edge (top/bottom/left/right), or double-click its group handle to expand/collapse individual unconnected Pin display. Drag targets are transformed back to local geometry after rotation. Connected Pins stay visible; collapse is visual only. Moving a group preserves physical instance, PortId/PinId, wires and electrical connection identity, reanchors incident routes, and invalidates the moved contact positions' confirmation. Locked module/incident wire movement fails closed. One drag uses one project commit/Undo entry; real mouse confirmation remains pending.
- CAD geometry continues to rotate, but CAD text, reference and exterior Pin labels render upright. Generic module captions move below the body; Pin labels use direction-aware exterior positions and basic collision spreading. WPF/PDF and DXF use the same symbol positioning authority; exact visual quality remains for Product Owner review.
- Cross-page markers now display an explicit `第 N 頁 / grid cell` reference. Double-clicking a marker or its caption navigates to its paired marker in WPF. PDF output adds internal page/position links for paired markers, using the current saved page order. The semantic destination and page-reorder focused tests pass; actual reader click-through is not yet owner-verified.
- Focused Release schematic tests: 59 PASS at the first gate. Fresh full Component Release suite after final source changes: 1130 PASS, 0 failed/skipped. Desktop Release build: PASS, 0 errors (pre-existing warnings). `git diff --check`: PASS. No Auto source or formal CAD/SQLite/archive/workbook write was made by this coding pass.
- **Verification boundary:** native Port drag/expand/collapse, upright text legibility, PDF click-through, Undo/Redo/Save/Reload and Product Owner visual acceptance are `NOT_RUN` for this candidate until the Product Owner operates the newly packaged isolated build. The earlier AH window is not evidence for this build. Overall Drawing Grammar and real power-pair gates remain PARTIAL/PENDING, not READY or accepted.

### Compact Port, Frame Grid, Route And Solid-Wire Correction (2026-09-29)

Same Task-013/Component branch/Draft PR33. Builds on the preceding candidate; no Auto source or engineering-graph change. The Product Owner's AI screenshots exposed a large body after Port collapse, pin/model overlap, an inaccurate company-grid reference, a wire crossing its own labels after movement, and dashed drawn wires.

- Generic symbols with collapsed Ports now use a compact **display** body, one visible circle per collapsed Port, and upright model text inside that body. The Port circle is a visual expansion/selection control, **not** a common electrical contact. Wire mode expands the Port and requires a specific Pin; connected Pin anchors remain visible at their original typed positions. Approved CAD geometry is never compacted. The compact body's edge maps back to the original local Port coordinate for Edit Module drag. Existing Port/Pin/connection identity is unchanged.
- The common frame-grid reader uses complete, paired top/bottom 1..N and left/right A.. row labels, with regular-spacing checks. It overrides only the default margin grid; an explicitly configured non-default grid wins. If an imported frame cannot be calibrated, a reference says `格位待校準` instead of claiming a guessed cell. Page ordering and marker links still use stable page/marker identity. Owner confirmation of the target reader's PDF links is pending.
- Top/bottom generic Pin labels turn along the outward axis with a side offset, and the generic model caption is inside the representation box in WPF/PDF and DXF. A moved/rotated symbol or moved Port recalculates the incident wire's local outward lead past a visible Pin label before rejoining the preserved path. Locked incident wires still reject movement. This does not promise automatic avoidance of every other object or authorize changing endpoints/Net; visual inspection is still required.
- Drawn incomplete wires are now solid in WPF/PDF and DXF. The existing open-end square, tooltip, draft layer and `INCOMPLETE_WIRE` export diagnostic continue to distinguish an unfinished wire. Solid appearance does not mean confirmed electrical connectivity.
- New focused tests cover compact body/group contacts, rotated-edge drag mapping, grid detection/fail-closed behavior, route reanchoring without identity changes, vertical label spacing and solid draft DXF. Fresh full Component Release: **1140 PASS, 0 failed/skipped**. Desktop Release build: **PASS, 0 errors**. `git diff --check`: PASS; pre-existing unrelated Drawing WIP remains unstaged and untouched.
- Normal WPF mouse visual quality, move/Undo/Save/Reload of this exact build, all-page inspection and PDF reader click-through are **NOT_RUN**; the Product Owner will test a separate isolated candidate. Earlier AI screenshots are defect evidence, not proof that this correction is accepted. Overall status remains **PARTIAL** and `PRODUCT_OWNER_VISUAL_ACCEPTANCE = PENDING`.
