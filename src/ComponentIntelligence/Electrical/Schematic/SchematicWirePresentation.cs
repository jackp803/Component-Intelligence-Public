using ComponentIntelligence.Electrical.Cables;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed record SchematicWireSpecification(int? Awg, double? AreaMm2, string? PhysicalColor);

public static class SchematicWirePresentation
{
    public static SchematicWireSpecification Resolve(ElectricalProject project, SchematicWire wire,
        IReadOnlyList<CableDefinition>? definitions = null)
    {
        var connection = project.Connections.SingleOrDefault(c => c.ConnectionId == wire.ConnectionId);
        var cable = project.Cables.SingleOrDefault(c => c.CableInstanceId == connection?.CableInstanceId);
        var core = cable is null || connection?.CableCoreId is null ? null
            : definitions?.SingleOrDefault(d => d.CableDefinitionId == cable.CableDefinitionId)?.Cores
                .SingleOrDefault(c => c.CoreId == connection.CableCoreId);
        if (core?.Awg is int catalogAwg && wire.Awg is int selectedAwg && catalogAwg != selectedAwg)
            throw new InvalidOperationException("Selected AWG conflicts with the exact catalog core.");
        var awg = core?.Awg ?? wire.Awg;
        var area = core?.AreaMm2 ?? (awg is int value ? WireSize.AwgToAreaMm2(value) : connection?.ConductorAreaMm2);
        return new(awg, area, core?.ColorCode);
    }

    // Preview weights only; conductor cross-section is stored separately.
    public static double StrokeWidthMm(int? awg) => awg switch
    {
        null => .35,
        < 0 or > 40 => throw new ArgumentOutOfRangeException(nameof(awg)),
        <= 10 => .7,
        <= 18 => .5,
        <= 26 => .35,
        _ => .25
    };
}
