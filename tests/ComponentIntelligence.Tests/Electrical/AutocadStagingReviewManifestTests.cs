using System.Text.Json;
using ComponentIntelligence.Desktop;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class AutocadStagingReviewManifestTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ci-acade-manifest-{Guid.NewGuid():N}");

    [Fact]
    public void Marker_TargetsTheEngineeringWriterManifest() =>
        Assert.Equal("CM_LRDU_STAGING_WRITER_MANIFEST=", AutocadStagingReviewManifest.Marker);

    [Fact]
    public void Load_AcceptsIsolatedWriterEvidence()
    {
        Directory.CreateDirectory(_root);
        var wdp = Touch("review.wdp");
        var dwg = Touch("review01.dwg");
        var pdf = Touch("review01.pdf");
        var manifest = WriteManifest(wdp, [dwg], [pdf], true, "NO");

        var result = AutocadStagingReviewManifest.Load(manifest, _root);

        Assert.Equal(wdp, result.ProjectPath);
        Assert.Equal([dwg], result.DrawingPaths);
        Assert.Equal([pdf], result.PdfPaths);
        Assert.Equal("NO", result.FormalDwgModified);
    }

    [Theory]
    [InlineData(false, "NO")]
    [InlineData(true, "YES")]
    public void Load_RejectsMissingSafetyEvidence(bool writerExecuted, string formalDwgModified)
    {
        Directory.CreateDirectory(_root);
        var manifest = WriteManifest(Touch("review.wdp"), [Touch("review01.dwg")], [Touch("review01.pdf")], writerExecuted, formalDwgModified);

        Assert.Throws<InvalidDataException>(() => AutocadStagingReviewManifest.Load(manifest, _root));
    }

    [Fact]
    public void Load_RejectsMissingArtifacts()
    {
        Directory.CreateDirectory(_root);
        var manifest = WriteManifest(Path.Combine(_root, "missing.wdp"), [Touch("review01.dwg")], [Touch("review01.pdf")], true, "NO");

        Assert.Throws<FileNotFoundException>(() => AutocadStagingReviewManifest.Load(manifest, _root));
    }

    [Fact]
    public void Load_RejectsArtifactsOutsideTheRunRoot()
    {
        Directory.CreateDirectory(_root);
        var outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.wdp");
        File.WriteAllText(outside, string.Empty);
        try
        {
            var manifest = WriteManifest(outside, [Touch("review01.dwg")], [Touch("review01.pdf")], true, "NO");

            Assert.Throws<InvalidOperationException>(() => AutocadStagingReviewManifest.Load(manifest, _root));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private string Touch(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private string WriteManifest(string projectPath, IReadOnlyList<string> drawings, IReadOnlyList<string> pdfs, bool writerExecuted, string formalDwgModified)
    {
        var path = Path.Combine(_root, "staging-review-manifest.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            projectPath,
            drawingPaths = drawings,
            pdfPaths = pdfs,
            writerExecuted,
            formalDwgModified
        }));
        return path;
    }
}
