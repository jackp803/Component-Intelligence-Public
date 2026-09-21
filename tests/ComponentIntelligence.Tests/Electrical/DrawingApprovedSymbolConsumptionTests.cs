using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Drawing;
using ComponentIntelligence.SymbolArchive;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class DrawingApprovedSymbolConsumptionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "approved-drawing-" + Guid.NewGuid().ToString("N"));
    public DrawingApprovedSymbolConsumptionTests() => Directory.CreateDirectory(_root);
    private async Task<SymbolArchiveRepository> Archive(bool large = false)
    {
        var repository = new SymbolArchiveRepository(_root);
        var source = Path.Combine(_root, "input.dwg"); await File.WriteAllTextAsync(source, large ? new string('x', 2_000_000) : "test symbol");
        await new SymbolArchiveApprovalService(repository, [ComponentSourceIdentityRestorerTests.Catalog()]).ApproveAsync(new ApproveSymbolRequest
        {
            SourcePath = source, ComponentId = "DEF", Role = SymbolRole.Schematic, SourceType = SymbolSourceType.ApprovedCustom, UserConfirmed = true,
            PortBindings = [new SymbolPortBinding { EngineeringEndpointId = "P", ConnectionPointId = "PORT-CONTACT" }, new SymbolPortBinding { EngineeringEndpointId = "SOURCE-A", ConnectionPointId = "X1TERM01" }]
        });
        return repository;
    }
    private static ElectricalProject Project()
        => new() { ProjectId = "P", Components = [new ComponentProjectBridge().CreateInstance(ComponentSourceIdentityRestorerTests.Catalog(), "I")] };

    [Fact]
    public async Task SynchronousPlanning_DoesNotRequireCallingUiContextToPump()
    {
        await Archive(large: true);
        var completed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var context = new RecordingContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                DrawingPlanningRuntimeFactory.Create(_root, [ComponentSourceIdentityRestorerTests.Catalog()]).Build(Project());
                completed.SetResult(context.Posts);
            }
            catch (Exception ex) { completed.SetException(ex); }
        }) { IsBackground = true };
        thread.Start();
        Assert.Equal(0, await completed.Task.WaitAsync(TimeSpan.FromSeconds(20)));
    }

    private sealed class RecordingContext : SynchronizationContext
    {
        public int Posts;
        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref Posts);
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }
    }

    [Fact]
    public async Task ConfiguredFactory_SelectsExact_ConfinedAbsolutePath_AndBridgesPortAndPin()
    {
        var repository = await Archive(); var project = Project();
        var before = System.Text.Json.JsonSerializer.Serialize(project);
        var input = DrawingPlanningRuntimeFactory.Create(_root, [ComponentSourceIdentityRestorerTests.Catalog()]).Build(project);
        var rep = Assert.Single(input.Representations);
        Assert.Equal(DrawingRepresentationFamily.ArchivedExact, rep.Family);
        Assert.Equal("ApprovedCustom", rep.SourceType); Assert.Equal("rev-001", rep.AssetRevision);
        Assert.True(Path.IsPathFullyQualified(rep.AssetPath!));
        Assert.Equal(repository.ResolveArchivePath(repository.Load().Bindings[0].Revisions[0].AssetPath), rep.AssetPath);
        Assert.Equal(project.Components[0].Ports[0].PortId, rep.PortBindings.Single(b => b.ConnectionPointId == "PORT-CONTACT").EngineeringEndpointId);
        Assert.Equal(project.Components[0].Ports[0].Pins[0].PinId, rep.PortBindings.Single(b => b.ConnectionPointId == "X1TERM01").EngineeringEndpointId);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(project));
    }
    [Fact]
    public void NoApprovedAsset_UsesExistingFunctionalGeneric()
        => Assert.Equal(DrawingRepresentationFamily.FunctionalGeneric, DrawingPlanningRuntimeFactory.Create(_root, [ComponentSourceIdentityRestorerTests.Catalog()]).Build(Project()).Representations[0].Family);

    [Theory]
    [InlineData("malformed")]
    [InlineData("duplicate")]
    [InlineData("traversal")]
    [InlineData("absolute")]
    [InlineData("missing")]
    [InlineData("sha")]
    public async Task CorruptApprovedAuthority_FailsClosed(string mode)
    {
        var repo = await Archive(); var revision = repo.Load().Bindings[0].Revisions[0];
        var asset = repo.ResolveArchivePath(revision.AssetPath);
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(repo.ArchivePath))!;
        var revisions = json["bindings"]![0]!["revisions"]!.AsArray();
        if (mode == "malformed") File.WriteAllText(repo.ArchivePath, "{");
        if (mode == "duplicate") { revisions.Add(revisions[0]!.DeepClone()); File.WriteAllText(repo.ArchivePath, json.ToJsonString()); }
        if (mode is "traversal" or "absolute") { revisions[0]!["assetPath"] = mode == "traversal" ? "../outside.dwg" : asset; File.WriteAllText(repo.ArchivePath, json.ToJsonString()); }
        if (mode == "missing") File.Delete(asset);
        if (mode == "sha") File.AppendAllText(asset, "tamper");
        Assert.ThrowsAny<Exception>(() => DrawingPlanningRuntimeFactory.Create(_root, [ComponentSourceIdentityRestorerTests.Catalog()]).Build(Project()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrDuplicateBridge_Blocks_NoExactFallbackClaim(bool duplicate)
    {
        await Archive(); var project = Project();
        if (duplicate) project.Components[0].Ports[0].Pins[1].SourcePinId = "SOURCE-A";
        else project.Components[0].Ports[0].Pins.RemoveAt(0);
        var input = DrawingPlanningRuntimeFactory.Create(_root, [ComponentSourceIdentityRestorerTests.Catalog()]).Build(project);
        Assert.Contains(input.Issues, i => i.Severity == DrawingPlanningIssueSeverity.Blocker);
        Assert.NotEqual(DrawingRepresentationFamily.ArchivedExact, input.Representations[0].Family);
        Assert.Null(input.Representations[0].AssetPath);
    }
    public void Dispose() => Directory.Delete(_root, true);
}
