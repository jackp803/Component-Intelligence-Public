# Engineering Graph Conformance Corpus v1

## Status and boundary

This directory documents the **synthetic/test-only** Engineering Graph Conformance Corpus v1 for `electrical-drawing-automation`.

It is a regression/conformance asset over the accepted Component Intelligence Engineering Graph export boundary:

- graph schema: `lrdu-staging-route.v2`
- corpus schema: `engineering-graph-conformance.v1`
- fixture schema: `engineering-graph-conformance-fixture.v1`
- fingerprint schema: `engineering-graph-conformance-fingerprint.v1`

Nothing in this corpus is real Product Owner approval, AutoCAD Electrical acceptance, DWG/WDP evidence, symbol approval, formal ACADE evidence, power-domain/converter evidence, Page Plan, Layout, Routing, Drawing IR, or Writer behavior.

## Purpose

Downstream tests need stable graph identities without inventing topology from names, models, TypeKey, labels, coordinates, drawing position, filenames, voltage, or collection order. The corpus pins a small set of READY and BLOCKED identity cases and a deterministic semantic fingerprint.

`fixtures/engineering-graph-conformance/v1/manifest.json` is the index. Each referenced fixture file contains the expected status, important identities, blocker codes, and the exact canonical semantic-identity projection used for the fingerprint.

## READY cases

- `EGC-READY-DIRECT-001` — direct single-route component/node/pin/connection-point/net/segment chain.
- `EGC-READY-FANOUT-001` — fanout/shared source node with injective pin and connection-point identities.
- `EGC-READY-CROSS-PAGE-001` — exact cross-page `pairIdentity`, route/net/segment, endpoint, page, and node identities.

READY fixtures have pinned SHA-256 fingerprints. Tests independently rebuild them using the accepted `lrdu-staging-route.v2` adapter and recompute the fingerprints.

## BLOCKED cases

- duplicate pin endpoint identity;
- missing exact confirmed route/net/segment for continuation evidence;
- ambiguous exact confirmed route/net/segment for continuation evidence;
- duplicate route identity;
- duplicate node identity;
- duplicate segment identity;
- unknown continuation evidence that would otherwise require semantic reconstruction.

The `EGC_*` blocker codes are **conformance-corpus validator codes**, not new production Engineering Graph contract codes. Existing accepted continuation blocker codes are preserved verbatim where the v2 adapter already emits them.

## Canonical fingerprint

The fingerprint is uppercase hexadecimal SHA-256 over UTF-8 bytes of the ordinally sorted canonical lines below:

```text
SCHEMA|<schemaVersion>
PROJECT|<projectId>
R|<routeId>|<netIdentity>|<topologyStatus>
N|<routeId>|<nodeId>|<kind>|<componentInstanceId>|<componentDefinitionId>|<pinId>|<symbolKey>|<connectionPointId>
S|<routeId>|<segmentId>|<kind>|<fromNodeId>|<toNodeId>|<topologyStatus>
C|<pairIdentity>|<routeId>|<netIdentity>|<segmentId>|<sourceEndpointId>|<destinationEndpointId>|<sourcePageId>|<destinationPageId>|<sourceNodeId>|<destinationNodeId>|<evidenceStatus>|<evidenceSource>|<blockingReason>
```

The lines are sorted with `StringComparer.Ordinal` and joined by `\n`.

The fingerprint intentionally excludes display/non-semantic signals such as `VisibleLabel`, `ComponentTypeKey`, display names, pin/port names, `SignalCode`, geometry, coordinates, page position, filenames, voltage, and source collection order. Regression tests mutate those weak/display fields and require the same fingerprint.

A BLOCKED fixture does not receive a READY fingerprint. Its canonical identity remains persisted in its fixture file so the blocked input/output identity evidence is still inspectable.

## Determinism

For every READY case, tests build a collection-permuted variant and require:

1. the same canonical identity text;
2. the same SHA-256 fingerprint;
3. the same expected READY result.

Cross-page continuation resolution remains the accepted v2 behavior: exact unordered endpoint-node pair over confirmed topology, exactly one route/net/segment candidate, and explicit source/destination evidence preserved.

## Maintenance rule

This corpus may track accepted Engineering Graph contract evolution, but it must not silently redefine production semantics. If a future accepted contract changes an authoritative identity field, schema version, or blocker boundary, update the corpus only under a separately authorized E1 task and regenerate/pin fingerprints with fresh verification.
