using ComponentIntelligence.Electrical.Domain;
using System.Text.RegularExpressions;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed record SchematicWireContact(string Caption, ComponentPin? Pin, string Function)
{
    public string ShortFunction { get; init; } = "";
}

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

    private static string? InferredCaption(ElectricalProject project, ComponentPin pin)
    {
        var link = pin.InferredFunction!;
        // The link records a runtime PinId, not the source pin's archive lineage ID.
        var sources = Topology.ConnectorMatingService.Ports(project).SelectMany(p => p.Pins)
            .Where(p => string.Equals(p.PinId, link.SourcePinId, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        if (sources.Length != 1) return null;
        var source = sources[0];
        if (source.FunctionManualOverride || source.Status is PinStatus.Nc or PinStatus.Unused or PinStatus.Reserved ||
            string.IsNullOrWhiteSpace(source.Function) ||
            !string.Equals(source.Function.Trim(), link.Value.Trim(), StringComparison.OrdinalIgnoreCase)) return null;
        var function = WithoutColor(source.Function);
        var caption = WithoutColor(source.PinName);
        return caption.Length is > 0 and <= 32 && !caption.Any(char.IsControl) && caption != source.PinNumber.Trim()
            ? CompactFunction(function, caption) : null;
    }

    private static string CompactFunction(string function, string caption)
    {
        var text = Regex.Replace(function, @"\s+", " ").Trim();
        var choices = text.Replace("MS/TP", "MSTP", StringComparison.OrdinalIgnoreCase).Replace("AC/DC", "ACDC", StringComparison.OrdinalIgnoreCase)
            .Replace("TCP/IP", "TCPIP", StringComparison.OrdinalIgnoreCase);
        var selectable = text.Contains("selectable", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("switchable", StringComparison.OrdinalIgnoreCase) || text.Contains(" or ", StringComparison.OrdinalIgnoreCase) ||
            text.Contains('或') || text.Contains('|') || text.Contains(';') || choices.Contains('/') ||
            Regex.IsMatch(text, @"\d\s*(V|mA)\s*/", RegexOptions.IgnoreCase);
        var protocolAlternatives = Regex.IsMatch(text, @"^BACnet MS/TP\s*(?:/|or)\s*Modbus RTU differential data (?:negative|positive)\.?$", RegexOptions.IgnoreCase);
        if (selectable && !protocolAlternatives)
        {
            if (text.Length <= 32) return text;
            const string sensorPrefix = "Optional external sensor input for ";
            if (text.StartsWith(sensorPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var options = text[sensorPrefix.Length..].TrimEnd('.').Replace("switching contact", "接點", StringComparison.OrdinalIgnoreCase)
                    .Replace("active ", "", StringComparison.OrdinalIgnoreCase).Replace(" sensor", "", StringComparison.OrdinalIgnoreCase);
                return "Sensor IN: " + options + "（型式待選）";
            }
            const string analogPrefix = "Selectable analogue output ";
            if (text.StartsWith(analogPrefix, StringComparison.OrdinalIgnoreCase))
                return "AO: " + text[analogPrefix.Length..].TrimEnd('.').Replace(" conductor", "", StringComparison.OrdinalIgnoreCase) + "（模式待選）";
            return text + "（模式待選）";
        }
        if (text.Contains("differential", StringComparison.OrdinalIgnoreCase))
        {
            if (caption.Length is > 0 and <= 32) return caption;
            if (text.Contains("negative", StringComparison.OrdinalIgnoreCase)) return "D-";
            if (text.Contains("positive", StringComparison.OrdinalIgnoreCase)) return "D+";
        }
        if (caption.Length is > 0 and <= 32 && !caption.Any(char.IsControl)) return caption;
        if (text.StartsWith("Common for ", StringComparison.OrdinalIgnoreCase)) return "COM";
        return text;
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
        var function = pin is null ? "" : WithoutColor(pin.DisplayFunction);
        if (function.Length == 0 && pin is not null && !pin.FunctionManualOverride) function = WithoutColor(pin.PinName);
        var inferred = pin?.InferredFunction is not null && !pin.FunctionManualOverride && string.IsNullOrWhiteSpace(pin.Function);
        var shortFunction = pin?.FunctionManualOverride == true ? function : inferred
            ? InferredCaption(project, pin!) ?? function : WithoutColor(pin?.PinName);
        if (shortFunction.Length == 0) shortFunction = function;
        if (!inferred && pin?.FunctionManualOverride != true) shortFunction = CompactFunction(function, shortFunction);
        else if (shortFunction.Any(char.IsControl)) shortFunction = Regex.Replace(shortFunction, @"\s+", " ").Trim();
        return new(reference + "/" + port.Name + (pin is null ? "" : "." + pin.PinNumber), pin, function)
        { ShortFunction = shortFunction };
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
        var owner = SchematicSymbolOwner.Resolve(project, symbol);
        var pin = owner.Ports.SelectMany(p => p.Pins).Single(p => p.PinId == anchor.EndpointId);
        if (connected) return pin.PinNumber;
        if (pin.InferredFunction is not null || pin.FunctionManualOverride)
            return owner.Ports.Single(p => p.Pins.Contains(pin)).Name + " / " + pin.PinNumber + " " + pin.DisplayFunction;
        return anchor.Label ?? anchor.EndpointId;
    }
}
