using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed record SchematicWireContact(string Caption, ComponentPin? Pin, string Function);

public static class SchematicWireContacts
{
    private static readonly Dictionary<string, (string Name, string Hex)> Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = ("black", "#1F1F1F"), ["BK"] = ("black", "#1F1F1F"),
        ["red"] = ("red", "#C62828"), ["RD"] = ("red", "#C62828"),
        ["white"] = ("white", "#FFFFFF"), ["WH"] = ("white", "#FFFFFF"),
        ["orange"] = ("orange", "#CC6200"), ["OG"] = ("orange", "#CC6200"),
        ["pink"] = ("pink", "#C21873"), ["PK"] = ("pink", "#C21873"),
        ["grey"] = ("grey", "#667078"), ["gray"] = ("grey", "#667078"), ["GY"] = ("grey", "#667078"),
        ["brown"] = ("brown", "#79502C"), ["BN"] = ("brown", "#79502C"),
        ["blue"] = ("blue", "#1766B5"), ["BU"] = ("blue", "#1766B5"),
        ["green"] = ("green", "#287A3D"), ["GN"] = ("green", "#287A3D"),
        ["yellow"] = ("yellow", "#E3BB22"), ["YE"] = ("yellow", "#E3BB22"),
        ["violet"] = ("violet", "#783AB7"), ["purple"] = ("violet", "#783AB7"), ["VT"] = ("violet", "#783AB7")
    };

    // Legacy lead records explicitly end their pin description with '/ color'. Never infer polarity from it.
    public static (string Name, string Hex)? Color(ComponentPin pin)
    {
        foreach (var text in new[] { pin.PinName, pin.Function })
            if (text?.LastIndexOf('/') is >= 0 and var slash && Colors.TryGetValue(text[(slash + 1)..].Trim(), out var color))
                return color;
        return null;
    }

    private static string WithoutColor(string? text)
    {
        if (text is null) return "";
        var slash = text.LastIndexOf('/');
        return (slash >= 0 && Colors.ContainsKey(text[(slash + 1)..].Trim()) ? text[..slash] : text).Trim();
    }

    public static SchematicWireContact? Resolve(ElectricalProject project, SchematicAttachment attachment)
    {
        if (attachment.Kind is not (SchematicAttachmentKind.Pin or SchematicAttachmentKind.Port)) return null;
        var symbol = project.Schematic?.Symbols.SingleOrDefault(s => s.SymbolId == attachment.SymbolId);
        var owner = symbol is null ? null : SchematicSymbolOwner.Resolve(project, symbol);
        var ports = owner?.Ports ?? project.Components.SelectMany(c => c.Ports).ToList();
        var port = ports.SingleOrDefault(p => p.PortId == attachment.EndpointId || p.Pins.Any(pin => pin.PinId == attachment.EndpointId));
        if (port is null) return null;
        var pin = attachment.Kind == SchematicAttachmentKind.Pin ? port.Pins.SingleOrDefault(p => p.PinId == attachment.EndpointId) : null;
        var reference = owner?.Reference;
        if (string.IsNullOrWhiteSpace(reference) && symbol?.ComponentInstanceId is string id)
            reference = project.Schematic?.WireNamingReferences.GetValueOrDefault(id);
        if (string.IsNullOrWhiteSpace(reference)) reference = port.Name;
        var function = pin is null ? "" : WithoutColor(pin.Function);
        if (function.Length == 0 && pin is not null) function = WithoutColor(pin.PinName);
        return new(reference + "/" + port.Name + (pin is null ? "" : "." + pin.PinNumber), pin, function);
    }

    public static IReadOnlyList<SchematicWireContact> Ends(ElectricalProject project, SchematicWire wire)
    {
        var segments = project.Schematic is { } doc ? SchematicWireIdentification.Segments(doc, wire) : [wire];
        return segments.SelectMany(w => new[] { w.Start, w.End }).Select(a => Resolve(project, a))
            .Where(c => c is not null).Cast<SchematicWireContact>().DistinctBy(c => c.Caption).ToArray();
    }

    public static string AnchorText(ElectricalProject project, SchematicSymbol symbol, SchematicAnchor anchor)
    {
        var connected = project.Schematic!.Wires.Any(w => new[] { w.Start, w.End }.Any(a =>
            a.Kind == SchematicAttachmentKind.Pin && a.SymbolId == symbol.SymbolId && a.EndpointId == anchor.EndpointId)
            && Ends(project, w).Count >= 2 && SchematicCableLabelPresentation.Resolve(project, w) is not null);
        if (!connected) return anchor.Label ?? anchor.EndpointId;
        var owner = SchematicSymbolOwner.Resolve(project, symbol);
        return owner.Ports.SelectMany(p => p.Pins).Single(p => p.PinId == anchor.EndpointId).PinNumber;
    }
}
