# Inline Wire Annotation Refinement

## Approved Scope

Show the actual wire designation, compact function and physical color beside each wire. Show AWG and conductor area together, using one line when it fits, two lines otherwise, or separate pieces on clear spans of that same wire. Emit each exact specification/status group once with explicit group membership. Never substitute the placeholder 功能見明細 or internal W indices for a manual designation.

Align expanded display endpoints using actual Pin-to-Pin attachments, not matching Pin numbers. Keep native CAD anchors, electrical identities, connector metadata, manual route interiors, locks and obstacle reservations authoritative. Ordinary branched or non-facing routes retain bends and use span-based annotation.

The user subsequently chose short automatic names such as Valve1_1 and Valve1_2. Save stable short component references and reserve retired wire names. Recognize the exact legacy generated reference_Port_Pin pattern only for nonmanual, unlocked routes. Do not alter component reference designators or Port/Pin metadata. Provide an explicit existing-project refresh action.

## Evidence

- New core annotation and alignment tests reproduced missing captions, missing candidate spans and absent alignment before implementation.
- The new offscreen inline-annotation fixture failed for remote specifications and failure to try the clear shorter span before implementation.
- A collinear-stub test reproduced artificially shortened annotation space; continuous collinear spans now merge for presentation without changing routes.
- Four short-name tests failed under the legacy generator before implementation.
- WPF verification checks annotation rectangles against strokes, outlines, crossing arcs, other captions, sheet margins and body/detail areas. It also verifies zoom-out clearance, manual routes, drag cancellation, source snapshots and PDF output.
- The narrow long-manual-name case must fail output rather than substitute indices or omit metadata. After explicit expansion/alignment with adequate clearance, every full manual designation remains visible.
- Independent review reproductions cover manual and slash-separated alternatives, native/display row coincidence, stale manual reservations, two-point manual reanchoring, numeric Port names, unrelated locked routes, other-Port contacts, and legitimate whole-Port ownership. Each regression was reproduced before its fix.
- Fanout display leads are temporary graphical reservations, not persisted electrical edges. Exact owner Pin/whole-Port routes exclude only their own leads from route clearance; unrelated routes and visible contacts remain checked.
- Ordinary full-wire-detail PDF pages are opt-in, with the desktop option unchecked by default. Physical cable topology appendices and their navigation remain unchanged. Both default inline-only output and explicit-detail output are tested.

## Boundaries

No AO, no active app automation, no original DWG edits, no central workbook/archive writes, and no live project mutation were used. Antigravity provided a synthetic regression-test second opinion only; the repository was not trusted and its contents were not delegated there. An independent Codex source review is requested before delivery. Native real-project interaction remains user acceptance work.
