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

`ElectricalPowerEvidenceConversion` now additionally carries:

- `inputEndpointIds[]`
- `outputEndpointIds[]`

Existing fields remain unchanged and continue to preserve source provenance:

- `inputSourcePortIds[]`
- `inputSourcePinIds[]`
- `outputSourcePortIds[]`
- `outputSourcePinIds[]`

The schema identifier remains `electrical-power-evidence.v1`. Missing additive arrays deserialize as empty arrays, so existing v1 payloads remain structurally compatible.

## Fail-closed behavior

A conversion side is not given a runtime endpoint claim when its source-reference mapping is incomplete or ambiguous. Deterministic blocking requirements are emitted for:

- no source PortID/PinID reference on a side;
- source ID resolving to zero runtime endpoints in the owning component;
- source ID resolving to multiple runtime endpoints in the owning component;
- source ID resolving only on a different component instance.

When any reference on a side is blocked, that side's endpoint array is empty rather than partially claiming connectivity.

## Explicitly forbidden identity evidence

Endpoint mapping never uses:

- Voltage or VoltageDomain;
- names, model/part number, TypeKey or drawing role;
- PinNumber;
- page, coordinates, route position or endpoint order;
- string reconstruction/sanitization of runtime endpoint IDs.

This contract transports identity evidence only. It does not create conversion semantics, topology edges, reachability, terminal pass-through, DAG ordering, or coverage policy.
