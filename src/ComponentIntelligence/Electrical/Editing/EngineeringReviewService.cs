using System.Text.Json;
using System.Text.Json.Nodes;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.SymbolArchive;

namespace ComponentIntelligence.Electrical.Editing;

public sealed record EngineeringCandidate(string ComponentId, string Manufacturer, string Model, string Evidence)
{
    public string Label => $"{Manufacturer} / {Model} ({ComponentId})";
}
public sealed record EngineeringEndpoint(string Id, string Kind, string Label, string? ParentId, bool Required)
{
    public override string ToString() => Label;
}
public sealed record ComponentEngineeringReview(string InstanceId, string DefinitionId, string Label, string Manufacturer,
    string Model, string Classification, string Reason, IReadOnlyList<EngineeringEndpoint> Endpoints, string Context)
{
    public override string ToString() => Label;
}
public sealed record CableEngineeringReview(string CableId, string Label, string ModelContext, string From, string To,
    string ConstructionType, string Classification)
{
    public override string ToString() => Label;
}
public sealed record EngineeringCoverage(IReadOnlyList<ComponentEngineeringReview> Components, IReadOnlyList<CableEngineeringReview> Cables)
{
    public int UnsafeComponentRoles => Components.Where(c => c.Classification == "UNSAFE_UNRESOLVED").Select(c => c.DefinitionId).Distinct(StringComparer.Ordinal).Count();
    public int UnsafeCableRoles => Cables.Count(c => c.Classification == "UNSAFE_UNRESOLVED");
    public bool IsSafe => UnsafeComponentRoles == 0 && UnsafeCableRoles == 0;
}

/// <summary>Read-only evidence audit and explicit, copy-on-write human decisions. No database or catalog writes.</summary>
public sealed class EngineeringReviewService
{
    private readonly IReadOnlyDictionary<string, ComponentIR> _catalog;
    private readonly SymbolResolver _resolver;
    private readonly IReadOnlyDictionary<string, ComponentIR> _context;
    public IReadOnlyList<EngineeringCandidate> Candidates { get; }

    public EngineeringReviewService(IReadOnlyList<ComponentIR> catalog, SymbolArchiveRepository archive, string catalogEvidence,
        IReadOnlyList<ComponentIR>? exactPersistedContext = null)
    {
        _catalog = catalog.ToDictionary(c => c.Identity.ComponentId, StringComparer.Ordinal);
        _resolver = new SymbolResolver(archive, catalog);
        _context = (exactPersistedContext ?? []).ToDictionary(c => c.Identity.ComponentId, StringComparer.Ordinal);
        Candidates = catalog.OrderBy(c => c.Identity.Manufacturer, StringComparer.Ordinal).ThenBy(c => c.Identity.Model, StringComparer.Ordinal)
            .Select(c => new EngineeringCandidate(c.Identity.ComponentId, c.Identity.Manufacturer, c.Identity.Model,
                $"{catalogEvidence}; authoritative catalog entry, NOT an automatic identity match. {c.Assets.DatasheetUrl}")).ToArray();
    }

    public IReadOnlyList<EngineeringEndpoint> CatalogEndpoints(string componentId)
    {
        var c = _catalog[componentId];
        return c.Ports.Select(p => new EngineeringEndpoint(p.PortId, "Port", $"{p.PortName ?? p.PortId} / {p.ConnectorFamily}", null, false))
            .Concat(c.Pins.Where(p => !string.IsNullOrWhiteSpace(p.PinId)).Select(p => new EngineeringEndpoint(p.PinId!, "Pin",
                $"{c.Ports.SingleOrDefault(x => x.PortId == p.PortId)?.PortName ?? p.PortId} / Pin {p.PinNumber} / {p.PinName} / {p.Function}", p.PortId, false))).ToArray();
    }

