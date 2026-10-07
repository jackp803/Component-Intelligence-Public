using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed partial class SchematicAuthoringService
{
    public ElectricalProject SetWireArea(ElectricalProject project, string wireId, double areaMm2)
    {
        if (!double.IsFinite(areaMm2) || areaMm2 <= 0 || areaMm2 > 1000) throw new ArgumentOutOfRangeException(nameof(areaMm2));
        return Edit(project, (draft, doc) =>
        {
            var wire = doc.Wires.Single(w => w.WireId == wireId);
            var members = SchematicWireIdentification.Segments(doc, wire);
            if (members.Any(w => w.Locked)) throw new InvalidOperationException("A route segment is locked.");
            var connection = draft.Connections.SingleOrDefault(c => c.ConnectionId == wire.ConnectionId);
            if (connection?.CableCoreId is not null) throw new InvalidOperationException("Edit the authoritative cable core specification before overriding its wire size.");
            foreach (var member in members)
                doc.Wires[doc.Wires.FindIndex(w => w.WireId == member.WireId)] = member with
                { Awg = null, AreaMm2 = areaMm2, ManualSpecification = true, SizingProposal = null };
            if (connection is not null) connection.ConductorAreaMm2 = areaMm2;
        });
    }

    public ElectricalProject UseAutomaticWireSize(ElectricalProject project, string wireId) => Edit(project, (draft, doc) =>
    {
        var wire = doc.Wires.Single(w => w.WireId == wireId);
        var members = SchematicWireIdentification.Segments(doc, wire);
        if (members.Any(w => w.Locked)) throw new InvalidOperationException("A route segment is locked.");
        var connection = draft.Connections.SingleOrDefault(c => c.ConnectionId == wire.ConnectionId);
        if (connection?.CableCoreId is not null) throw new InvalidOperationException("The exact catalog core controls this wire size.");
        foreach (var member in members)
            doc.Wires[doc.Wires.FindIndex(w => w.WireId == member.WireId)] = member with
            { Awg = null, AreaMm2 = null, ManualSpecification = false, SizingProposal = null };
        if (connection is not null) connection.ConductorAreaMm2 = null;
    });

    public ElectricalProject SetWireDesignation(ElectricalProject project, string wireId, string designation) => Edit(project, (_, doc) =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(designation);
        var name = designation.Trim();
        if (name.Length > 100 || name.Any(char.IsControl)) throw new InvalidOperationException("Wire name must be at most 100 characters without control characters.");
        var wire = doc.Wires.Single(w => w.WireId == wireId);
        var members = SchematicWireIdentification.Segments(doc, wire);
        var ids = members.Select(w => w.WireId).ToHashSet(StringComparer.Ordinal);
        if (members.Any(w => w.Locked)) throw new InvalidOperationException("A route segment is locked.");
        if (doc.Wires.Any(w => !ids.Contains(w.WireId) && string.Equals(w.Designation, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("This wire name is already used by another conductor.");
        foreach (var member in members)
            doc.Wires[doc.Wires.FindIndex(w => w.WireId == member.WireId)] = member with { Designation = name, ManualDesignation = true };
    });
}
