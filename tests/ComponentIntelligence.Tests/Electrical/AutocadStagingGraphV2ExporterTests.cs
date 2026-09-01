using System.Security.Cryptography;
using System.Text.Json;
using ComponentIntelligence.Electrical.Export;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class AutocadStagingGraphV2ExporterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ci-week1-v2-export-{Guid.NewGuid():N}");

    [Fact]
    public void PrepareAndWrite_ValidPreparation_WritesOnlyPinnedV2Artifact()
    {
        var project = Week1FirstSliceV2Fixture.CreateProject();
        var projectBefore = JsonSerializer.Serialize(project);
        var outputDirectory = Path.Combine(_root, "valid");

        var result = new AutocadStagingGraphV2Exporter().PrepareAndWrite(
            project,
            Week1FirstSliceV2Fixture.AuditedBindings(),
            Week1FirstSliceV2Fixture.DrawingEvidence(),
            outputDirectory);

        var graph = Assert.IsType<AutocadStagingGraphV2Contract>(result.Preparation.Graph);
        Assert.Equal("lrdu-staging-route.v2", graph.SchemaVersion);
        Assert.Equal(Week1FirstSliceV2Fixture.ProjectId, graph.ProjectId);
        var graphPath = Assert.IsType<string>(result.GraphPath);
        Assert.Equal(Path.Combine(outputDirectory, "lrdu-staging-route.v2.json"), graphPath);
        Assert.Equal(graphPath, Assert.Single(Directory.GetFiles(outputDirectory, "*", SearchOption.AllDirectories)));
        var bytes = File.ReadAllBytes(graphPath);
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.DoesNotContain((byte)'\r', bytes);
        using var document = JsonDocument.Parse(bytes);
        Assert.Equal("lrdu-staging-route.v2", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(Week1FirstSliceV2Fixture.ProjectId, document.RootElement.GetProperty("projectId").GetString());
        Assert.Equal(projectBefore, JsonSerializer.Serialize(project));
        Assert.DoesNotContain(Directory.GetFiles(outputDirectory, "*", SearchOption.AllDirectories), path =>
            new[] { ".wdp", ".dwg", ".dwt", ".scr", ".lsp", ".log" }.Contains(
                Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void PrepareAndWrite_PreflightHardError_WritesNothing()
    {
        var outputDirectory = Path.Combine(_root, "blocked");

        var result = new AutocadStagingGraphV2Exporter().PrepareAndWrite(
            Week1FirstSliceV2Fixture.CreateProject(),
            Week1FirstSliceV2Fixture.AuditedBindings().Skip(1),
            Week1FirstSliceV2Fixture.DrawingEvidence(),
            outputDirectory);

        Assert.Null(result.Preparation.Graph);
        Assert.Null(result.GraphPath);
        Assert.False(Directory.Exists(outputDirectory));
    }

    [Fact]
    public void PrepareAndWrite_EquivalentInputs_ProduceByteIdenticalJson()
    {
        var first = new AutocadStagingGraphV2Exporter().PrepareAndWrite(
            Week1FirstSliceV2Fixture.CreateProject(),
            Week1FirstSliceV2Fixture.AuditedBindings(),
            Week1FirstSliceV2Fixture.DrawingEvidence(),
            Path.Combine(_root, "first"));
        var second = new AutocadStagingGraphV2Exporter().PrepareAndWrite(
            Week1FirstSliceV2Fixture.CreateProject(reverseInputOrder: true),
            Week1FirstSliceV2Fixture.AuditedBindings(reverseInputOrder: true),
            Week1FirstSliceV2Fixture.DrawingEvidence(reverseInputOrder: true),
            Path.Combine(_root, "second"));

        var firstPath = Assert.IsType<string>(first.GraphPath);
        var secondPath = Assert.IsType<string>(second.GraphPath);
        Assert.Equal(
            SHA256.HashData(File.ReadAllBytes(firstPath)),
            SHA256.HashData(File.ReadAllBytes(secondPath)));
    }

    [Fact]
    public void PrepareAndWrite_UnresolvedPlannerPolicy_SerializesBlockingEvidenceWithoutInventingValues()
    {
        var result = new AutocadStagingGraphV2Exporter().PrepareAndWrite(
            Week1FirstSliceV2Fixture.CreateProject(),
            Week1FirstSliceV2Fixture.AuditedBindings(),
            Week1FirstSliceV2Fixture.DrawingEvidence(),
            Path.Combine(_root, "policy"));

        var graph = Assert.IsType<AutocadStagingGraphV2Contract>(result.Preparation.Graph);
        Assert.Equal("Missing", graph.WireLayerPolicy.ApprovalStatus);
        Assert.Empty(graph.WireLayerPolicy.PolicyId);
        Assert.Empty(graph.WireLayerPolicy.SegmentLayers);
        Assert.Empty(graph.PageIntents);
        Assert.Empty(graph.CrossPageContinuations);

        using var document = JsonDocument.Parse(File.ReadAllBytes(Assert.IsType<string>(result.GraphPath)));
        var root = document.RootElement;
        Assert.Equal("Missing", root.GetProperty("wireLayerPolicy").GetProperty("approvalStatus").GetString());
        Assert.Equal(string.Empty, root.GetProperty("wireLayerPolicy").GetProperty("policyId").GetString());
        Assert.Empty(root.GetProperty("wireLayerPolicy").GetProperty("segmentLayers").EnumerateArray());
        Assert.Empty(root.GetProperty("pageIntents").EnumerateArray());
        Assert.Empty(root.GetProperty("crossPageContinuations").EnumerateArray());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