    public async Task<EngineeringCoverage> InspectAsync(ElectricalProject original, CancellationToken ct = default)
    {
        var project = Clone(original);
        var rows = new List<ComponentEngineeringReview>();
        var used = project.Connections.SelectMany(c => new[] { c.FromEndpointId, c.ToEndpointId })
            .Concat(project.Cables.SelectMany(c => c.CoreAssignments.SelectMany(a => new[] { a.FromEndpointId, a.ToEndpointId })))
            .Where(x => x is not null).ToHashSet(StringComparer.Ordinal);
        foreach (var instance in project.Components.OrderBy(c => c.ComponentInstanceId, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            _catalog.TryGetValue(instance.ComponentDefinitionId, out var source);
            var status = "UNSAFE_UNRESOLVED";
            var reason = "無 exact 型錄身分／端點權威；不可依名稱自動配對。";
            var endpoints = InstanceEndpoints(instance, used);
            if (InlineInterfaceRepresentation.IsRecognized(instance.ComponentDefinitionId))
            {
                var blocking = InlineInterfaceRepresentation.BlockingReason(project, instance);
                status = blocking is null ? "SAFE_INLINE_INTERFACE" : "UNSAFE_UNRESOLVED";
                reason = blocking ?? "專案明確 interface/contact 與使用端點完整；僅中性介面表示，不代表型錄或圖塊核准。";
            }
            else if (source is not null)
            {
                var lineage = new ComponentSourceIdentityRestorer().Restore(instance, source);
                if (lineage.Status == SourceIdentityStatus.CONFLICT) reason = "型錄來源端點有衝突，需核對工程證據。";
                else
                {
                    try
                    {
                        var bridge = instance.Ports.Select(p => (Source: p.SourcePortId, Instance: p.PortId))
                            .Concat(instance.Ports.SelectMany(p => p.Pins.Select(x => (Source: x.SourcePinId, Instance: x.PinId)))).ToArray();
                        var requiredSources = bridge.Where(b => used.Contains(b.Instance) && b.Source is not null).Select(b => b.Source!).Distinct(StringComparer.Ordinal).ToArray();
                        var resolved = await _resolver.ResolveAsync(source.Identity.ComponentId, SymbolRole.Schematic, cancellationToken: ct, requiredEndpointIds: requiredSources);
                        var represented = new HashSet<string>(StringComparer.Ordinal);
                        var invalid = false;
                        foreach (var binding in resolved.PortBindings)
                        {
                            var matches = bridge.Where(x => x.Source == binding.EngineeringEndpointId).ToArray();
                            if (matches.Length != 1) invalid = true;
                            else represented.Add(matches[0].Instance);
                        }
                        var missing = endpoints.Where(e => e.Required && !represented.Contains(e.Id)).ToArray();
                        if (!invalid && missing.Length == 0)
                        {
                            status = resolved.SourceType == SymbolSourceType.GeneratedGeneric ? "SAFE_GENERATED_GENERIC" : "APPROVED_EXACT";
                            reason = "Exact 型錄／來源端點對照與所有使用端點覆蓋成立。";
                        }
                        else reason = "需補足明確端點覆蓋：" + string.Join("; ", missing.Select(e => e.Label)) + (invalid ? "；來源對照不唯一／缺少。" : "");
                    }
                    catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException)
                    { reason = "型錄／圖塊證據無效：" + ex.Message; }
                }
            }
            var own = endpoints.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
            var context = string.Join("\n", project.Connections.Where(c => own.Contains(c.FromEndpointId) || own.Contains(c.ToEndpointId))
                .Select(c => $"{EndpointLabel(project, c.FromEndpointId)} → {EndpointLabel(project, c.ToEndpointId)}"));
            _context.TryGetValue(instance.ComponentDefinitionId, out var persisted);
            rows.Add(new(instance.ComponentInstanceId, instance.ComponentDefinitionId, ComponentLabel(instance),
                source?.Identity.Manufacturer ?? persisted?.Identity.Manufacturer ?? "未確認",
                source?.Identity.Model ?? persisted?.Identity.Model ?? "未確認", status, reason, endpoints, context));
        }
        var cables = project.Cables.OrderBy(c => c.CableInstanceId, StringComparer.Ordinal).Select(c =>
        {
            var connections = project.Connections.Where(x => x.CableInstanceId == c.CableInstanceId).ToArray();
            var from = connections.Select(x => x.FromEndpointId).Concat(c.CoreAssignments.Select(x => x.FromEndpointId)).Where(x => x is not null).Distinct(StringComparer.Ordinal);
            var to = connections.Select(x => x.ToEndpointId).Concat(c.CoreAssignments.Select(x => x.ToEndpointId)).Where(x => x is not null).Distinct(StringComparer.Ordinal);
            return new CableEngineeringReview(c.CableInstanceId, string.Join(" / ", new[] { c.ReferenceDesignator, c.DisplayName }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } label ? label : "未命名線材",
                c.CableDefinitionId, string.Join("\n", from.Select(x => EndpointLabel(project, x!))), string.Join("\n", to.Select(x => EndpointLabel(project, x!))),
                c.CableConstructionType.ToString(), c.CableConstructionType switch
                {
                    CableConstructionType.Purchased => "SAFE_PURCHASED_FUNCTIONAL",
                    CableConstructionType.Custom => "SAFE_CUSTOM_FUNCTIONAL",
                    _ => "UNSAFE_UNRESOLVED"
                });
        }).ToArray();
        return new(rows, cables);
    }

