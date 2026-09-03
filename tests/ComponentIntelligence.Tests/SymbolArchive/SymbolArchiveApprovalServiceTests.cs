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
