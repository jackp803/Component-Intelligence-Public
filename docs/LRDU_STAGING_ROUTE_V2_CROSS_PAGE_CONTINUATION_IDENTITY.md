# LRDU staging-route.v2 cross-page continuation identity

Task: `E1-20260827-013`

## Scope

This note defines only the E1-owned identity/evidence transport for `lrdu-staging-route.v2` cross-page continuations. It does not define routing, pathfinding, page layout, Drawing IR, AutoCAD writer behavior, or electrical PowerTopology semantics.

The outer schema remains exactly `lrdu-staging-route.v2`.

## Authoritative identity chain

A cross-page continuation preserves explicit source evidence:

`pairIdentity + sourceEndpointId + destinationEndpointId + sourcePageId + destinationPageId + evidenceStatus/evidenceSource`

The source and destination roles come from that explicit continuation evidence. They are not inferred from a route segment's serialization order.

Runtime endpoint IDs continue to bind to audited graph nodes through the existing evidence chain:

`runtime endpoint ID -> audited AutocadConnectionPointBinding -> graph node { componentInstanceId, PinId, ConnectionPoint.SymbolKey, ConnectionPoint.ConnectionPointId }`

This task does not change that binding or runtime endpoint encoding.

## Exact route/net/segment resolution

For one unique `pairIdentity`, define:

- `sourceNodeId = node:<sourceEndpointId>`
- `destinationNodeId = node:<destinationEndpointId>`

A route segment is an eligible candidate only when:

1. `TopologyStatus == Confirmed` using ordinal comparison;
2. the segment endpoints equal the unordered exact node pair `{sourceNodeId, destinationNodeId}` using ordinal identity;
3. the containing `routeId`, `netIdentity`, and `segmentId` are all explicitly present.

The segment's `FromNodeId` / `ToNodeId` order is serialization order, not engineering direction. Forward and reversed serializations of the same exact physical pair therefore resolve identically.

A continuation is route-identity ready only when exactly one confirmed `(routeId, netIdentity, segmentId)` candidate exists and the source continuation evidence itself is `Confirmed`.

## Additive v2 fields

`crossPageContinuations[]` retains its existing fields and additively carries:

- `routeId`
- `netIdentity`
- `sourceEndpointId`
- `destinationEndpointId`

The existing fields remain:

- `pairIdentity`
- `segmentId`
- `sourcePageId`
- `destinationPageId`
- `sourceNodeId`
- `destinationNodeId`
- `evidenceStatus`
- `evidenceSource`
- `blockingReason`

The additive fields default to empty strings when absent in an older v2 JSON payload; the outer schema identifier is unchanged.

## Fail-closed rules

No first/list-order/lexicographic winner is selected to hide ambiguity.

- zero confirmed exact tuple candidates -> `EXACT_CONFIRMED_ROUTE_NET_SEGMENT_REQUIRED`
- more than one confirmed exact tuple candidate -> `EXACT_CONFIRMED_ROUTE_NET_SEGMENT_AMBIGUOUS`
- exactly one tuple but continuation evidence is not Confirmed -> `CONFIRMED_CROSS_PAGE_CONTINUATION_EVIDENCE_REQUIRED`
- repeated `pairIdentity`, including otherwise identical duplicates -> `DUPLICATE_CROSS_PAGE_PAIR_IDENTITY`

For duplicate `pairIdentity`, E1 emits one blocked record. Route/net/segment identity is suppressed. Explicit source fields are retained only where every duplicate agrees exactly; conflicting evidence is never silently canonicalized into a winner.

## Determinism

The v2 adapter normalizes:

- routes by `routeId`;
- route nodes by `nodeId`;
- route segments by `segmentId`;
- raw cross-page source evidence by the complete explicit continuation identity/evidence tuple;
- resolved continuations by `pairIdentity` groups.

Reordering route, segment, node, or continuation collections therefore cannot change the logical v2 result.

## Forbidden weak signals

Cross-page identity resolution does not use:

- segment `SignalCode`;
- route `VisibleLabel`;
- component/model/TypeKey/name text;
- pin display numbers or names;
- coordinates, page geometry, drawing position, or route list position;
- case-folded or sanitized identity guesses.

Unknown or ambiguous evidence remains blocking.

## Boundary with downstream agents

E1 transports one exact evidence-backed continuation identity tuple. It does not decide how E4 routes that continuation, where E3 places it, how E5 represents it, or how E6 writes it to AutoCAD. Those remain separately governed downstream responsibilities.