# Cable connector editor correction

## Approved scope

The owner approved the five-item in-chat design on 2026-10-02: endpoint
management separate from Pin counts, generic connector family choices rather
than BOM entries, separate mechanical Coding selector, immediate Pin-count
application, and safe removal of blank/configured endpoints.

No bundled-Port CAD wiring support is claimed in this correction. The current
CAD cable representation still requires exact per-Pin contact bindings.

## Execution receipt

The AO auto bridge and record-native-fallback entry point were attempted.
Both failed before execution with ModuleNotFoundError: agent_orchestrator.
Native implementation remains within the previously explicitly authorized
dual-CAD/cable-table work, with the owner's current explicit design approval.
No AO policy, paid fallback, installation, production workbook, user CAD,
current native window, or protected DrawingPlanning WIP was changed.
The bridge did not record a receipt; this document is the native receipt.

Antigravity reviewed only an abstract integrity scenario in the already trusted
test workspace. No source, BOM, CAD, credentials or user data was delegated.
Its advice highlighted stable Pin IDs, cancellation, binding cleanup, source
revision immutability and placed-instance inventory protection.

## Behavior

- Connector families are reusable metadata, independent of the BOM.
- M12 mechanical Coding presets do not generate terminal assignments, signals,
  continuity or a standards-compliance assertion. Unknown/custom values remain
  possible; legacy archived values are preserved on opening.
- Dropdown Pin counts apply immediately. Typed counts apply on Enter or focus
  departure; allowed inventory range remains 1 through 512.
- Empty automatic placeholders can be removed with their endpoints/Pins.
- Function, NC/unused, mapping and CAD-bound removal requires explicit Yes.
  Cancellation preserves the draft. Confirmed removal clears only affected
  references and bindings, retains the opposite endpoint as an incomplete
  row, records explicit removal intent for the new archive revision, and
  revokes confirmation.
- Original templates are not mutated. Existing placed cable instances retain
  fixed Pin/endpoint inventories. Archive edits create new candidate revisions.

## Evidence

Before implementation, one core regression and three real WPF control
regressions failed with the reported behaviors: immediate count, blank-end
deletion, and missing common connector families. An additional opening-no-op
regression exposed and corrected unintended metadata initialization.
Further red-green cases cover literal legacy Coding preservation, unoccupied
new Pin-number allocation, and unchanged-count no-op behavior.

Independent review identified editable ComboBox text-search prefix selection:
typing 12 could first apply 1 and remove stable Pins. The real WPF editable
TextBox reproduces this before the fix. Numeric text search is now disabled;
typing 1 then 12 leaves all original identities intact until Enter/focus
departure, while explicit dropdown selection still applies immediately.
Endpoint changes synchronize the highlighted numeric item under the refresh
guard. The unchanged-count event issue is also covered and fixed.

The WPF framework behavior was verified against its primary source:
[dotnet/wpf ComboBox TextUpdated](https://raw.githubusercontent.com/dotnet/wpf/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Controls/ComboBox.cs).

The offscreen STA harness exercises actual WPF controls and confirmation
callbacks without sending input to the user's desktop. It also verifies source
immutability, mechanical Coding selection, no inferred mapping, cancellation,
CAD binding cleanup, and placed-instance inventory lockout.

Build diagnostics include existing nullable, obsolete API and xUnit analyzer
warnings outside this correction. They are not described as a warning-free
build.

## Review outcome

The independent reviewer returned one Important typing issue and one Minor
unchanged-count issue. Both have targeted red-green WPF regressions and fixes.
Focused re-review cleared both with no remaining Critical, Important or Minor
findings. The reviewer used read-only inspection; test execution was performed
and verified by the implementing Codex agent.

The full worktree unit suite passed 1295 tests (including three protected
pre-existing tests not part of this deliverable). The offscreen connector,
central workbook lookup and real dual-CAD/multi-page PDF smoke programs passed.
The delivery runtime is built separately from a clean committed source export,
excluding the unrelated protected worktree changes. Owner GUI UAT is pending.

## Mechanical Coding evidence

The preset names are sourced from the manufacturer's public product overview,
not a purchased IEC document or inferred Pin-function standard:

- [Phoenix Contact circular/data connector overview](https://www.phoenixcontact.com/en-us/products/pcb-terminal-blocks-and-pcb-connectors/circular-connectors-and-data-connectors-for-printed-circuit-boards?p=57)
- [Phoenix Contact M12/RJ45 data connector product overview 2026](https://www.phoenixcontact.com/assets/32a4964b-0799-461b-bfa7-faf7a82bae69/index.html)

Pin counts/functions/mappings remain explicit owner input, not derived from
these choices. M8 and other families have no unverified Coding presets added.
