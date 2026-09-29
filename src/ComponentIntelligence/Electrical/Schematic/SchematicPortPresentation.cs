using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public static class SchematicPortPresentation
{
    public static SchematicGridBounds GenericBodyBounds(SchematicDocument doc, SchematicSymbol symbol,
        SchematicSymbolOwner owner)
    {
        var rotated = symbol.Rotation % 180 != 0;
        var original = new SchematicGridBounds(symbol.Position.X, symbol.Position.Y,
            rotated ? symbol.Height : symbol.Width, rotated ? symbol.Width : symbol.Height);
        if (symbol.Geometry is not null || !owner.Ports.Any(port => IsRepresented(symbol, port) && IsPortCollapsed(symbol, port))) return original;

        var visible = symbol.Anchors.Where(a => !IsCollapsedPin(doc, symbol, owner, a)).ToArray();
        var groups = owner.Ports.Where(p => IsRepresented(symbol, p) && IsPortCollapsed(symbol, p)).ToArray();
        var horizontal = new[] { "Top", "Bottom" }.Max(side =>
            groups.Count(p => PortSide(symbol, p) == side) * GroupPitch(groups.Where(p => PortSide(symbol, p) == side)) + 10);
        var vertical = new[] { "Left", "Right" }.Max(side =>
            groups.Count(p => PortSide(symbol, p) == side) * GroupPitch(groups.Where(p => PortSide(symbol, p) == side)) + 10);
        var offsets = visible.Select(a => AnchorOffset(symbol, a)).ToArray();
        var width = Math.Max(30, Math.Max(horizontal,
            offsets.Length == 0 ? 0 : offsets.Max(p => p.X) - offsets.Min(p => p.X) + 12));
        var height = Math.Max(30, Math.Max(vertical,
            offsets.Length == 0 ? 0 : offsets.Max(p => p.Y) - offsets.Min(p => p.Y) + 12));
        var x = offsets.Length == 0 || width >= original.Width ? 0 :
            Math.Clamp((offsets.Min(p => p.X) + offsets.Max(p => p.X) - width) / 2, 0, original.Width - width);
        var y = offsets.Length == 0 || height >= original.Height ? 0 :
            Math.Clamp((offsets.Min(p => p.Y) + offsets.Max(p => p.Y) - height) / 2, 0, original.Height - height);
        return new(original.X + x, original.Y + y, width, height);
    }

    public static (SchematicPoint Position, string Side) GroupContact(SchematicDocument doc,
        SchematicSymbol symbol, SchematicSymbolOwner owner, ComponentPort port, SchematicGridBounds body)
    {
        var side = PortSide(symbol, port);
        var sameSide = owner.Ports.Where(p => IsRepresented(symbol, p) && (IsPortCollapsed(symbol, p) ||
            HasPortRoute(doc, symbol, p.PortId)) &&
            PortSide(symbol, p) == side)
            .OrderBy(p => GroupCoordinate(symbol, p, side)).ThenBy(p => p.PortId, StringComparer.Ordinal).ToArray();
        var index = Array.FindIndex(sameSide, p => p.PortId == port.PortId);
        if (index < 0) throw new InvalidOperationException("The selected port has no visible group contact.");
        var coordinate = GroupCoordinate(symbol, port, side);
        var rotated = symbol.Rotation % 180 != 0;
        var originalStart = side is "Top" or "Bottom" ? symbol.Position.X : symbol.Position.Y;
        var originalExtent = side is "Top" or "Bottom"
            ? rotated ? symbol.Height : symbol.Width
            : rotated ? symbol.Width : symbol.Height;
        var fraction = port.Pins.Count == 0 || originalExtent <= 0
            ? (index + 1d) / (sameSide.Length + 1d)
            : Math.Clamp((coordinate - originalStart) / originalExtent, 0.025, 0.975);
        var position = side switch
        {
            "Left" => new SchematicPoint(body.X, body.Y + body.Height * fraction),
            "Top" => new SchematicPoint(body.X + body.Width * fraction, body.Y),
            "Bottom" => new SchematicPoint(body.X + body.Width * fraction, body.Y + body.Height),
            _ => new SchematicPoint(body.X + body.Width, body.Y + body.Height * fraction)
        };
        return (position, side);
    }

    public static SchematicPoint ConnectionPoint(ElectricalProject project, SchematicSymbol symbol, string portId)
    {
        var doc = project.Schematic ?? throw new InvalidOperationException("The schematic is missing.");
        var owner = SchematicSymbolOwner.Resolve(project, symbol);
        var port = owner.Ports.SingleOrDefault(p => p.PortId == portId)
            ?? throw new InvalidOperationException("The Port endpoint is not part of this representation.");
        if (!IsRepresented(symbol, port))
            throw new InvalidOperationException("This representation does not contain the complete Port interface; connect an exact Pin instead.");
        var body = GenericBodyBounds(doc, symbol, owner);
        return GroupContact(doc, symbol, owner, port, body).Position;
    }

    public static (SchematicPoint Position, int Rotation) GroupLabel(SchematicPoint contact, string side) => side switch
    {
        "Top" => (new(contact.X + 2, contact.Y - 3), -90),
        "Bottom" => (new(contact.X - 2, contact.Y + 3), 90),
        "Left" => (new(contact.X - 13, contact.Y - 5), 0),
        _ => (new(contact.X + 2, contact.Y - 5), 0)
    };

    public static bool HasPortRoute(SchematicDocument doc, SchematicSymbol symbol, string portId) =>
        doc.Wires.Any(wire =>
            wire.Start.Kind == SchematicAttachmentKind.Port && wire.Start.SymbolId == symbol.SymbolId && wire.Start.EndpointId == portId ||
            wire.End.Kind == SchematicAttachmentKind.Port && wire.End.SymbolId == symbol.SymbolId && wire.End.EndpointId == portId);

    public static bool IsPortCollapsed(SchematicSymbol symbol, ComponentPort port) =>
        port.Pins.Count == 0 || symbol.CollapsedPortIds.Contains(port.PortId, StringComparer.Ordinal);

    public static bool IsRepresented(SchematicSymbol symbol, ComponentPort port) =>
        port.Pins.Count == 0 ? symbol.SectionIndex == 1 :
            port.Pins.All(pin => symbol.Anchors.Any(anchor => anchor.EndpointId == pin.PinId));

    private static double GroupPitch(IEnumerable<ComponentPort> ports) =>
        Math.Max(16, ports.Select(p => p.Name.Length * 1.9 + 6).DefaultIfEmpty(0).Max());

    private static double GroupCoordinate(SchematicSymbol symbol, ComponentPort port, string side)
    {
        var ids = port.Pins.Select(pin => pin.PinId).ToHashSet(StringComparer.Ordinal);
        var points = symbol.Anchors.Where(a => ids.Contains(a.EndpointId))
            .Select(a => SchematicAuthoringService.AnchorPoint(symbol, a.EndpointId)).ToArray();
        return points.Length == 0 ? 0 : side is "Top" or "Bottom" ? points.Average(p => p.X) : points.Average(p => p.Y);
    }

    private static SchematicPoint AnchorOffset(SchematicSymbol symbol, SchematicAnchor anchor)
    {
        var point = SchematicAuthoringService.AnchorPoint(symbol, anchor.EndpointId);
        return new(point.X - symbol.Position.X, point.Y - symbol.Position.Y);
    }

    private static string PortSide(SchematicSymbol symbol, ComponentPort port)
    {
        var pinIds = port.Pins.Select(p => p.PinId).ToHashSet(StringComparer.Ordinal);
        var direction = symbol.Anchors.FirstOrDefault(a => pinIds.Contains(a.EndpointId))?.Direction ??
            (port.PhysicalLocation?.Side is "Left" or "Right" or "Top" or "Bottom" ? port.PhysicalLocation.Side : "Right");
        for (var i = 0; i < symbol.Rotation; i += 90) direction = direction switch
        { "Left" => "Top", "Top" => "Right", "Right" => "Bottom", _ => "Left" };
        return direction;
    }
    public static bool IsCollapsedPin(SchematicDocument doc, SchematicSymbol symbol,
        SchematicSymbolOwner owner, SchematicAnchor anchor) =>
        owner.Ports.Any(port => IsRepresented(symbol, port) && symbol.CollapsedPortIds.Contains(port.PortId, StringComparer.Ordinal) &&
            port.Pins.Any(pin => pin.PinId == anchor.EndpointId)) &&
        !doc.Wires.Any(wire =>
            wire.Start.SymbolId == symbol.SymbolId && wire.Start.EndpointId == anchor.EndpointId ||
            wire.End.SymbolId == symbol.SymbolId && wire.End.EndpointId == anchor.EndpointId);

    public static (string Side, double Coordinate) DropTarget(SchematicSymbol symbol, SchematicPoint world)
    {
        var x = world.X - symbol.Position.X;
        var y = world.Y - symbol.Position.Y;
        var local = symbol.Rotation switch
        {
            90 => new SchematicPoint(y, symbol.Height - x),
            180 => new SchematicPoint(symbol.Width - x, symbol.Height - y),
            270 => new SchematicPoint(symbol.Width - y, x),
            _ => new SchematicPoint(x, y)
        };
        var distances = new (string Side, double Distance, double Coordinate)[]
        {
            ("Left", Math.Abs(local.X), Math.Clamp(local.Y, 0, symbol.Height)),
            ("Right", Math.Abs(local.X - symbol.Width), Math.Clamp(local.Y, 0, symbol.Height)),
            ("Top", Math.Abs(local.Y), Math.Clamp(local.X, 0, symbol.Width)),
            ("Bottom", Math.Abs(local.Y - symbol.Height), Math.Clamp(local.X, 0, symbol.Width))
        };
        var closest = distances.MinBy(edge => edge.Distance);
        return (closest.Side, closest.Coordinate);
    }

    public static (string Side, double Coordinate) DropTarget(SchematicSymbol symbol, SchematicPoint world,
        SchematicGridBounds body)
    {
        var u = Math.Clamp((world.X - body.X) / body.Width, 0, 1);
        var v = Math.Clamp((world.Y - body.Y) / body.Height, 0, 1);
        var local = symbol.Rotation switch
        {
            90 => new SchematicPoint(v * symbol.Width, (1 - u) * symbol.Height),
            180 => new SchematicPoint((1 - u) * symbol.Width, (1 - v) * symbol.Height),
            270 => new SchematicPoint((1 - v) * symbol.Width, u * symbol.Height),
            _ => new SchematicPoint(u * symbol.Width, v * symbol.Height)
        };
        var distances = new (string Side, double Distance, double Coordinate)[]
        {
            ("Left", local.X / symbol.Width, local.Y),
            ("Right", 1 - local.X / symbol.Width, local.Y),
            ("Top", local.Y / symbol.Height, local.X),
            ("Bottom", 1 - local.Y / symbol.Height, local.X)
        };
        var closest = distances.MinBy(edge => edge.Distance);
        return (closest.Side, closest.Coordinate);
    }
}
