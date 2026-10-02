using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed partial class SchematicAuthoringService
{
    private const double ContactMargin = 6;
    private const double ContactSpacing = 6;

    public ElectricalProject ResizeGenericSymbol(ElectricalProject project, string symbolId,
        double displayedWidth, double displayedHeight) => Edit(project, (draft, doc) =>
    {
        var index = doc.Symbols.FindIndex(s => s.SymbolId == symbolId);
        if (index < 0) throw new InvalidOperationException("Select a component representation.");
        var symbol = doc.Symbols[index];
        if (symbol.Geometry is not null) throw new InvalidOperationException("Archived CAD geometry must be revised in its source asset.");
        if (symbol.Locked) throw new InvalidOperationException("Unlock the representation before resizing it.");
        if (!double.IsFinite(displayedWidth) || !double.IsFinite(displayedHeight))
            throw new InvalidOperationException("Choose a finite module size.");

        var owner = SchematicSymbolOwner.Resolve(draft, symbol);
        var oldBody = SchematicPortPresentation.GenericBodyBounds(doc, symbol, owner);
        var contacts = new List<(string Id, string Side, double Fraction)>();
        foreach (var anchor in symbol.Anchors.Where(a => !SchematicPortPresentation.IsCollapsedPin(doc, symbol, owner, a)))
        {
            var side = SchematicPortPresentation.RotatedSide(anchor.Direction, symbol.Rotation);
            contacts.Add(("pin:" + anchor.EndpointId, side,
                EdgeFraction(SchematicAuthoringService.AnchorPoint(symbol, anchor.EndpointId), side, oldBody)));
        }
        foreach (var port in owner.Ports.Where(p => SchematicPortPresentation.IsRepresented(symbol, p) &&
            (SchematicPortPresentation.IsPortCollapsed(symbol, p) || SchematicPortPresentation.HasPortRoute(doc, symbol, p.PortId))))
        {
            var contact = SchematicPortPresentation.GroupContact(doc, symbol, owner, port, oldBody);
            contacts.Add(("port:" + port.PortId, contact.Side, EdgeFraction(contact.Position, contact.Side, oldBody)));
        }
        var width = Math.Max(30, displayedWidth);
        var height = Math.Max(30, displayedHeight);
        foreach (var group in contacts.GroupBy(c => c.Side, StringComparer.Ordinal))
        {
            var required = 2 * ContactMargin + (group.Count() - 1) * ContactSpacing;
            if (group.Key is "Top" or "Bottom") width = Math.Max(width, required);
            else height = Math.Max(height, required);
        }
        var body = new SchematicGridBounds(oldBody.X, oldBody.Y, width, height);
        var resolved = new Dictionary<string, SchematicPoint>(StringComparer.Ordinal);
        foreach (var group in contacts.GroupBy(c => c.Side, StringComparer.Ordinal))
        {
            var sorted = group.OrderBy(c => c.Fraction).ThenBy(c => c.Id, StringComparer.Ordinal).ToArray();
            var extent = group.Key is "Top" or "Bottom" ? width : height;
            var along = new double[sorted.Length];
            for (var i = 0; i < sorted.Length; i++)
            {
                var desired = ContactMargin + sorted[i].Fraction * (extent - 2 * ContactMargin);
                along[i] = Math.Max(desired, i == 0 ? ContactMargin : along[i - 1] + ContactSpacing);
            }
            var overflow = Math.Max(0, along[^1] - (extent - ContactMargin));
            for (var i = 0; i < sorted.Length; i++)
                resolved[sorted[i].Id] = EdgePoint(body, group.Key, along[i] - overflow);
        }
        var rotated = symbol.Rotation % 180 != 0;
        var originalFrame = new SchematicGridBounds(symbol.Position.X, symbol.Position.Y,
            rotated ? symbol.Height : symbol.Width, rotated ? symbol.Width : symbol.Height);
        var next = symbol with { Position = new(body.X, body.Y), Width = rotated ? height : width,
            Height = rotated ? width : height, ManualSize = true, AssetRevision = null };
        next = next with { Anchors = symbol.Anchors.Select(anchor =>
        {
            var side = SchematicPortPresentation.RotatedSide(anchor.Direction, symbol.Rotation);
            var oldPoint = AnchorPoint(symbol, anchor.EndpointId);
            var fraction = EdgeFraction(oldPoint, side, resolved.ContainsKey("pin:" + anchor.EndpointId) ? oldBody : originalFrame);
            var extent = side is "Top" or "Bottom" ? width : height;
            var point = resolved.GetValueOrDefault("pin:" + anchor.EndpointId) ??
                EdgePoint(body, side, ContactMargin + fraction * (extent - 2 * ContactMargin));
            var local = LocalPoint(next, point);
            return anchor with { Position = local, Confirmed = false, CadContactId = null };
        }).ToList() };
        next = next with { PortPlacements = contacts.Where(c => c.Id.StartsWith("port:", StringComparison.Ordinal))
            .Select(c =>
            {
                var side = SchematicPortPresentation.RotatedSide(c.Side, (360 - symbol.Rotation) % 360);
                var local = LocalPoint(next, resolved[c.Id]);
                return new SchematicPortPlacement { PortId = c.Id[5..], Side = side,
                    Coordinate = side is "Top" or "Bottom" ? local.X : local.Y };
            }).ToList() };
        ReanchorChangedSymbol(draft, doc, index, symbol, next);
    });

    public ElectricalProject MovePinToEdge(ElectricalProject project, string symbolId, string pinId,
        string side, double coordinate) => Edit(project, (draft, doc) =>
    {
        var index = doc.Symbols.FindIndex(s => s.SymbolId == symbolId);
        if (index < 0) throw new InvalidOperationException("Select a component representation.");
        var symbol = doc.Symbols[index];
        if (symbol.Geometry is not null) throw new InvalidOperationException("Archived CAD contacts must be revised in the source asset.");
        if (!symbol.ManualSize) throw new InvalidOperationException("Resize the module first, then position individual Pins.");
        if (symbol.Locked) throw new InvalidOperationException("Unlock the representation before moving a Pin.");
        if (!double.IsFinite(coordinate) || side is not ("Left" or "Right" or "Top" or "Bottom"))
            throw new InvalidOperationException("Choose a finite position on a module edge.");
        var anchor = symbol.Anchors.SingleOrDefault(a => a.EndpointId == pinId)
            ?? throw new InvalidOperationException("The selected Pin is not on this representation.");
        var extent = side is "Top" or "Bottom" ? symbol.Width : symbol.Height;
        var along = Math.Clamp(coordinate, ContactMargin, extent - ContactMargin);
        var local = side switch
        {
            "Top" => new SchematicPoint(along, 0),
            "Bottom" => new SchematicPoint(along, symbol.Height),
            "Left" => new SchematicPoint(0, along),
            _ => new SchematicPoint(symbol.Width, along)
        };
        var next = symbol with { Anchors = symbol.Anchors.Select(a => a.EndpointId == pinId
            ? a with { Position = local, Direction = side, Confirmed = false, CadContactId = null } : a).ToList(),
            AssetRevision = null };
        EnsureMovedContactsClear(draft, doc, next, ["pin:" + pinId]);
        ReanchorChangedSymbol(draft, doc, index, symbol, next);
    });

    private static void ReanchorChangedSymbol(ElectricalProject draft, SchematicDocument doc, int index,
        SchematicSymbol old, SchematicSymbol next)
    {
        foreach (var wire in doc.Wires.Where(w => w.Locked &&
            (w.Start.SymbolId == old.SymbolId || w.End.SymbolId == old.SymbolId)))
            if (new[] { wire.Start, wire.End }.Where(a => a.SymbolId == old.SymbolId)
                .Any(a => SymbolAttachmentPoint(draft, old, a) != SymbolAttachmentPoint(draft, next, a)))
                throw new InvalidOperationException("Unlock attached wires before moving their contacts.");
        doc.Symbols[index] = next;
        for (var i = 0; i < doc.Wires.Count; i++)
        {
            var wire = doc.Wires[i];
            if (wire.Start.SymbolId != old.SymbolId && wire.End.SymbolId != old.SymbolId) continue;
            var points = wire.Points.ToList();
            if (wire.Start.SymbolId == old.SymbolId) points = ReanchorSymbol(points, draft, next, wire.Start, true);
            if (wire.End.SymbolId == old.SymbolId) points = ReanchorSymbol(points, draft, next, wire.End, false);
            if (CanReroutePortWire(doc, wire)) points = ReroutePortWire(draft, doc, wire);
            else if (CanRerouteSimpleWire(doc, wire)) points = RerouteSimpleWire(draft, doc, wire);
            doc.Wires[i] = wire with { Points = points };
            ReanchorDependentJunctions(doc, wire.WireId);
        }
    }

    private static void EnsureMovedContactsClear(ElectricalProject project, SchematicDocument doc,
        SchematicSymbol candidate, IReadOnlyCollection<string> moved)
    {
        var owner = SchematicSymbolOwner.Resolve(project, candidate);
        var visible = new List<(string Id, SchematicPoint Point)>();
        foreach (var anchor in candidate.Anchors.Where(a => !SchematicPortPresentation.IsCollapsedPin(doc, candidate, owner, a)))
            visible.Add(("pin:" + anchor.EndpointId, AnchorPoint(candidate, anchor.EndpointId)));
        var body = SchematicPortPresentation.GenericBodyBounds(doc, candidate, owner);
        foreach (var port in owner.Ports.Where(p => SchematicPortPresentation.IsRepresented(candidate, p) &&
            (SchematicPortPresentation.IsPortCollapsed(candidate, p) || SchematicPortPresentation.HasPortRoute(doc, candidate, p.PortId))))
            visible.Add(("port:" + port.PortId, SchematicPortPresentation.GroupContact(doc, candidate, owner, port, body).Position));
        foreach (var contact in visible.Where(c => moved.Contains(c.Id)))
            if (visible.Any(other => other.Id != contact.Id &&
                Math.Pow(other.Point.X - contact.Point.X, 2) + Math.Pow(other.Point.Y - contact.Point.Y, 2) < 25))
                throw new InvalidOperationException("This contact overlaps another Pin or Port. Resize the module or choose another position.");
    }

    private static double EdgeFraction(SchematicPoint point, string side, SchematicGridBounds body)
    {
        var fraction = side is "Top" or "Bottom" ? (point.X - body.X) / body.Width : (point.Y - body.Y) / body.Height;
        return Math.Clamp(fraction, 0, 1);
    }

    private static SchematicPoint EdgePoint(SchematicGridBounds body, string side, double along) => side switch
    {
        "Top" => new(body.X + along, body.Y),
        "Bottom" => new(body.X + along, body.Y + body.Height),
        "Left" => new(body.X, body.Y + along),
        _ => new(body.X + body.Width, body.Y + along)
    };

    private static SchematicPoint LocalPoint(SchematicSymbol symbol, SchematicPoint world)
    {
        var x = world.X - symbol.Position.X;
        var y = world.Y - symbol.Position.Y;
        return symbol.Rotation switch
        {
            90 => new(y, symbol.Height - x),
            180 => new(symbol.Width - x, symbol.Height - y),
            270 => new(symbol.Width - y, x),
            _ => new(x, y)
        };
    }
}
