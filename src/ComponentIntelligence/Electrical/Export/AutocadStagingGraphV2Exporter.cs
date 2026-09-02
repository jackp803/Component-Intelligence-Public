using System.Text;
using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Export;

/// <summary>
/// Prepares and writes an inspectable v2 contract artifact. It does not invoke planning, runners,
/// AutoCAD, persistence, or project/drawing writers.
/// </summary>
public sealed class AutocadStagingGraphV2Exporter
{
    public const string ArtifactFileName = "lrdu-staging-route.v2.json";

    public AutocadStagingGraphV2ExportResult PrepareAndWrite(
        ElectricalProject project,
        IEnumerable<AutocadConnectionPointBinding> auditedBindings,
        AutocadEngineeringDrawingEvidence? drawingEvidence,
        string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(auditedBindings);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var preparation = new AutocadStagingGraphV2Builder().Prepare(
            project,
            auditedBindings,
            drawingEvidence);
        if (preparation.Graph is null)
            return new AutocadStagingGraphV2ExportResult { Preparation = preparation };

        AutocadStagingGraphV2Contract.EnsureSupportedSchema(preparation.Graph.SchemaVersion);
        var json = JsonSerializer.Serialize(preparation.Graph)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        Directory.CreateDirectory(outputDirectory);
        var graphPath = Path.Combine(outputDirectory, ArtifactFileName);
        File.WriteAllText(graphPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return new AutocadStagingGraphV2ExportResult
        {
            Preparation = preparation,
            GraphPath = graphPath
        };
    }
}

public sealed record AutocadStagingGraphV2ExportResult
{
    public required AutocadStagingGraphV2PreparationResult Preparation { get; init; }
    public string? GraphPath { get; init; }
}
