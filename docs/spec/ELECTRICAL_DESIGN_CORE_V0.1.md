# Electrical Design Core v0.1

> Status: implementation baseline based on the 2026-08-14 Component Intelligence electrical-design decisions.
>
> Scope boundary: this document stops at Electrical Design Engine（電氣設計引擎）and Physical Layout（實體佈局）. Drawing IR（繪圖中介資料）and AutoCAD Drawing Automation（AutoCAD 自動畫圖）are the next function and are intentionally not implemented here.

## Data flow

```text
Component Intelligence / BOM
→ Electrical Project Model
→ Topology / Wiring / Net
→ Cable / Connector / Terminal Planning
→ Validation
→ Physical Layout / Cable Route
→ Pre-Export Review
→ Drawing-ready Project Model
```

## Implemented in this branch

- `ElectricalProject` is the project-level Single Source of Truth（單一事實來源）for electrical design state.
- Component Instance（元件實例）is separated from archived Component Definition（元件定義）.
- Port（接口）and Pin（腳位）are separate; one port may contain mixed Power / Communication / Digital / Analog functions.
- Connector（接頭）and Protocol（協定）are separate. An RJ45 connector is not assumed to mean Ethernet.
- Net（電氣網路）uses a stable `NetId`; equal labels do not automatically merge nets.
- 0V / SG / PE / FE / Chassis / Shield are represented by distinct `GroundReferenceType` values.
- Cable Definition / Cable Instance / Cable Assembly and per-core assignments are separate.
- Cable Assembly supports multiple physical cable members for composite/hybrid assemblies.
- Terminal Block / Terminal Position / Level / Connection Point / Shorting Jumper are explicit domain objects.
- Each real terminal conductor entry can define side, maximum conductors and allowed wire-area range.
- Terminal electrical groups are calculated from actual wire connections, internal terminal connections and shorting jumpers. A label such as `54V+` does not create electrical continuity by itself.
- Naming Engine separates Stable ID, Reference Designator, Equipment Tag and Display Name. Automatic numbering uses max-used + 1 and therefore does not silently reuse deleted designators.
- Validation uses `INFO / WARNING / ERROR / BLOCK` and derives `READY / REVIEW_REQUIRED / BLOCKED` drawing readiness.
- Unconnected required pins enter Pre-Export Review instead of automatically blocking drawing output.
- Required PE is elevated to ERROR + Pre-Export Review.
- Protocol mismatch, connector mismatch, AC/DC mismatch, out-of-range source voltage, source-to-source power conflict, digital output conflict, analog-standard mismatch, terminal conductor-count overflow and terminal wire-size mismatch have deterministic validation rules.
- RS485 pair completeness and verified differential roles are checked without assuming that manufacturer `A/B` labels always have the same polarity convention.
- Physical Layout supports containers, zones/keep-out areas, footprint, clearance, DIN rail, cable duct, placement and cable-route segments.
- SQLite persistence stores an electrical-project JSON snapshot in the same existing SQLite infrastructure, isolated in `ElectricalProjects` and schema-versioned at the project snapshot level.

## Key invariants

1. BOM Item ≠ Component Definition ≠ Component Instance ≠ UI Node.
2. Port ≠ Pin.
3. Connection ≠ Cable ≠ Cable Route.
4. Connector ≠ Protocol ≠ Pin Mapping.
5. Signal ≠ Cable Requirement ≠ Cable Product ≠ Cable Assembly.
6. TopologyPlacement ≠ PhysicalPlacement.
7. Same display label ≠ same Net.
8. Shield ≠ Signal Ground ≠ Power Return ≠ PE/FE.
9. A Terminal Block reference such as `TB1` does not imply every position is common.
10. Only actual conductive objects (wire, internal conductor, shorting jumper) establish electrical continuity.
11. Physical Layout contains physical wire/cable routes, but does not decide the electrical connection itself.
12. Drawing / AutoCAD is downstream and must consume the validated Project Model instead of recomputing electrical intent.

## First implemented rule IDs

- `RULE-NAME-002`: duplicate component reference designator.
- `RULE-CONN-001`: connection references a nonexistent endpoint.
- `RULE-PROTOCOL-001`: protocol mismatch (for example RJ45/RS485 ↔ RJ45/Ethernet).
- `RULE-CONNECTOR-001..003`: connector family / coding / gender mismatch.
- `RULE-PWR-001..004`: AC/DC, voltage range, polarity and source-to-source validation.
- `RULE-DIO-001..003`: digital output and current-capability validation.
- `RULE-AIO-002`, `RULE-AIO-004`: analog signal-standard and 4–20 mA loop-role validation.
- `RULE-RS485-008`, `RULE-RS485-009`: incomplete differential pair and unknown verified pair role.
- `RULE-GND-VAL-005`: required PE unconnected.
- `RULE-TERM-006`, `RULE-TERM-007`: terminal conductor count and wire-size range.
- `RULE-LAYOUT-001..003`, `RULE-LAYOUT-005`, `RULE-LAYOUT-007`: layout container, overlap, mount target, keep-out and DIN-rail capacity.

## Explicitly deferred

These are not architecture gaps; they are policy/library/UI work that can be added without replacing the model:

- complete manufacturer connector mating library and mechanical cable-entry/gland checks;
- full AWG reference dataset and ampacity policy;
- configurable cable candidate ranking weights;
- RS485 stub-length thresholds and termination-impedance policy;
- full Ethernet / EtherCAT / IO-Link protocol-specific validators;
- exact physical-layout spacing defaults and route-length allowance policy;
- WPF topology/layout editing UI, drag/rotate/snap interaction and undo/redo command surface;
- Derived BOM material generation for every terminal/jumper/cable/connector case;
- Drawing IR, sheet planning, module placement and AutoCAD Electrical API adapter.

The deferred items must build on this domain model rather than introducing a parallel electrical model or UI-only source of truth.
