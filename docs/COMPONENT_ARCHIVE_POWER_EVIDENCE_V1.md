# Component Archive Power Evidence v1｜中央歸檔電源證據擴充規格

Status: **Additive / Backward-compatible（加法式／向下相容）**
Applies to: `Component_Intelligence_Database.xlsx` read-only ingestion and `ComponentIR` power-evidence transport.

This document extends `COMPONENT_ARCHIVE_SPEC_V2.md` with optional explicit power evidence. It does not replace the existing Components / Ports / Pins contract and does not authorize any workbook write, runtime SQLite schema change, Power Topology / DAG analysis, drawing inference, or AutoCAD behavior.

## 1. Non-negotiable identity rule｜不可違反的身份規則

`Voltage != PowerDomainId`.

`Voltage` / `VoltageDomain` are voltage evidence（電壓證據）, for example `24 V DC`, `18...30 V DC`, or `0 V`.

`PowerDomainId` is an opaque stable engineering identity（不透明的穩定工程識別碼）. A power-domain ID may be ingested only when an explicit archive field supplies it. The parser must never create, merge, split, or repair a power-domain identity from:

- voltage value/range;
- net label;
- component/port/pin name;
- manufacturer/model/part number;
- TypeKey or drawing role;
- endpoint or row order;
- UI coordinates, topology geometry, or drawing/page position.

Blank, `Unknown`, `NotApplicable`, `N/A`, or `TBD` power-domain cells remain absent/null evidence.

## 2. Required and optional worksheets｜必要與選用工作表

Existing required worksheets remain unchanged:

1. `Components`
2. `Ports`
3. `Pins`

New optional worksheet:

4. `PowerConversions`

Absence of `PowerConversions` is valid and backward-compatible. It produces no `ComponentIR.PowerConversions` rows. Existing workbooks therefore continue to load without migration.

No actual archive workbook is modified by this schema definition. The desktop store remains read-only.

## 3. Ports / Pins additive field｜Ports / Pins 新增選用欄位

### Ports

Optional column:

`PowerDomainId`

Mapping:

`Ports.PowerDomainId -> ComponentIntelligence.Contracts.ComponentPort.PowerDomainId`

### Pins

Optional column:

`PowerDomainId`

Mapping:

`Pins.PowerDomainId -> ComponentIntelligence.Contracts.ComponentPin.PowerDomainId`

Missing column or blank value maps to `null`.

Existing `Voltage` mapping is unchanged:

- `Ports.Voltage -> ComponentPort.VoltageDomain`
- `Pins.Voltage -> ComponentPin.VoltageDomain`

The parser never falls back from `PowerDomainId` to `Voltage`.

## 4. PowerConversions worksheet contract｜電源轉換工作表契約

Recognized columns:

| Column | Required for a confirmed conversion | Meaning |
|---|---:|---|
| `ComponentID` | owner required | Explicit component owner. Rows are attached only to this stable component identity. |
| `ConversionID` | yes | Stable conversion identity. |
| `InputPowerDomainID` | yes | Explicit input power-domain identity. |
| `OutputPowerDomainID` | yes | Explicit output power-domain identity. |
| `InputPortIDs` | optional | Explicit source PortID list. |
| `InputPinIDs` | optional | Explicit source PinID list. |
| `OutputPortIDs` | optional | Explicit source PortID list. |
| `OutputPinIDs` | optional | Explicit source PinID list. |

The four list columns use semicolon-separated stable IDs, e.g.:

`PWR_IN;AUX_IN;PWR_IN`

Read normalization is deterministic:

1. split only on `;`;
2. trim surrounding whitespace;
3. remove blank entries;
4. de-duplicate using ordinal identity comparison;
5. order retained IDs with ordinal comparison.

No list item is interpreted as a display name, voltage, pin function, route, or drawing coordinate.

## 5. Incomplete rows stay incomplete｜不完整資料不得修補

For a known `ComponentID`, any row with conversion payload is preserved as `ComponentPowerConversion` even when one or more mandatory conversion identities are blank.

The archive parser does not repair an incomplete row. In particular it must not infer the missing side of a conversion from voltage differences, topology, endpoint order, component category, drawing role, or adjacent rows.

This preserves the accepted downstream `POWER_CONVERSION_FIELDS_REQUIRED` fail-closed behavior in `electrical-power-evidence.v1`.

A row that contains only `ComponentID` and no conversion payload may be ignored because it declares no conversion fact.

## 6. Duplicate declarations are preserved｜重複宣告保留

Rows sharing the same `ConversionID` are not collapsed and are never processed with last-write-wins semantics.

Both identical and conflicting declarations remain in the component conversion collection in deterministic order so the accepted downstream duplicate/conflict guard can reject ambiguous evidence fail-closed.

The archive layer is a transport boundary, not the authority that chooses which conflicting conversion is correct.

## 7. Missing / unknown owner behavior｜找不到元件 owner

A `PowerConversions` row with conversion payload is attached only when its `ComponentID` matches a known Components-sheet identity according to the existing workbook component-link convention.

A blank/Unknown owner or an owner not present in `Components` is never reassigned to another component. `FindByIdentityAsync` emits deterministic read diagnostics in the form:

`CENTRAL_WORKBOOK_POWER_CONVERSION_COMPONENT_UNRESOLVED:ComponentID=<id-or-blank>;ConversionID=<id-or-blank>`

The unresolved row is not silently attached by name, model, voltage, row adjacency, or order.

## 8. Deterministic logical output｜決定性輸出

For one component, parsed conversions are normalized in deterministic ordinal order by:

1. `ConversionID`;
2. `InputPowerDomainID`;
3. `OutputPowerDomainID`;
4. normalized input PortIDs;
5. normalized input PinIDs;
6. normalized output PortIDs;
7. normalized output PinIDs.

Therefore workbook row ordering and semicolon list ordering do not change the logical `ComponentIR.PowerConversions` output.

Duplicate/conflicting rows remain duplicate/conflicting after sorting; sorting is not conflict resolution.

## 9. Backward compatibility｜向下相容

Legacy workbook behavior remains valid when:

- `Ports.PowerDomainId` is absent;
- `Pins.PowerDomainId` is absent;
- `PowerConversions` is absent.

In that case:

- existing `Voltage` / `VoltageDomain` values are preserved exactly as before;
- `PowerDomainId` is null;
- `ComponentIR.PowerConversions` is empty;
- downstream power evidence remains Unknown/blocking where explicit domain/conversion evidence is required.

No synthetic identities are introduced to make old workbooks appear complete.

## 10. Persistence and execution boundary｜持久化與執行邊界

This change is parser/schema documentation only:

- no central workbook file is modified;
- no new workbook conversion table is written automatically;
- no runtime SQLite table/schema is added or mutated;
- ordinary ComponentIR JSON serialization may carry the new typed fields through existing cache mechanisms;
- no E2 Power Topology, DAG, reachability, cycle, source-selection, or coverage semantics are defined here;
- no Page Plan, Layout, routing, Drawing IR, symbol policy, AutoCAD writer, DWG, WDP, or AutoCAD Electrical library behavior is changed.

The archive's responsibility is only to preserve explicit evidence and explicit absence faithfully.
