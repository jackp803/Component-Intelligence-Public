using ComponentIntelligence.Cache;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.SymbolArchive;

namespace ComponentIntelligence.Tests.SymbolArchive;

public sealed class SymbolArchiveApprovalServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ci-symbol-approve-{Guid.NewGuid():N}");
    public SymbolArchiveApprovalServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task ExplicitConfirmationAndStableEndpointAreRequiredBeforeWrite()
    {
        var source = Source("a.dwg", "one");
        var service = Service();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveAsync(Request(source) with { UserConfirmed = false }));
        Assert.False(File.Exists(Path.Combine(_root, SymbolArchiveRepository.FileName)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveAsync(Request(source) with
        {
            PortBindings = [new SymbolPortBinding { EngineeringEndpointId = "invented-pin", ConnectionPointId = "TERM01" }]
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveAsync(Request(source) with { SourceType = SymbolSourceType.GeneratedGeneric }));
    }

    [Fact]
    public async Task ApprovalCopiesImmutableRevisionAndExactDuplicateDoesNotCreateRev002()
    {
        var source = Source("a.dwg", "one");
        var before = await HashService.Sha256FileAsync(source);
        var service = Service();
        var first = await service.ApproveAsync(Request(source));
        var duplicate = await service.ApproveAsync(Request(source));
        Assert.Equal(SymbolApprovalDisposition.CreatedRevision, first.Disposition);
        Assert.Equal("rev-001", first.Revision);
        Assert.Equal(SymbolApprovalDisposition.ExactDuplicate, duplicate.Disposition);
        Assert.Equal(before, await HashService.Sha256FileAsync(source));
        Assert.False(Directory.Exists(Path.Combine(_root, "Documents", "MFR", "MODEL", "autocad", "schematic", "rev-002")));
    }

    [Fact]
    public async Task DifferentContentCreatesNextRevisionAndSupersedesPrevious()
    {
        var service = Service();
        var firstSource = Source("first.dwg", "one");
        var secondSource = Source("second.dwg", "two");
        await service.ApproveAsync(Request(firstSource));
        var second = await service.ApproveAsync(Request(secondSource));
        Assert.Equal("rev-002", second.Revision);
        var binding = Assert.Single(new SymbolArchiveRepository(_root).Load().Bindings);
        Assert.Equal(SymbolRevisionStatus.Superseded, binding.Revisions.Single(item => item.Revision == "rev-001").Status);
        Assert.Equal(SymbolRevisionStatus.Approved, binding.Revisions.Single(item => item.Revision == "rev-002").Status);
        Assert.True(File.Exists(new SymbolArchiveRepository(_root).ResolveArchivePath(binding.Revisions.Single(item => item.Revision == "rev-001").AssetPath)));
    }

    [Fact]
    public async Task ReapprovingSupersededSameHashReusesAssetAndMaintainsOneApproved()
    {
        var service = Service();
        var first = Source("first.dwg", "one");
        var second = Source("second.dwg", "two");
        await service.ApproveAsync(Request(first));
        await service.ApproveAsync(Request(second));
        var result = await service.ApproveAsync(Request(first));
        Assert.Equal(SymbolApprovalDisposition.ReapprovedExisting, result.Disposition);
        var binding = Assert.Single(new SymbolArchiveRepository(_root).Load().Bindings);
        Assert.Single(binding.Revisions.Where(item => item.Status == SymbolRevisionStatus.Approved));
        Assert.Equal("rev-001", binding.Revisions.Single(item => item.Status == SymbolRevisionStatus.Approved).Revision);
    }

    [Fact]
    public async Task SameAppearanceWithDifferentMappingCreatesExplicitNewRevision()
    {
        var source = Source("same.dwg", "unchanged appearance");
        var service = Service();
        var first = await service.ApproveAsync(Request(source));
        var second = await service.ApproveAsync(Request(source) with
        {
            PortBindings = [new() { EngineeringEndpointId = "PIN-A", ConnectionPointId = "TERM01" }]
        });
        Assert.Equal(SymbolApprovalDisposition.CreatedRevision, second.Disposition);
        Assert.Equal("rev-002", second.Revision);
        Assert.Equal(first.Sha256, second.Sha256);
        var revisions = Assert.Single(new SymbolArchiveRepository(_root).Load().Bindings).Revisions;
        Assert.Equal("P1", Assert.Single(revisions.Single(r => r.Revision == "rev-001").PortBindings).EngineeringEndpointId);
        Assert.Equal("PIN-A", Assert.Single(revisions.Single(r => r.Revision == "rev-002").PortBindings).EngineeringEndpointId);
        var duplicate = await service.ApproveAsync(Request(source) with
        {
            PortBindings = [new() { EngineeringEndpointId = "PIN-A", ConnectionPointId = "TERM01" }]
        });
        Assert.Equal(SymbolApprovalDisposition.ExactDuplicate, duplicate.Disposition);
        Assert.Equal("rev-002", duplicate.Revision);
    }

    [Fact]
    public async Task BindingOrderDoesNotCreateRevisionAndUnconfirmedChangeCannotWrite()
    {
        var source = Source("same.dwg", "same");
        var request = Request(source) with { PortBindings =
        [new() { EngineeringEndpointId = "P1", ConnectionPointId = "TERM01" },
         new() { EngineeringEndpointId = "PIN-A", ConnectionPointId = "TERM02" }] };
        var service = Service();
        await service.ApproveAsync(request);
        var manifest = Path.Combine(_root, SymbolArchiveRepository.FileName);
        var before = File.ReadAllBytes(manifest);
        var duplicate = await service.ApproveAsync(request with { PortBindings = request.PortBindings.Reverse().ToArray() });
        Assert.Equal(SymbolApprovalDisposition.ExactDuplicate, duplicate.Disposition);
        Assert.Equal(before, File.ReadAllBytes(manifest));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveAsync(request with
        {
            UserConfirmed = false,
            PortBindings = [new() { EngineeringEndpointId = "PIN-A", ConnectionPointId = "TERM03" }]
        }));
        Assert.Equal(before, File.ReadAllBytes(manifest));
        Assert.Single(Assert.Single(new SymbolArchiveRepository(_root).Load().Bindings).Revisions);
    }

    [Fact]
    public async Task ExplicitRepresentationsKeepIndependentApprovalAndResolution()
    {
        var source = Source("appearance.dwg", "same geometry, distinct confirmed use");
        var service = Service();
        var original = await service.ApproveAsync(Request(source));
        var coil = await service.ApproveAsync(Request(source) with { RepresentationId = "coil" });
        var contact = await service.ApproveAsync(Request(source) with { RepresentationId = "contact" });
        Assert.Equal("rev-001", coil.Revision);
        Assert.Equal("rev-001", contact.Revision);
        Assert.NotEqual(original.AssetPath, coil.AssetPath);
        Assert.NotEqual(coil.AssetPath, contact.AssetPath);
        await service.ApproveAsync(Request(Source("coil2.dwg", "new coil")) with { RepresentationId = "coil" });
        var repository = new SymbolArchiveRepository(_root);
        var bindings = repository.Load().Bindings;
        Assert.Equal("ci-symbol-archive.v2", repository.Load().SchemaVersion);
        Assert.Equal(3, bindings.Count);
        Assert.All(bindings, b => Assert.Single(b.Revisions, r => r.Status == SymbolRevisionStatus.Approved));
        var resolver = new SymbolResolver(repository, [Component()]);
        Assert.Equal(original.AssetPath, (await resolver.ResolveAsync("C1", SymbolRole.Schematic)).AssetPath);
        Assert.Equal("rev-002", (await resolver.ResolveAsync("C1", SymbolRole.Schematic, representationId: "coil")).Revision);
        Assert.Equal(contact.AssetPath, (await resolver.ResolveAsync("C1", SymbolRole.Schematic, representationId: "contact")).AssetPath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync("C1", SymbolRole.Schematic, representationId: "unknown"));
        Assert.True(File.Exists(repository.ResolveArchivePath(original.AssetPath)));
    }

    [Theory]
    [InlineData("../coil")]
    [InlineData("coil/contact")]
    [InlineData("")]
    [InlineData("Coil")]
    public async Task InvalidRepresentationIdentityCannotWrite(string representationId)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Service().ApproveAsync(
            Request(Source("bad.dwg", "unchanged")) with { RepresentationId = representationId }));
        Assert.False(File.Exists(Path.Combine(_root, SymbolArchiveRepository.FileName)));
    }

    [Fact]
    public async Task DrawingBridgeDoesNotMistakeVariantForDefault()
    {
        await Service().ApproveAsync(Request(Source("coil.dwg", "coil")) with { RepresentationId = "coil" });
        var repository = new SymbolArchiveRepository(_root);
        var bridge = new ComponentIntelligence.Electrical.Drawing.Cp3aDrawingAssetResolver(new SymbolResolver(repository, [Component()]), repository);
        Assert.Null(bridge.Resolve("C1", ComponentIntelligence.Electrical.Drawing.DrawingRepresentationRole.Schematic));
    }

    [Fact]
    public async Task HistoricalManifestWithoutRepresentationStillResolvesDefault()
    {
        var approved = await Service().ApproveAsync(Request(Source("old.dwg", "historical")));
        var repository = new SymbolArchiveRepository(_root);
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(repository.ArchivePath))!;
        foreach (var binding in json["bindings"]!.AsArray()) binding!.AsObject().Remove("representationId");
        File.WriteAllText(repository.ArchivePath, json.ToJsonString());
        var before = File.ReadAllBytes(repository.ArchivePath);
        Assert.Equal("default", Assert.Single(repository.Load().Bindings).RepresentationId);
        Assert.Equal("ci-symbol-archive.v1", repository.Load().SchemaVersion);
        Assert.Equal(approved.AssetPath, (await new SymbolResolver(repository, [Component()]).ResolveAsync("C1", SymbolRole.Schematic)).AssetPath);
        Assert.Equal(before, File.ReadAllBytes(repository.ArchivePath));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private SymbolArchiveApprovalService Service() => new(new SymbolArchiveRepository(_root), [Component()]);
    private static ComponentIR Component() => new()
    {
        Identity = new ComponentIrIdentity { ComponentId = "C1", Manufacturer = "MFR", Model = "MODEL" },
        Ports = [new ComponentPort { PortId = "P1", TopologyEndpointMode = "Connector" }],
        Pins = [new ComponentPin { PinId = "PIN-A", PortId = "P1", PinNumber = "1" }]
    };
    private static ApproveSymbolRequest Request(string source) => new()
    {
        SourcePath = source, ComponentId = "C1", Role = SymbolRole.Schematic,
        SourceType = SymbolSourceType.ApprovedCustom, UserConfirmed = true,
        PortBindings = [new SymbolPortBinding { EngineeringEndpointId = "P1", ConnectionPointId = "TERM01" }]
    };
    private string Source(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }
}
