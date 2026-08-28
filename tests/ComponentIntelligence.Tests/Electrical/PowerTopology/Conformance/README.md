# Power Topology Conformance Corpus v1 — SYNTHETIC / TEST-ONLY

This directory is an E2-owned deterministic conformance corpus for accepted E1 Engineering Graph power identities and accepted E2 Power Topology behavior.

It is **not** real hardware evidence, Product Owner acceptance, AutoCAD/ACADE execution evidence, DWG/WDP inspection, or formal production data. Fixture names, endpoint identities, route identities, and domains are synthetic test identities only.

The executable manifest is `PowerTopologyConformanceCorpusV1.Cases`. Every case records exact expected domain identities, producer/consumer facts, conversion edges, topological order, physical endpoint coverage, and blocker `code@subject` identities. `PowerTopologyConformanceCorpusV1Tests` executes the accepted production adapter/analyzers against that manifest without modifying production semantics.

| Case | Expected | Purpose |
|---|---|---|
| `ready-direct` | READY | Direct explicit Producer → Consumer physical coverage in one domain. |
| `ready-fanout` | READY | One explicit producer conductively covers multiple consumers. |
| `ready-multilevel-conversion` | READY | A → X → B → Y → C with exact converter input/output runtime endpoint identities and canonical order `X,Y`. |
| `ready-terminal-transparency` | READY | Confirmed ordinary-terminal continuity is transparent only as accepted conductive evidence. |
| `ready-order-invariant-conversion` | READY | Reversed collections, route serialization, segment endpoints, and endpoint-array ordering preserve the same logical result. |
| `blocked-missing-producer` | BLOCKED | Consumed domain without explicit producer stays fail-closed. |
| `blocked-orphan-converter` | BLOCKED | Conversion input domain not reachable from an explicit producer is orphaned. |
| `blocked-duplicate-producer` | BLOCKED | Multiple explicit producers for one domain remain rejected. |
| `blocked-conversion-cycle` | BLOCKED | Conversion DAG cycle is rejected and no partial topological order is accepted. |
| `blocked-converter-output-missing` | BLOCKED | One declared converter output endpoint missing from confirmed physical topology invalidates complete output-side proof. |
| `blocked-converter-input-ambiguous` | BLOCKED | One converter input runtime endpoint resolving to multiple topology anchors invalidates complete input-side proof. |
| `blocked-converter-empty-side` | BLOCKED | Empty converter input/output endpoint evidence cannot be replaced by names or adjacency. |
| `blocked-stale-endpoint-identity` | BLOCKED | A stale `PinId` cannot be reconstructed from a suggestive `NodeId` or other weak signal. |

## Evidence boundary

The corpus consumes only explicit accepted identities/evidence. It does not derive engineering meaning from endpoint order, component/model/TypeKey/name, voltage-like strings, page, coordinates, geometry, route direction, or node-name resemblance.

Confirmed route segments and confirmed terminal continuities are treated only as undirected conductive facts. No fixture creates a conductive edge across a converter input/output boundary; cross-domain truth comes only from the accepted conversion fact.
