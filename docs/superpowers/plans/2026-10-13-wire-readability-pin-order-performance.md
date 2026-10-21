# Wire Readability, Pin Order and Memory Plan

> **For agentic workers:** Use superpowers:executing-plans for inline implementation and verification.

**Goal:** Keep wire captions adjacent without covering wires, reorder virtual Pin positions without changing connectivity, and reduce avoidable CPU/memory costs.

**Architecture:** Retain the existing authoring commands and WPF rendering pipeline. Use bounded compressed undo snapshots, one caption calculation per page, and lightweight cached vector drawing for CAD linework. Preserve immutable source CAD coordinates and identities.

**Tech Stack:** .NET, WPF, xUnit, existing offscreen smoke harness.

**Spec:** User-approved in-chat requirements: functions and pin colors visible; AWG suggestions remain provisional and editable; labels must not obscure wires; Pin display order editable; diagnose sluggish interaction without touching live data.

## Global Constraints
- Do not use AO or modify live databases, workbooks, source DWGs or running windows.
- Preserve unrelated dirty changes.
- Do not change electrical Pin IDs/numbers/functions when changing display order.
- CAD-bound physical Pin coordinates remain protected; virtual Port Pins can be reordered.
- Unknown electrical information remains pending; never certify provisional wire sizing.

## Review Focus
- Manual routes, locked wires, undo and reopening retain explicit decisions.
- A reordered Pin moves its existing wire endpoint, not its identity.
- Text remains visible and never covers actual wire geometry.
- History bounds retain the newest usable snapshot and reliable redo.
- CAD vector rendering retains exact ink bounds, text and hit testing.

### Task 1: Adjacent Captions
- [x] Run the existing failing near-wire smoke.
- [x] Fit two-line captions into available spacing, search locally first, keep leaders away from text.
- [x] Calculate caption/short-pin labels once per page.
- [x] Verify offscreen PNG and PDF, delete restoration and label persistence.

### Task 2: Virtual Pin Order
- [x] Add tests for identity-preserving position swaps, exact wire endpoints, locked/physical-CAD rejection and serialization.
- [x] Add ReorderPortPins authoring command and right-click move/sort actions.
- [x] Run focused domain and UI validation.

### Task 3: CPU and Memory
- [x] Demonstrate unbounded history with a failing test, then compress and bound history by count and bytes.
- [x] Demonstrate excessive CAD visual count with an offscreen regression, then batch immutable non-text CAD geometry.
- [x] Measure rendering, allocation, history bytes and WPF tier; do not force hardware mode.
- [x] Verify existing interaction/zoom/frame/PDF fixtures, full tests and independent review.

**Review outcome:** Six initial findings and one outlined-jump follow-up have regression coverage and fixes. The independent reviewer confirmed the final follow-up was resolved. WPF smoke covers incident-wire drag cancellation, final release flush, quarter-zoom strokes and jump arcs, visible unplaced captions, unique junction markers and unchanged circle ink bounds.

### Task 4: Delivery
- [x] Build a scoped source/runtime bundle without unrelated changes or live data.
- [x] Report measured improvements and remaining uncertainty; validate launcher without starting software.

**Delivery evidence:** BJ runtime built from archived clean source; 1,476 clean-source tests / 1,479 working-tree tests passed. 222 runtime files and source ZIP verified by ValidateOnly with StartedSoftware=false. PDF, connection-editor links, 170 CAD text ink cases and pointer-preview fixtures passed. A final screenshot regression explicitly arranges layout and verifies interior ink, and output rejection for unplaceable captions is exercised. Synthetic timings are documented as CPU/test measurements, not native UI FPS.
