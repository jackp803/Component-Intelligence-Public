# Topology Endpoint + Routing v2

> Central archive decisions are governed by [`COMPONENT_ARCHIVE_SPEC_V2.md`](COMPONENT_ARCHIVE_SPEC_V2.md). This document defines how archived Ports/Pins are presented and connected on the Topology canvas.

## 1. Purpose

Topology is not only a component relationship diagram. It must retain enough endpoint identity to become a reliable input for Wiring Diagram / Electrical Drawing generation.

The central archive always keeps the complete physical Pin list. The topology canvas decides whether those Pins are shown individually or collapsed behind a whole-mated connector.

## 2. Central workbook: `Ports.TopologyEndpointMode`

Allowed v2 values:

- `Connector`: show one Port/Connector endpoint by default. M12, RJ45, M8 and other whole-mated connectors normally use this mode.
- `Pins`: show every physical Pin / terminal / loose conductor as an individually connectable endpoint. Screw terminals, terminal blocks, flying leads, loose wires and fixed multi-core pigtails normally use this mode.

The value controls topology presentation/interaction only. It never changes or deletes the `Pins` rows.

### Examples

| Interface | Endpoint mode | Default topology behavior |
|---|---|---|
| M12 A-code 4-contact | Connector | one M12 endpoint; double-click may expand all 4 Pins |
| RJ45 8-contact | Connector | one RJ45 endpoint; double-click may expand all 8 Pins |
| AL5021 fixed 18-wire field cable | Pins | X1.0...X1.15, L-, L+ are all visible/wireable |
| Screw terminal power input | Pins | +V, 0V, PE/FG are individually visible/wireable |
| Passive terminal block | Pins | every physical terminal is individually visible/wireable |

## 3. Pin completeness remains mandatory

`TopologyEndpointMode=Connector` does not mean Pins can be omitted from the archive.

If a connector has N physical contacts, `Pins` must contain all N physical contacts. `NC`, `Unused`, `Reserved`, `Optional`, and `Unknown` contacts remain explicit rows.

`Unknown != NC` and `Unused != NC`.

Do not infer the contact count from the connector family alone. For example, M12 is not automatically 5-pin; use the actual product connector specification.

`Pins.PinID` is the stable archive identity for a contact/conductor. Runtime endpoint creation must preserve that identity rather than rebuilding identity only from `PinNumber`. This is required for engineering identifiers such as `+`, `-`, `V+`, `V-`, `A+`, and `B-`, which must never collapse to the same runtime endpoint after identifier normalization.

## 4. Exact topology connection identity

A topology connection may now terminate at:

- Component Port / whole connector
- Component Pin / loose conductor / screw terminal
- Terminal-block connection point

A Pin-level connection stores the exact Pin IDs in `ElectricalConnection.FromEndpointId` and `ToEndpointId`. It must not be collapsed back to the parent Port when saved.

This permits later drawing logic to distinguish, for example:

```text
AL5021.X1.3 -> Sensor A.OUT
AL5021.L+   -> Sensor A.L+
AL5021.L-   -> Sensor A.L-
```

instead of merely recording `AL5021.FIELD_IO -> Sensor A`.

## 5. Topology component size

Topology blocks are not fixed-size symbols. When a component has many visible endpoint markers, the canvas grows the component block so that endpoints retain readable spacing.

Current editor target is approximately 22 px vertical pitch per visible endpoint on the more populated side, with a larger minimum width when Pin-level endpoints are shown.

Large I/O devices may therefore render taller than ordinary sensors. Readability has priority over keeping every block the same size.

## 6. Connector expand/collapse

`Connector` mode is collapsed by default.

In Select mode, double-clicking a whole-mated Connector toggles its Pin markers:

```text
Collapsed:  [M12] ●

Expanded:   [M12] ●
                 Pin1 ●
                 Pin2 ●
                 Pin3 ●
                 Pin4 ●
```

Expanded Pin markers are real wireable endpoints. Expansion changes presentation only and does not modify archive engineering data.

`Pins` mode is permanently expanded because hiding independent terminals/conductors would make the wiring ambiguous.

## 7. Screen side semantics｜畫面左右規則

Topology screen side and electrical Direction are different concepts.

Generic priority for Port placement:

1. component-specific approved presentation rule, when one exists;
2. clear `PortRole` semantics: Input-role → screen-left; Output-role → screen-right;
3. electrical `Direction` if the role is not directional;
4. Pin engineering semantics if both are still unresolved;
5. mixed / bidirectional / neutral fallback follows the editor presentation policy; current neutral default is right.

`PhysicalSide` describes the real physical connector face and does not override this topology convention. A screen-placement requirement must not be archived as fake `PhysicalSide`, `Direction`, or `PortRole` data.

### OMRON K7L-AT50DP

K7L uses an approved component-specific **presentation rule** that intentionally differs from generic Input-left / Output-right flow. The archive remains truthful:

