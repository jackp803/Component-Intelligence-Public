# Component Intelligence Central Workbook v1

> **Storage contract / 儲存契約。** 最新歸檔決策規則以 [`COMPONENT_ARCHIVE_SPEC_V2.md`](COMPONENT_ARCHIVE_SPEC_V2.md) 為權威。若本文件與 v2 衝突，以 v2 為準。

## Purpose

The Windows desktop application reads reusable component engineering knowledge from a zero-cost central archive synchronized by Google Drive for Desktop. The desktop is read-only with respect to this central archive. Human + GPT archiving owns central writes; local SQLite remains the runtime/query cache.

The workbook is named `Component_Intelligence_Database.xlsx` and contains three authoritative engineering sheets:

- `Components`: one row per Manufacturer + Model / Part Number.
- `Ports`: one row per physical/logical port.
- `Pins`: one row per physical pin/contact.

No automatic web search, PDF download, PDF parsing, or central-library write belongs in the production Windows lookup path.

## File tree

```text
Component Intelligence/
├─ Component_Intelligence_Database.xlsx
└─ Documents/
   └─ <Manufacturer>/
      └─ <Model>/
         ├─ datasheet.pdf
         ├─ manual.pdf
         ├─ product.jpg|png
         ├─ mechanical_drawing.pdf|png
         ├─ wiring_drawing.pdf
         ├─ cad_2d.dxf
         └─ cad_3d.step
```

Only files that actually exist are created. Workbook paths are relative to the workbook directory, for example `Documents/OMRON/F03-20/mechanical_drawing.png`. Drive-letter absolute paths are forbidden.

The native Google Sheet may be used as the editable cloud surface, but the current Desktop adapter reads `.xlsx`. After cloud edits, export/replace the synchronized `Component_Intelligence_Database.xlsx`; otherwise Desktop may read stale data.

## Components sheet

Core fields:

`ComponentID, Manufacturer, Model, Category, Description, Voltage, IOType, OutputType, Protocol, GeometryType, WidthMm, HeightMm, DepthMm, DiameterMm, MountingType, DatasheetPath, ImagePath, DrawingPath, TopologyStatus, LayoutStatus, DatasheetURL`

Current archive extensions may include `DrawingURL` and `Fastener`.

Unknown numeric values stay blank. Unknown text values use `Unknown`. `NotApplicable` means confirmed not applicable, not unknown. Engineering values must not be invented.

For rectangular Layout, use trusted `WidthMm`, `HeightMm`, and `DepthMm`. For cylindrical parts, preserve `DiameterMm`; the current 2D/2.5D bounding box may use `Width=Diameter`, `Depth=Diameter`, `Height=OverallLength` when those dimensions are documented.

`MountingType` is the mounting category (`DIN Rail`, `Backplate`, `PanelCutout`, `Surface`, etc.). Fastener details such as `M3 screw` belong in `Fastener`. Process connections such as `G 1/4`, `G 1/2`, `Rp 1`, or clamp-on must not be stored as MountingType.

## Ports sheet

Core fields:

`PortID, ComponentID, PortName, PortRole, Direction, SignalType, Voltage, Protocol, Connector, ConnectorCoding, Gender, PinCount, ActualPinCount, PinCompleteness, PhysicalSide, SourcePage, Notes, TopologyEndpointMode`

Direction values:

`Input, Output, Bidirectional, Mixed, Passive, Unknown`

`PortRole` and `Direction` are separate facts. `PortRole` carries functional/topology meaning; `Direction` preserves truthful electrical behavior. Do not falsify Direction merely to force screen-left/screen-right placement.

`PhysicalSide` is the documented real connector face/location. It is not the same as topology screen side.

`TopologyEndpointMode` values:

- `Connector`: whole-mated interface is collapsed to one Port endpoint by default; all Pins still remain archived.
- `Pins`: independent terminals/conductors are individually visible and wireable.

Examples:

- M12 / M8 / RJ45 whole-mated connector → `Connector`.
- Screw terminal / terminal block / flying lead / loose wire / fixed individually wired pigtail → `Pins`.

## Pins sheet

Core fields:

`PinID, PortID, PinNumber, PinName, PinRole, Direction, SignalType, Voltage, Function, PinStatus, SourcePage, Notes`

Pin direction may additionally use `Return` for `0V`, `L-`, `V-`, and other DC supply returns.

Pin status values:

`Used, Unused, NC, Reserved, Optional, Unknown, NotApplicable`

`Unknown != NC`. `Unused != NC`. `NC` is used only when evidence explicitly defines the contact as not connected.

## Complete physical-pin rule

If a connector declares `PinCount = N`, all N physical contacts must exist as Pin rows. Unused, NC, Reserved, Optional, and Unknown contacts are never deleted merely because they do not carry a normal signal.

Do not infer PinCount from connector family alone: M12 is not automatically 5-pin.

Example:

```text
M12 5-contact Port
├─ Pin 1  Used
├─ Pin 2  Unknown
├─ Pin 3  Used
├─ Pin 4  Used
└─ Pin 5  NC
```

## Reference semantics

OMRON K7L-AT50DP SENSING terminals 2/3/4 are archived as one `Sensor Input` Port with `Direction=Mixed` and `TopologyEndpointMode=Pins`: the group is a functional sensing input even though Pin 4 transmits the sensing signal.

IFM AL1342 X01~X08 are `IO-Link Port Class A`, `Direction=Mixed`, M12 A-coded, `TopologyEndpointMode=Connector`. Do not change them to fake Output ports just to force topology placement; mixed/non-directional placement is a presentation rule.

## Runtime flow

```text
Google Drive for Desktop
        ↓
Component_Intelligence_Database.xlsx + Documents/
        ↓
WorkbookComponentKnowledgeStore
        ↓
CentralLibraryComponentLookupService
        ↓
Local SQLite runtime cache
        ↓
Topology / Layout / Electrical Design
```

Missing central knowledge remains visible as a placeholder/review state; it is not silently replaced by web search or guessed engineering data.
