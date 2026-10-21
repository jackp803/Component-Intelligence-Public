using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Connectivity;

public sealed record ProjectPinFunctionIssue(string Code, string Description, IReadOnlyList<string> PinIds);

public static class ProjectPinFunctionService
{
    public static IReadOnlyList<ProjectPinFunctionIssue> Synchronize(ElectricalProject project)
    {
        var (inferred, issues) = Analyze(project);
        foreach (var pin in Pins(project)) pin.InferredFunction = inferred.GetValueOrDefault(pin.PinId);
        return issues;
    }
    public static IReadOnlyList<ProjectPinFunctionIssue> Inspect(ElectricalProject project) => Analyze(project).Issues;
    public static IReadOnlyList<ComponentPin> Pins(ElectricalProject project) =>
        Topology.ConnectorMatingService.Ports(project).SelectMany(p => p.Pins).ToArray();
    public static void SetOverride(ElectricalProject project, string pinId, string? function, bool manual)
    {
        var pin = Pins(project).SingleOrDefault(p => p.PinId == pinId) ?? throw new InvalidOperationException("Pin 已不存在。");
        if (function?.Length > 512 || function?.Any(char.IsControl) == true) throw new InvalidOperationException("功能文字過長或含無效字元。");
        pin.FunctionManualOverride = manual; pin.Function = manual ? function?.Trim() ?? "" : null; Synchronize(project);
    }
    private static (Dictionary<string, ProjectPinFunctionLink> Inferred, IReadOnlyList<ProjectPinFunctionIssue> Issues) Analyze(ElectricalProject project)
    {
        var pins = Pins(project).ToDictionary(p => p.PinId, StringComparer.OrdinalIgnoreCase);
        var inferred = new Dictionary<string, ProjectPinFunctionLink>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<ProjectPinFunctionIssue>();
        foreach (var group in new TerminalConnectivityEngine().BuildGroups(project))
        {
            var members = group.EndpointIds.Where(pins.ContainsKey).Select(id => pins[id]).ToArray();
            var sources = members.Where(p => Eligible(p) && Normalize(p.Function) is not null).OrderBy(p => p.PinId, StringComparer.Ordinal).ToArray();
            var values = sources.Select(p => Normalize(p.Function)!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (values.Length > 1)
            {
                issues.Add(new("FUNCTION_CONFLICT", "同一路功能標示不同，已保留手動值：" + string.Join(" / ", values), members.Select(p => p.PinId).ToArray()));
                continue;
            }
            if (sources.Length == 0)
            {
                if (members.Length > 1 && members.Any(Eligible))
                    issues.Add(new("FUNCTION_PENDING", "這組接線尚無已設定的功能來源。", members.Select(p => p.PinId).ToArray()));
                continue;
            }
            foreach (var pin in members.Where(p => Eligible(p) && !p.FunctionManualOverride && Normalize(p.Function) is null))
                inferred[pin.PinId] = new(sources[0].PinId, Normalize(sources[0].Function)!);
        }
        foreach (var cable in project.Cables.Where(c => c.ArchivedCable is { MappingConfirmed: false }))
            issues.Add(new("UNCONFIRMED_INTERNAL_MAPPING", (cable.ReferenceDesignator ?? cable.DisplayName ?? cable.CableInstanceId) +
                "：內部接法未確認，功能映射停在此線材。", cable.ArchivedCable!.Ports.SelectMany(p => p.Pins).Select(p => p.PinId).ToArray()));
        foreach (var connection in project.Connections.Where(c => c.Kind == ConnectionKind.DirectMating && c.MatingMap is null))
            issues.Add(new("UNCONFIRMED_MATING_MAPPING", "既有接頭對接尚未核對逐 Pin 對應。", [connection.FromEndpointId, connection.ToEndpointId]));
        var connected = ProjectConductiveEdges.ConnectedEndpointIds(project);
        foreach (var pin in pins.Values.Where(p => p.IsRequired && Eligible(p) && !connected.Contains(p.PinId)))
            issues.Add(new("REQUIRED_PIN_UNCONNECTED", "必要 Pin " + pin.PinNumber + " 尚未接線。", [pin.PinId]));
        return (inferred, issues);
    }
    private static bool Eligible(ComponentPin pin) => pin.Status is not (PinStatus.Nc or PinStatus.Unused or PinStatus.Reserved);
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) || value.Trim() is "待確認" or "Unknown" or "N/C" or "NC" ? null : value.Trim();
}
