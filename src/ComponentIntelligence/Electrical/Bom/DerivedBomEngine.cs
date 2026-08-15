using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Bom;

public enum ElectricalBomItemKind
{
    Original,
    Manual,
    TerminalBlock,
    ShortingJumper,
    CableProduct,
    CableAssembly,
    Connector,
    Adapter,
    Other
}

public enum MaterialResolutionStatus
{
    Resolved,
    NeedsSelection,
    LengthPending,
    CustomDefined,
    Unknown
}

public sealed record ElectricalBomLine
{
    public required string LineId { get; init; }
    public required ElectricalBomItemKind Kind { get; init; }
    public string? Manufacturer { get; init; }
    public string? PartNumber { get; init; }
    public required string Description { get; init; }
    public decimal Quantity { get; init; }
    public string Unit { get; init; } = "EA";
    public MaterialResolutionStatus ResolutionStatus { get; init; } = MaterialResolutionStatus.Unknown;
    public string Source { get; init; } = "DERIVED";
    public string? RuleId { get; init; }
    public string? Reason { get; init; }
    public IReadOnlyList<string> SourceObjectIds { get; init; } = Array.Empty<string>();
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record CableLengthPolicy
{
    public double PercentageAllowance { get; init; }
    public double FixedAllowanceMm { get; init; }
    public double ServiceLoopMmPerEnd { get; init; }

    public double Apply(double providedLengthMm)
    {
        if (providedLengthMm < 0) throw new ArgumentOutOfRangeException(nameof(providedLengthMm));
        return providedLengthMm * (1 + PercentageAllowance / 100.0) + FixedAllowanceMm + ServiceLoopMmPerEnd * 2;
    }
}

public sealed class DerivedBomEngine
{
    public IReadOnlyList<ElectricalBomLine> Build(
        ElectricalProject project,
        IEnumerable<CableDefinition>? cableLibrary = null,
        CableLengthPolicy? lengthPolicy = null,
        DateTimeOffset? generatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        lengthPolicy ??= new CableLengthPolicy();
        var timestamp = generatedAt ?? DateTimeOffset.UtcNow;
        var cableDefinitions = (cableLibrary ?? Array.Empty<CableDefinition>())
            .ToDictionary(item => item.CableDefinitionId, StringComparer.OrdinalIgnoreCase);
        var lines = new List<ElectricalBomLine>();

        foreach (var block in project.TerminalBlocks)
        {
            lines.Add(new ElectricalBomLine
            {
                LineId = $"derived:terminal:{block.TerminalBlockId}",
                Kind = ElectricalBomItemKind.TerminalBlock,
                Description = $"Terminal block {block.ReferenceDesignator} ({block.Positions.Count} position(s)){Suffix(block.FunctionTag)}",
                Quantity = block.Positions.Count,
                Unit = "POSITION",
                ResolutionStatus = MaterialResolutionStatus.NeedsSelection,
                RuleId = "RULE-BOM-TERM-001",
                Reason = "Terminal positions exist in the electrical design but no manufacturer terminal product is attached yet.",
                SourceObjectIds = new[] { block.TerminalBlockId },
                GeneratedAt = timestamp
            });

            foreach (var jumper in block.Jumpers)
            {
                var resolved = !string.IsNullOrWhiteSpace(jumper.Manufacturer) && !string.IsNullOrWhiteSpace(jumper.PartNumber);
                lines.Add(new ElectricalBomLine
                {
                    LineId = $"derived:jumper:{jumper.JumperId}",
                    Kind = ElectricalBomItemKind.ShortingJumper,
                    Manufacturer = jumper.Manufacturer,
                    PartNumber = jumper.PartNumber,
                    Description = $"Shorting jumper for {block.ReferenceDesignator}, {jumper.ConnectionPointIds.Count} connected point(s)",
                    Quantity = 1,
                    Unit = "EA",
                    ResolutionStatus = resolved ? MaterialResolutionStatus.Resolved : MaterialResolutionStatus.NeedsSelection,
                    RuleId = "RULE-BOM-JUMPER-001",
                    Reason = "A real shorting jumper is required because the design intentionally makes these terminal positions electrically common.",
                    SourceObjectIds = new[] { block.TerminalBlockId, jumper.JumperId },
                    GeneratedAt = timestamp
                });
            }
        }

        foreach (var assembly in project.CableAssemblies)
        {
            lines.Add(new ElectricalBomLine
            {
                LineId = $"derived:cable-assembly:{assembly.CableAssemblyId}",
                Kind = ElectricalBomItemKind.CableAssembly,
                Description = assembly.IsCustom
                    ? $"Custom cable assembly {assembly.ReferenceDesignator ?? assembly.CableAssemblyId} ({assembly.Members.Count} cable member(s))"
                    : $"Cable assembly {assembly.ReferenceDesignator ?? assembly.CableAssemblyId}",
                Quantity = 1,
                Unit = "EA",
                ResolutionStatus = assembly.IsCustom ? MaterialResolutionStatus.CustomDefined : MaterialResolutionStatus.NeedsSelection,
                RuleId = "RULE-BOM-CABLE-ASM-001",
                Reason = assembly.IsCustom
                    ? "The project explicitly defines a custom cable assembly; it must appear in the effective BOM even if absent from the imported BOM."
                    : "The project contains a cable assembly that requires a purchasable or manufacturable definition.",
                SourceObjectIds = new[] { assembly.CableAssemblyId }.Concat(assembly.Members.Select(member => member.CableInstanceId)).ToArray(),
                GeneratedAt = timestamp
            });
        }

        foreach (var cable in project.Cables)
        {
            cableDefinitions.TryGetValue(cable.CableDefinitionId, out var definition);
            var providedLengthMm = cable.ProvidedLengthMm is > 0 ? cable.ProvidedLengthMm : null;
            var finalLengthMm = providedLengthMm is double length ? lengthPolicy.Apply(length) : (double?)null;
            var materialResolved = definition is not null && !string.IsNullOrWhiteSpace(definition.Manufacturer) && !string.IsNullOrWhiteSpace(definition.PartNumber);
            var status = finalLengthMm is null
                ? MaterialResolutionStatus.LengthPending
                : materialResolved ? MaterialResolutionStatus.Resolved : MaterialResolutionStatus.NeedsSelection;

            lines.Add(new ElectricalBomLine
            {
                LineId = $"derived:cable:{cable.CableInstanceId}",
                Kind = ElectricalBomItemKind.CableProduct,
                Manufacturer = definition?.Manufacturer,
                PartNumber = definition?.PartNumber,
                Description = materialResolved
                    ? $"Cable {definition!.Manufacturer} {definition.PartNumber}"
                    : $"Cable material for {cable.ReferenceDesignator ?? cable.CableInstanceId}",
                Quantity = finalLengthMm is double final ? (decimal)(final / 1000.0) : 0,
                Unit = "M",
                ResolutionStatus = status,
                RuleId = "RULE-BOM-CABLE-001",
                Reason = finalLengthMm is null
                    ? "Cable exists but no authoritative cable length has been supplied by Mechanical / User / Import; layout and route geometry are not used as a substitute."
                    : $"Cable quantity uses the externally supplied cable length ({cable.LengthSource}) plus the configured allowance policy; drawn route geometry is visual/reference data only.",
                SourceObjectIds = new[] { cable.CableInstanceId },
                GeneratedAt = timestamp
            });
        }

        return lines;
    }

    public IReadOnlyList<ElectricalBomLine> BuildEffective(
        IEnumerable<ElectricalBomLine> original,
        IEnumerable<ElectricalBomLine> manual,
        IEnumerable<ElectricalBomLine> derived)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(manual);
        ArgumentNullException.ThrowIfNull(derived);
        return original.Concat(manual).Concat(derived).ToArray();
    }

    private static string Suffix(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : $" - {value.Trim()}";
}
