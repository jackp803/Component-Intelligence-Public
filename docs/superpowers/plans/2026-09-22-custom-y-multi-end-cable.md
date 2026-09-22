# Multi-End Cable Implementation Plan

> Execute inline with superpowers:executing-plans. Product semantics and execution are authorized by task CODEX-W1-20260921-012.

**Goal:** Author one physical multi-end cable from existing pin connections without changing electrical truth.

**Architecture:** Add optional physical topology to CableAssembly, with exact owned port references, stable branch indices and connection membership. One CableInstance represents the entire cable; no per-pin cable or electrical split is created. A detached draft and dedicated editor validate before applying through existing mutation/save/revision events.

**Tech Stack:** .NET 8, WPF, existing JSON/SQLite persistence, existing Drawing Planning contracts.

**Spec:** coordination repository main, CODEX_CUSTOM_Y_MULTI_END_CABLE_AUTHORING_20260921.md.

## Constraints

- Exact Component baseline 88f5aae9ca75bd278c7e6cbafc7baaa43b4e2892.
- Production and source engineering assets remain read-only; normal UI proof uses a disposable DB.
- Construction has no default; common and branch groups require confirmation.
- Preserve connection IDs, endpoints, nets, explicit core identity and geometry.
- WIRE-A and M12 ports on a component are not interchangeable pin identities.
- No source changes to Auto unless required for explicit multi-end contract consumption.

## Review Focus

- Duplicate endpoint ownership must fail closed, never select the first owner.
- Editing after concurrent project mutation must revalidate membership before writing.
- NaN, infinity and negative lengths must not persist.
- Reopening physical assemblies in the legacy editor must not discard topology.
- Removing members must preserve electrical connections and their original core metadata.

## Ordered Work

- [x] Verify baseline tree and run existing CableAssembly/ConnectorCable tests (64 passed).
- [ ] Add focused domain/service tests, observe RED, implement physical model and detached editor service. Test 1->2, mixed 1->3, explicit choices, ownership, exact electrical invariants, editing, unknown lengths, stable indices and backward-compatible serialization.
- [ ] Add normal Topology entry and physical editor; route existing multi-end assembly reopening to it. Update Connector Cable guidance without changing point-to-point behavior. Test source navigation contracts and build WPF.
- [ ] Project the single physical cable into explicit deterministic multi-end planning data and Cable Detail. Preserve pin identity; fail closed in consumers lacking support. Add focused planning tests before changes. Extend Auto only where actual contract requires it.
- [ ] Capture protected hashes and fresh disposable DB; run normal WPF X1/X4/X5 flow, Cancel, Save, revision/reload and invariant comparison. Record screenshots and raw evidence locally without private paths in Git.
- [ ] Run focused/full Release tests, Desktop Release build, diff checks and protected-state comparison. If safe planning slice is executable, run fresh isolated staging proof under task012 authority.
- [ ] Publish durable status, commit/push, confirm remote head/tree, open Draft PR(s), stop with truthful task disposition. Do not approve unrelated Human Gate decisions.
