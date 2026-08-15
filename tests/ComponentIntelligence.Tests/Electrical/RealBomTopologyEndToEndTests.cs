using ClosedXML.Excel;
using ComponentIntelligence.Bom;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class RealBomTopologyEndToEndTests
{
    [Fact]
    public async Task SyntheticPublicBom_ImportsAndProjectsAllInstalledItemsIntoTopologyWithoutDuplication()
    {
        var sourceRows = Enumerable.Range(1, 31)
            .Select(index => new PublicBomRow(
                "PUBLIC-DEMO",
                $"PUBLIC-PART-{index:000}",
                index <= 27 ? "2" : "1",
                index <= 27 ? "2" : "1",
                "Synthetic public topology test row"))
            .Append(new PublicBomRow(
                "PUBLIC-DEMO",
                "PUBLIC-PART-UNKNOWN",
                "TBD",
                "TBD",
                "Synthetic public row with unknown quantity"))
            .ToArray();
        var filePath = Path.Combine(Path.GetTempPath(), $"component-intelligence-public-topology-{Guid.NewGuid():N}.xlsx");

        try
        {
            WriteWorkbook(filePath, sourceRows);
            var import = await new BomImporter().ImportAsync(filePath);

            Assert.Empty(import.Errors);
            Assert.Equal(32, import.Rows.Count);
            Assert.Equal(58, import.Rows.Sum(row => row.UsedQuantity.GetValueOrDefault()));
            Assert.Equal(1, import.Rows.Count(row => row.UsedQuantity is null));

            var unknown = Assert.Single(import.Rows, row =>
                string.Equals(row.ModelOrPartNumber, "PUBLIC-PART-UNKNOWN", StringComparison.OrdinalIgnoreCase));
            Assert.Null(unknown.UsedQuantity);
            Assert.Null(unknown.TotalQuantity);
            Assert.Equal("TBD", unknown.RawRow?[BomHeaderMapper.UsedQuantity]);
            Assert.Equal(BomImportStatus.Imported, unknown.ImportStatus);

            var project = new ElectricalProject { ProjectId = "PUBLIC-BOM-E2E" };
            var synchronizer = new BomTopologySynchronizer();
            var first = await synchronizer.SynchronizeAsync(
                project,
                import.Rows,
                (_, _, _) => Task.FromResult<ComponentIR?>(null));

            Assert.Equal(32, first.BomRowCount);
            Assert.Equal(59, first.AddedInstances);
            Assert.Equal(0, first.RichInstances);
            Assert.Equal(59, first.PlaceholderInstances);
            Assert.Equal(1, first.UnknownQuantityRows);
            Assert.Equal(0, first.SkippedSpareOnlyRows);
            Assert.Equal(59, project.Components.Count);
            Assert.Equal(59, project.Components.Select(component => component.ComponentInstanceId)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Single(project.Components, component =>
                string.Equals(component.TypeKey, "BOM_ITEM_QTY_UNKNOWN", StringComparison.OrdinalIgnoreCase) &&
                component.DisplayName?.Contains("Qty ?", StringComparison.Ordinal) == true);

            var second = await synchronizer.SynchronizeAsync(
                project,
                import.Rows,
                (_, _, _) => Task.FromResult<ComponentIR?>(null));

            Assert.Equal(0, second.AddedInstances);
            Assert.Equal(59, project.Components.Count);

            var projection = new TopologyProjection();
            projection.EnsurePlacements(project);
            var graph = projection.Build(project);

            Assert.Equal(59, graph.Nodes.Count);
            Assert.Empty(graph.Edges);
            Assert.Equal(59, project.TopologyPlacements.Count);
            Assert.Contains(graph.Nodes, node => node.Label.Contains("Qty ?", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    private static void WriteWorkbook(string filePath, IReadOnlyList<PublicBomRow> rows)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("BOM");
        var headers = new[]
        {
            BomHeaderMapper.Manufacturer,
            BomHeaderMapper.ModelOrPartNumber,
            BomHeaderMapper.UsedQuantity,
            BomHeaderMapper.TotalQuantity,
            BomHeaderMapper.Notes
        };

        for (var column = 0; column < headers.Length; column++)
            worksheet.Cell(1, column + 1).Value = headers[column];

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var targetRow = index + 2;
            worksheet.Cell(targetRow, 1).Value = row.Manufacturer;
            worksheet.Cell(targetRow, 2).Value = row.Model;
            worksheet.Cell(targetRow, 3).Value = row.UsedQuantity;
            worksheet.Cell(targetRow, 4).Value = row.TotalQuantity;
            worksheet.Cell(targetRow, 5).Value = row.Notes;
        }

        workbook.SaveAs(filePath);
    }

    private sealed record PublicBomRow(
        string Manufacturer,
        string Model,
        string UsedQuantity,
        string TotalQuantity,
        string Notes);
}
