# Schematic Display Identification

The application controls display colours; there is no per-wire colour picker.
Display does not change a conductor's purchased core colour, Net identity,
voltage, or power-domain membership.

## Bounded Profile

`IEC60445-2021-DC-PREVIEW` uses the 2021 clause 6.2.4 DC polarity convention:
explicit positive DC is red; explicit negative DC is white (a contrasting screen
outline keeps it visible on paper). Unknown, AC, and merely labelled return remain
unclassified. Zero volts alone does not establish neutral, protective earth, or
negative polarity. Conflicting typed endpoint classifications produce a separate
diagnostic display, not a repaired or joined circuit. Shades, outlines and the
diagnostic colour are application display choices, not normative physical colours.

Evidence is exact runtime PinId lookup and structured PowerCapability only.
Names, voltage-looking references, model strings, list positions and whole-port
selection do not establish polarity. The sidebar shows declared endpoint ratings,
not measured operating voltage or an approved project power-domain assignment.

The official [IEC publication description](https://webstore.iec.ch/en/publication/66712)
defines the conductor-identification scope and lists a 2026 amendment. The publicly
accessible [IEC 2021 redline preview, clause 6.2.4](https://cdn.standards.iteh.ai/samples/104151/3ed43237dea640b3bde4c5343880117d/IEC-60445-2021.pdf)
was consulted for this bounded DC mapping. The full 2026 amendment and complete
project-specific installation applicability have not been verified. This profile
is therefore not a claim of latest-edition compliance, a wiring instruction,
or company-standard approval. No standard was purchased.

Distinct voltages do not imply distinct mandated colours. Labels and routes remain
necessary. Explicit continuation pairs carry the same endpoint display evidence
without creating a Net. Full AC/control/PE conventions remain open; do not infer
them to make a colourful drawing.

## Size

Explicit AWG is converted by the existing WireSize formula and preserved with
the schematic. If exact CableDefinitionId/CoreId data is supplied, its core size
is authoritative. The current Desktop adapter does not yet supply that optional
core catalog; do not claim runtime catalog AWG integration complete.
Preview weights are bounded display weights, not physical conductor diameters.
No AWG is inferred from voltage. Current, routing, temperature, derating and
voltage-drop requirements still require engineering evidence.
