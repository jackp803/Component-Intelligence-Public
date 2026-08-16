# Component Intelligence Central Workbook v1

## Purpose

The Windows desktop application reads reusable component engineering knowledge from a zero-cost central archive synchronized by Google Drive for Desktop. The desktop is read-only with respect to this central archive. Human + GPT archiving owns central writes; local SQLite remains the runtime/query cache.

The workbook is named `Component_Intelligence_Database.xlsx` and contains exactly three authoritative engineering sheets for v1:

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
         ├─ product.jpg
         ├─ mechanical_drawing.pdf|png
         ├─ wiring_drawing.pdf
         ├─ cad_2d.dxf
         └─ cad_3d.step
```

Only files that actually exist are created. Workbook paths are relative to the workbook directory, for example `Documents/OMRON/F03-20/mechanical_drawing.png`. Drive-letter absolute paths are forbidden because Google Drive may be synchronized to different local locations on different computers.

## Components sheet

Core fields:

`ComponentID, Manufacturer, Model, Category, Description, Voltage, IOType, OutputType, Protocol, GeometryType, WidthMm, HeightMm, DepthMm, DiameterMm, MountingType, DatasheetPath, ImagePath, DrawingPath, TopologyStatus, LayoutStatus, DatasheetURL`

Unknown numeric values stay blank. Unknown text values use `Unknown`. Engineering values must not be invented.

For v1 Layout, rectangular components need `WidthMm`, `HeightMm`, and `DepthMm`. Cylindrical/custom geometry may remain review-only until the domain model grows beyond the basic W×H×D footprint.

## Ports sheet

Core fields:

`PortID, ComponentID, PortName, PortRole, Direction, SignalType, Voltage, Protocol, Connector, ConnectorCoding, Gender, PinCount, ActualPinCount, PinCompleteness, PhysicalSide, SourcePage, Notes`

Direction values:

`Input, Output, Bidirectional, Mixed, Passive, Unknown`

`PortRole` and `Direction` are separate facts. A power supply can have `AC_IN = Power Input / Input` and `DC_OUT = Power Output / Output`. A mixed M12 sensor connector can be `Mixed` while its individual pins carry their own directions.

## Pins sheet

Core fields:

`PinID, PortID, PinNumber, PinName, PinRole, Direction, SignalType, Voltage, Function, PinStatus, SourcePage, Notes`

Pin direction may additionally use `Return`.

Pin status values:

`Used, Unused, NC, Reserved, Optional, Unknown, NotApplicable`

`Unknown` is not `NC`. `Unused` means the physical contact exists but the standard engineering connection leaves it open/unused. `NC` is used only when evidence explicitly defines the contact as not connected.

## Complete physical-pin rule

If a connector declares `PinCount = N`, all N physical contacts must exist as Pin rows. Unused, NC, Reserved, Optional, and Unknown contacts are never deleted merely because they do not carry a normal signal.

Example:

```text
M12 5-pin Port
├─ Pin 1  Used
├─ Pin 2  Unknown
├─ Pin 3  Used
├─ Pin 4  Used
└─ Pin 5  NC
```

The desktop maps per-port `PinCount`, coding, gender, role, direction, and physical side into Component IR and then into the electrical project model.

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
