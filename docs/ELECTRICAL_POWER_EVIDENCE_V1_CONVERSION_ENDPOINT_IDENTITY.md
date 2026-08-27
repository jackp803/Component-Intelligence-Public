# electrical-power-evidence.v1 conversion endpoint identity

Status: additive E1 contract clarification for `electrical-power-evidence.v1`.

## Authoritative join

Conversion endpoint mapping uses only this typed, component-scoped identity chain:

`PowerConversion source PortID / PinID -> ComponentIR.PortId / PinId -> Electrical Domain SourcePortId / SourcePinId -> runtime PortId / PinId`

The join key is:

- `componentInstanceId`;
- source reference kind (`Port` or `Pin`);
- exact ordinal source ID.

Port and Pin namespaces remain distinct. Reuse of the same source ID on different component instances is allowed because the join is component-scoped.

The runtime endpoint ID is never reconstructed from the source string. It is copied from the exact runtime port/pin object whose typed source identity matches.

## Additive conversion fields

`ElectricalPowerEvidenceConversion` additionally carries:

- `inputEndpointIds[]`
- `outputEndpointIds[]`

Existing fields remain unchanged and continue to preserve source provenance:

- `inputSourcePortIds[]`
- `inputSourcePinIds[]`
- `outputSourcePortIds[]`
- `outputSourcePinIds[]`

The schema identifier remains `electrical-power-evidence.v1`. Missing additive arrays deserialize as empty arrays, so existing v1 payloads remain structurally compatible.

## Fail-closed source-reference behavior

A conversion side is not given a runtime endpoint claim when its source-reference mapping is incomplete or ambiguous. Deterministic blocking requirements are emitted for:

- no source PortID/PinID reference on a side;
- source ID resolving to zero runtime objects in the owning component;
- source ID resolving to multiple runtime objects in the owning component;
- source ID resolving only on a different component instance.

Runtime object identity is evaluated before any runtime endpoint-ID deduplication. Therefore two distinct runtime objects that happen to carry the same `PortId` or `PinId` do not become a false single match for one source reference.

When any source reference on a side is blocked, that side's endpoint array is empty rather than partially claiming connectivity.

## Runtime endpoint injectivity guard

After each typed source reference resolves to exactly one runtime object, E1 validates the resulting runtime endpoint identity mapping before serializing `inputEndpointIds[]` or `outputEndpointIds[]`.

The mapping must remain injective for distinct authoritative typed source references. If two or more distinct typed source references resolve to the same runtime endpoint ID string, the affected conversion side fails closed.

Collision cases include:

- distinct source PortIDs resolving to different runtime port objects whose `PortId` strings are equal;
- distinct source PinIDs resolving to different runtime pin objects whose `PinId` strings are equal;
- a source PortID and source PinID resolving to the same runtime endpoint ID string;
- distinct input/output source references sharing one runtime endpoint ID string.

The deterministic blocker convention is:

`POWER_CONVERSION_<SIDE>_RUNTIME_ENDPOINT_ID_COLLISION`

Its `missingFields` records the colliding runtime endpoint identity and the typed source references, for example:

- `runtimeEndpointId:converter:port:A-B`
- `Port:A-B`
- `Port:A_B`

If a collision involves references on both input and output sides, both affected sides receive their own blocker and both affected endpoint arrays remain empty.

Repeated declarations of the same typed source reference are normalized before resolution and do not create a false collision. A collision requires distinct typed authoritative source references.

## Runtime ID migration boundary

This hardening does not change runtime PortId/PinId generation or encoding. Existing runtime identity behavior is treated as read-only input to this export boundary.

A runtime endpoint ID migration is therefore **not required for this E1 contract hardening**: any lossy runtime-ID collision that reaches the export boundary is detected deterministically and blocked before downstream consumers can treat the collapsed string as an unambiguous physical anchor.

Changing runtime ID encoding, migrating stored endpoint IDs, or redefining ComponentProjectBridge identity generation requires separate authority and is outside this contract.

## Explicitly forbidden identity evidence

Endpoint mapping and collision handling never use:

- Voltage or VoltageDomain;
- names, model/part number, TypeKey or drawing role;
- PinNumber;
- page, coordinates, route position or endpoint order;
- string reconstruction/sanitization of runtime endpoint IDs.

This contract transports and validates identity evidence only. It does not create conversion semantics, topology edges, reachability, terminal pass-through, DAG ordering, coverage policy, page/layout policy, or drawing behavior.
