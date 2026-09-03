using ComponentIntelligence.Contracts;
using ComponentIntelligence.SymbolArchive;

namespace ComponentIntelligence.Tests.SymbolArchive;

public sealed class SymbolResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ci-symbol-resolve-{Guid.NewGuid():N}");
    public SymbolResolverTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task NoApprovedAsset_ReturnsDeterministicGeneratedGenericUsingExplicitEndpointsOnly()
    {
        var component = Component();
        var resolver = new SymbolResolver(new SymbolArchiveRepository(_root), [component]);
        var first = await resolver.ResolveAsync("C1", SymbolRole.Schematic);
        var second = await resolver.ResolveAsync("C1", SymbolRole.Schematic);
        Assert.Equal(SymbolSourceType.GeneratedGeneric, first.SourceType);
        Assert.Equal("generated-generic.v1", first.Revision);
        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Equal(new[] { "PIN-STABLE", "P-CONNECTOR" }, first.GeneratedGeneric!.Endpoints.Select(item => item.EngineeringEndpointId));
        Assert.DoesNotContain(first.GeneratedGeneric.Endpoints, endpoint => endpoint.EngineeringEndpointId == "2");
    }

    [Fact]
    public async Task ApprovedAssetWinsAndTamperFailsClosedInsteadOfGenericFallback()
    {
        var repository = new SymbolArchiveRepository(_root);
        var component = Component();
        var source = Path.Combine(_root, "source.dwg");
        File.WriteAllText(source, "approved");
        await new SymbolArchiveApprovalService(repository, [component]).ApproveAsync(new ApproveSymbolRequest
        {
            SourcePath = source, ComponentId = "C1", Role = SymbolRole.Schematic,
            SourceType = SymbolSourceType.Manufacturer, UserConfirmed = true
        });
        var resolver = new SymbolResolver(repository, [component]);
        var resolved = await resolver.ResolveAsync("C1", SymbolRole.Schematic);
        Assert.Equal(SymbolSourceType.Manufacturer, resolved.SourceType);
        var asset = repository.ResolveArchivePath(resolved.AssetPath);
        File.AppendAllText(asset, "tamper");
        await Assert.ThrowsAsync<InvalidDataException>(() => resolver.ResolveAsync("C1", SymbolRole.Schematic));
    }

    [Fact]
    public async Task IndependentRolesResolveIndependently()
    {
        var repository = new SymbolArchiveRepository(_root);
        var component = Component();
        foreach (var (role, name, sourceType) in new[]
        {
            (SymbolRole.Schematic, "schematic.dwg", SymbolSourceType.ApprovedCustom),
            (SymbolRole.PanelFootprint, "panel.dxf", SymbolSourceType.Manufacturer)
        })
        {
            var source = Path.Combine(_root, name); File.WriteAllText(source, name);
            await new SymbolArchiveApprovalService(repository, [component]).ApproveAsync(new ApproveSymbolRequest
            {
                SourcePath = source, ComponentId = "C1", Role = role, SourceType = sourceType, UserConfirmed = true
            });
        }
        var resolver = new SymbolResolver(repository, [component]);
        Assert.Equal(SymbolSourceType.ApprovedCustom, (await resolver.ResolveAsync("C1", SymbolRole.Schematic)).SourceType);
        Assert.Equal(SymbolSourceType.Manufacturer, (await resolver.ResolveAsync("C1", SymbolRole.PanelFootprint)).SourceType);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    internal static ComponentIR Component() => new()
    {
        Identity = new ComponentIrIdentity { ComponentId = "C1", Manufacturer = "MFR", Model = "MODEL" },
        Ports =
        [
            new ComponentPort { PortId = "P-PINS", TopologyEndpointMode = "Pins", ConnectorFamily = "M12", PinCount = 8 },
            new ComponentPort { PortId = "P-CONNECTOR", TopologyEndpointMode = "Connector", ConnectorFamily = "RJ45", PinCount = 8 }
        ],
        Pins =
        [
            new ComponentPin { PinId = "PIN-STABLE", PortId = "P-PINS", PinNumber = "1" },
            new ComponentPin { PinId = null, PortId = "P-PINS", PinNumber = "2" }
        ]
    };
}
