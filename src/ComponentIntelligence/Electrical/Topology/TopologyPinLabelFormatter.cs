using System;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Topology;

/// <summary>
/// User-facing text for a Topology pin marker. Engineering keys, physical PinNumber,
/// PinID, PortID, and saved wire endpoints must never be changed to shorten a label.
/// </summary>
public static class TopologyPinLabelFormatter
{
    private const string NanoCh340ArchiveId = "電子料_1806251545";

    public static string Format(ComponentInstance component, ComponentPin pin)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(pin);

        // Name is the archived electrical function (D13/SCK, A0, GND, VIN...).
        // Number is the permanent contact-position identity (H1-1, H2-15...).
        var name = !string.IsNullOrWhiteSpace(pin.PinName)
            ? pin.PinName.Trim()
            : !string.IsNullOrWhiteSpace(pin.Function) ? pin.Function.Trim() : null;

        // Only the Nano CH340 module has requested function-only visible labels.
        // Keep H1/H2 in the model and tooltip so existing routed wires still work.
        if (string.Equals(component.ComponentDefinitionId, NanoCh340ArchiveId,
                StringComparison.OrdinalIgnoreCase) && name is not null)
            return name;

        return name is null ? pin.PinNumber : $"{pin.PinNumber} {name}";
    }
}