    public ElectricalProject ReviewCable(ElectricalProject project, string cableId, CableConstructionType? choice, bool confirmed)
    {
        if (!confirmed || choice is not (CableConstructionType.Purchased or CableConstructionType.Custom))
            throw new InvalidOperationException("請明確選擇外購或自製並確認；尚未修改資料。");
        var draft = Clone(project);
        var cable = draft.Cables.Single(c => c.CableInstanceId == cableId);
        if (cable.CableConstructionType != CableConstructionType.Unknown)
            throw new InvalidOperationException("此線材已不在待確認清單，請重新檢查。");
        cable.CableConstructionType = choice.Value;
        return draft;
    }

    public async Task<ElectricalProject> ReviewComponentAsync(ElectricalProject project, string instanceId, string? canonicalId,
        IReadOnlyDictionary<string, string> mappings, string evidence, bool confirmed)
    {
        if (!confirmed || string.IsNullOrWhiteSpace(evidence) || canonicalId is null || !_catalog.TryGetValue(canonicalId, out var source))
            throw new InvalidOperationException("必須明確確認型錄身分、端點對照及工程證據；尚未修改資料。");
        var draft = Clone(project);
        var index = draft.Components.FindIndex(c => c.ComponentInstanceId == instanceId);
        if (index < 0) throw new InvalidOperationException("專案元件已不存在。");
        var instance = draft.Components[index];
        var endpoints = InstanceEndpoints(instance, new HashSet<string?>());
        if (mappings.Count != endpoints.Count || endpoints.Any(e => !mappings.ContainsKey(e.Id))
            || mappings.Values.Distinct(StringComparer.Ordinal).Count() != mappings.Count)
            throw new InvalidOperationException("請逐一提供唯一的 Port / Pin 對照，不可漏填或重複。");
        foreach (var port in instance.Ports)
        {
            if (source.Ports.Count(p => p.PortId == mappings[port.PortId]) != 1)
                throw new InvalidOperationException("Port 對照不是唯一型錄 Port。");
            foreach (var pin in port.Pins)
                if (source.Pins.Count(p => p.PinId == mappings[pin.PinId] && p.PortId == mappings[port.PortId]) != 1)
                    throw new InvalidOperationException("Pin 必須屬於所選 Port 且具有唯一型錄 PinID。");
        }
        // Replace only identity fields on the clone; keep every instance/endpoint/geometry ID.
        var node = JsonSerializer.SerializeToNode(instance)!;
        node[nameof(ComponentInstance.ComponentDefinitionId)] = canonicalId;
        instance = node.Deserialize<ComponentInstance>()!;
        foreach (var port in instance.Ports)
        {
            port.SourcePortId = mappings[port.PortId];
            port.Capabilities.RemoveAll(c => c.StartsWith("SOURCE_PORT_ID:", StringComparison.Ordinal));
            port.Capabilities.Add("SOURCE_PORT_ID:" + port.SourcePortId);
            foreach (var pin in port.Pins) pin.SourcePinId = mappings[pin.PinId];
        }
        if (new ComponentSourceIdentityRestorer().Analyze(instance, source).Status == SourceIdentityStatus.CONFLICT)
            throw new InvalidOperationException("人工對照與既有明確来源衝突，需先核對；尚未修改資料。");
        draft.Components[index] = instance;
        var coverage = await InspectAsync(draft);
        if (coverage.Components.Single(c => c.InstanceId == instanceId).Classification == "UNSAFE_UNRESOLVED")
            throw new InvalidOperationException("身分／對照仍未形成完整端點覆蓋；未套用，請保留 unresolved。");
        return draft;
    }

    public static ElectricalProject Clone(ElectricalProject project) =>
        JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(project))!;
    private static string ComponentLabel(ComponentInstance c) =>
        string.Join(" / ", new[] { c.ReferenceDesignator, c.DisplayName, c.EquipmentTag }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } label ? label : "未命名元件";
    private static IReadOnlyList<EngineeringEndpoint> InstanceEndpoints(ComponentInstance c, IReadOnlySet<string?> used) =>
        c.Ports.SelectMany(p => new[] { new EngineeringEndpoint(p.PortId, "Port", $"{p.Name} / {p.Connector?.Family} {p.Connector?.Coding} {p.Connector?.Gender}", null, used.Contains(p.PortId)) }
            .Concat(p.Pins.Select(x => new EngineeringEndpoint(x.PinId, "Pin", $"{p.Name} / Pin {x.PinNumber} / {x.PinName} / {x.Function}", p.PortId, used.Contains(x.PinId))))).ToArray();
    private static string EndpointLabel(ElectricalProject project, string id)
    {
        foreach (var c in project.Components)
        {
            var matches = InstanceEndpoints(c, new HashSet<string?>()).Where(e => e.Id == id).ToArray();
            if (matches.Length > 1) return $"端點身分不唯一 ({id})";
            var endpoint = matches.SingleOrDefault();
            if (endpoint is not null) return $"{ComponentLabel(c)} / {endpoint.Label}";
        }
        return $"未解析端點 ({id})";
    }
}
