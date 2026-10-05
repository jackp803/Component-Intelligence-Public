# Wire Identification And Visible Port Panels

## User-Approved Scope

- Show a general conductor's saved wire designation, AWG and conductor area in mm2 on the engineering drawing and PDF.
- Use archived consumption current, or DC input watts divided by the documented minimum operating voltage when current is absent. Never use source capacity as load consumption, and never derive AC current from watts alone.
- Unknown Sensor loads may use an editable product draft default. No default or automatic proposal is an engineering approval or a guarantee against overheating.
- Allocate stable component-based names, for example `IOLink1_X1_1`. Preserve explicit Reference, Port and Pin identities, including signed Pin names. Similar unnamed components receive separate stable numbers.
- Show P1 and P2 beside each other in the manufacturing editor. Each has independent connector family, Coding, gender, name and Pin-count controls. Add creates another visible Port panel; delete applies to that panel only.
- Do not infer continuity from a connector family, a visual shape or a collapsed multi-Pin Port. Existing mapping confirmation and inventory protections remain in effect.

## Specification Ownership

Automatic proposals store area, known load subtotal, source basis and unresolved review conditions in the editable project. The UI, project validation and PDF remain explicit about pending review. Manual AWG/metric selection replaces the proposal, but does not itself grant engineering approval.

Explicit continuation pairs share one conductor's name and specification. Conflicting manual sizes or manual names reject a join without mutating the project. Separating incomplete continuations allocates distinct names for the newly separate conductors. Junction branches retain separate conductor names. Only explicit junctions contribute loads; crossings and equal text never join networks.

Catalog-controlled cables retain authoritative catalog specifications. Old automatic wire values cannot override the selected cable. An approximate mathematical AWG equivalent is marked as approximate, not presented as an exact catalog gauge. Metric area is never rounded to the approximate AWG area.

Names and component naming reservations are persisted, so moves, page transfers and reloads do not renumber existing conductors. Explicit wire-name edits are supported and checked for conflicts. Clearing a prior automatic AWG choice cannot silently convert its provisional area into an approved manual value.

## Source And Applicability Record

Checked 2026-10-05, without buying standards documents.

1. [LAPP T12 basic current-rating reference](https://products.lappgroup.com/fileadmin/catalog/en_T_pdf/T12_Current_ratings__basic_table_int.pdf), table 12-1, page 1. It identifies DIN VDE 0298-4:2013-06 tables 11/15 and DIN VDE 0891:1990-05 part 1 as excerpt sources. The product uses a small category-B excerpt at 30 C: 0.5/3, 0.75/6, 1/10, 1.5/16, 2.5/20 and 4/25 (mm2/A), selecting the lower three-loaded-core value where two/three-core ratings differ. This reference category is not a complete machine-wiring method. The source directs machinery users to DIN EN 60204-1/VDE 0113-1; its simplified excerpt does not replace the applicable current standard, installation assessment or derating.
2. [Schneider conductor sizing methodology](https://www.electrical-installation.org/enwiki/Conductor_sizing%3A_methodology_and_definition) and [general cable sizing](https://www.electrical-installation.org/enwiki/General_method_for_cable_sizing) support retaining installation, temperature, grouping, protection and voltage-drop checks instead of declaring a current-only result approved.
3. [Schneider steady-load voltage drop](https://www.electrical-installation.org/enwiki/Calculation_of_voltage_drop_in_steady_load_conditions), G29, page revision 2026-08-05, gives copper/XLPE resistance `23.7 / S` ohm/km. The product's DC preliminary calculation is an explicitly labeled inference using an equal-area return conductor: `minimum area = 2 * I * 23.7 * L(km) / allowed drop(V)`. Copper/XLPE, equal return area and circuit-level applicability remain assumptions to verify. The user's saved drop limit is used, not asserted to be an international requirement.
4. `PRODUCT-SENSOR-DRAFT-V1` is a product default, not an international standard: start at 0.5 mm2, respect known connector termination bounds, and keep the value unconfirmed. Incompatible bounds produce no numeric proposal.

Actual cable length must be supplied with an authoritative length-source value. Drawing length, view scale and module movement are never cable-length sources. Material, current and each supplied length/drop-limit field are validated independently. AC, known voltage above 60 V, aluminum, invalid data and out-of-range reference sizes are not automatically sized by this limited workflow. Unknown branch loads, unknown material and installation/protection details remain unresolved. This feature does not replace engineering design or final verification.

## Verification And Acceptance

Core regressions cover load-based sizing, Sensor defaults, invalidation, manual overrides, continuation ownership/conflicts, catalog precedence, termination limits, actual-length separation, signed Pins and stable component numbering.

`validation/CableConnectorEditorSmoke` supports `all`, `panels` and `wire`. It instantiates controls offscreen without taking over the user's desktop. The wire mode checks preview/cancel isolation, atomic apply, invalid metric input, automatic restoration, project serialization, actual canvas labels and PDF output. Final interactive acceptance is performed by the user.

Development for this change is direct native implementation and validation, per the user's authorization and subsequent explicit instruction not to use AO. No AO invocation is required to build or run the product. Central workbooks, user CAD files and unrelated local drawing-planning work are not modified by this change.
