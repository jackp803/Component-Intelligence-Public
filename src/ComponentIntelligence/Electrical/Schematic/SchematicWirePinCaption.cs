using System.Text.RegularExpressions;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public static class SchematicWirePinCaption
{
    public static string Text(ElectricalProject project, SchematicWire wire)
    {
        var doc = project.Schematic!;
        var segments = SchematicWireIdentification.Segments(doc, wire);
        var crossPage = segments.Select(w => w.PageId).Distinct(StringComparer.Ordinal).Skip(1).Any();
        var attachments = crossPage ? segments.SelectMany(w => new[] { w.Start, w.End })
            .Where(a => a.Kind is SchematicAttachmentKind.Pin or SchematicAttachmentKind.Port).DistinctBy(a => (a.SymbolId, a.EndpointId)).ToArray()
            : new[] { wire.Start, wire.End };
        var connection = project.Connections.SingleOrDefault(c => c.ConnectionId == wire.ConnectionId);
        if (crossPage && connection is not null)
            attachments = attachments.OrderBy(a => a.EndpointId == connection.FromEndpointId ? 0 : 1).ToArray();
        var tokens = attachments.Select(a => Endpoint(project, a, crossPage)).ToArray();
        if (tokens.All(t => t == "未接點")) return wire.Designation ?? "未接點";
        return string.Join(" → ", tokens);
    }

    private static string Endpoint(ElectricalProject project, SchematicAttachment attachment, bool includeReference)
    {
        var contact = SchematicWireContacts.Resolve(project, attachment);
        if (contact is null) return attachment.Kind == SchematicAttachmentKind.WireJunction ? "分支" : "未接點";
        var symbol = project.Schematic!.Symbols.Single(s => s.SymbolId == attachment.SymbolId);
        var owner = SchematicSymbolOwner.Resolve(project, symbol);
        var port = owner.Ports.Single(p => p.PortId == attachment.EndpointId || p.Pins.Any(pin => pin.PinId == attachment.EndpointId));
        var token = contact.Pin is { } pin ? pin.PinNumber + "_" + Function(contact) : port.Name + "_接頭";
        return includeReference ? (owner.Reference ?? project.Schematic.WireNamingReferences.GetValueOrDefault(symbol.ComponentInstanceId) ?? owner.DisplayName) + ":" + token : token;
    }

    private static string Function(SchematicWireContact contact)
    {
        var pin = contact.Pin!;
        var function = Regex.Replace(contact.Function, @"\s+", " ").Trim();
        if (!pin.FunctionManualOverride)
        {
            if (function.StartsWith("Shared auxiliary-switch contact;", StringComparison.OrdinalIgnoreCase)) return "共點";
            if (function.StartsWith("Auxiliary-switch contact paired with S1 ", StringComparison.OrdinalIgnoreCase)) return "接點";
            if (function.Equals("Supply/control conductor", StringComparison.OrdinalIgnoreCase)) return "供電/控制";
            if (function.StartsWith("Common for ", StringComparison.OrdinalIgnoreCase)) return "COM";
            if (function.StartsWith("Optional external sensor input for ", StringComparison.OrdinalIgnoreCase)) return "Sensor";
            if (function.StartsWith("Selectable analogue output ", StringComparison.OrdinalIgnoreCase))
                return function.Contains("MP-Bus", StringComparison.OrdinalIgnoreCase) ? "AO/MP-Bus" : "AO（模式待選）";
        }
        var shortText = contact.ShortFunction;
        // Bare lead numbers such as '1 / black' are not functional descriptions.
        if (shortText == pin.PinNumber || Regex.IsMatch(shortText, @"^S?\d+$", RegexOptions.IgnoreCase)) shortText = function;
        if (shortText.Length is > 0 and <= 24 && !shortText.Any(char.IsControl)) return shortText;
        return function.Length is > 0 and <= 24 ? function : "待辨識";
    }
}