```text
POWER   = terminals 1 / 8
SENSING = terminals 2 / 3 / 4
OUTPUT  = terminals 5 / 6 / 7

SENSING PortRole = Sensor Input
SENSING Direction = Mixed
TopologyEndpointMode = Pins
```

The Topology canvas renders:

```text
screen-left               screen-right
1 / 8   ──┐          ┌── 2
5 / 6 / 7 ├─ K7L ────┤   3
          │          └── 4
```

Therefore:

- POWER terminals `1 / 8` → **left**.
- OUTPUT terminals `5 / 6 / 7` → **left**.
- SENSING terminals `2 / 3 / 4` → **right**.

This is UI presentation only. Do not change `Direction`, `PortRole`, or `PhysicalSide` merely to reproduce this picture.

### IFM AL1342

X01~X08 are archived truthfully as `IO-Link Port Class A`, `Direction=Mixed`, `TopologyEndpointMode=Connector`.

They must **not** be re-archived as fake `Output` ports merely to force a side. `Direction=Mixed` is a resolved neutral/mixed presentation case and therefore X01~X08 remain on the **right**, even when the real archived Pins include L+, L-, DI, and C/Q.

If a runtime build shows X01~X08 on the wrong side while the archive still contains the values above, treat it as a program/runtime regression, not an archive correction.

## 8. Orthogonal routing

Formal topology lines use 90-degree orthogonal polylines rather than only straight center-to-center lines.

Automatic routing:

1. anchors at the true Port/Pin/terminal marker;
2. generates horizontal/vertical candidate routes;
3. treats other topology component rectangles as obstacles with clearance;
4. strongly penalizes routes crossing another component;
5. chooses the lowest-cost Manhattan route.

A selected route exposes a draggable bend handle. Dragging the handle creates a manual orthogonal waypoint, producing PPT-like editable elbows while preserving true electrical endpoint identity.

Manual route waypoints are currently editor-session visual state; engineering connection endpoint data remains persistent. Persistent multi-waypoint project serialization can be added independently without changing the endpoint contract.

## 9. Palette-first placement + Auto Arrange｜元件清單優先與自動排列

The left `Components` palette is the project/BOM inventory. Presence in the project and presence on the canvas are separate states:

```text
Component exists in BOM / Project
        !=
TopologyPlacement exists on canvas
```

Normal editor behavior:

- Loading a project does **not** create missing `TopologyPlacement` rows.
- Rendering/refreshing the canvas does **not** auto-place missing components.
- An unplaced component remains in the left palette and is shown as `未定位`.
- Dragging an unplaced palette item creates exactly one `TopologyPlacement` for that object.
- Dragging the same item again moves the existing placement; it never creates a duplicate component.
- Existing saved placements are preserved when a project is loaded.
- Removing/moving a canvas placement must not delete the underlying BOM/project component.

`Auto Arrange` is an explicit user command. It may clear/rebuild topology placements and place all project objects using the current arrangement policy. It is not allowed to run implicitly during ordinary render/load/refresh.

Current Auto Arrange layout behavior:

- Controls, masters, power supplies, amplifiers and other infrastructure occupy left/center columns.
- True Sensor components are grouped in an orderly rightmost column.
- A component must not be classified as Sensor merely because its Description contains the word `sensor`; e.g. a sensor amplifier/controller remains an amplifier/controller.
- User manual placement remains editable after auto-arrange.

The archive should provide truthful `Category` / Type information only. Do not add fake Port directions or physical-side values to influence auto-arrange.

## 10. Canvas capacity

The topology workspace is intentionally larger than the visible viewport and is scrollable in both axes. Large endpoint-count devices and a rightmost Sensor column are expected to require more working area than the original fixed small canvas.

Canvas size is a UI setting and must not be encoded into the central archive.

## 11. Archive rules for GPT archiving workflow

When archiving a Port, the GPT archive workflow must:

1. identify the actual physical interface and real contact count;
2. archive every physical Pin/contact;
3. set `TopologyEndpointMode=Connector` only when the interface is normally mated as a complete connector/cable assembly;
4. set `TopologyEndpointMode=Pins` when conductors/terminals are independently selected and wired by the engineer;
5. never change Electrical `Direction` merely to force a topology visual side;
6. keep `PortRole` as the topology/functional role and `Direction` as the truthful electrical behavior;
7. keep all unused/NC/unknown physical Pins explicit;
8. preserve a stable unique `Pins.PinID` for each archived contact/conductor;
9. never use `PhysicalSide` or fake Category values as a screen-placement hack.

## 12. AL5021 reference behavior

For IFM AL5021:

- IO-Link M12 interface: `TopologyEndpointMode=Connector`.
- FIELD_IO fixed 18-wire cable: `TopologyEndpointMode=Pins`.
- Pins/wires 1-16 correspond to X1.0...X1.15 configurable I/O.
- wire 17 is L- (US).
- wire 18 is L+ (US).

The topology therefore exposes all 18 field conductors individually so each sensor I/O, +24 V, and 0 V connection can be recorded exactly.
