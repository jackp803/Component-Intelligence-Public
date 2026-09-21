using ComponentIntelligence.Electrical.Drawing;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class DrawingProjectMetadataTests
{
    [Fact]
    public void Store_RoundTripsMetadataByProjectIdWithoutCustomerInference()
    {
        var root = Path.Combine(Path.GetTempPath(), $"drawing-metadata-{Guid.NewGuid():N}");
        try
        {
            var store = new DrawingProjectMetadataStore(Path.Combine(root, "metadata.json"));
            var metadata = new DrawingProjectMetadata
            {
                ProjectName = "PHASE2A PROJECT",
                EquipmentPartNumber = "EQ-P2A-001",
                Checker = "CHECK-P2A",
                Drafter = "DRAW-P2A",
                Revision = "R-P2A",
                Pages =
                [
                    new DrawingPageMetadata { PageId = "P-01", DrawingTitle = "POWER-P2A", DrawingDocumentNumber = "DOC-P2A-01" }
                ]
            };

            store.Save("PROJECT-A", metadata);

            var loaded = new DrawingProjectMetadataStore(Path.Combine(root, "metadata.json")).Load("PROJECT-A");
            Assert.Equal(metadata.ProjectName, loaded.ProjectName);
            Assert.Equal(metadata.EquipmentPartNumber, loaded.EquipmentPartNumber);
            Assert.Equal(metadata.Pages, loaded.Pages);
            Assert.Equal(DrawingProjectMetadata.Empty, store.Load("PROJECT-B"));
            Assert.DoesNotContain("customer", File.ReadAllText(Path.Combine(root, "metadata.json")), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void PlanningInput_MetadataRoundTripsAndParticipatesInHash()
    {
        var first = DrawingPlanningContractsTests.TestInput() with
        {
            ProjectMetadata = new DrawingProjectMetadata { ProjectName = "PROJECT-A", Revision = "A" }
        };
        var second = first with { ProjectMetadata = first.ProjectMetadata with { Revision = "B" } };

        var roundTrip = DrawingPlanningJson.Deserialize(DrawingPlanningJson.Serialize(first));

        Assert.Equal(first.ProjectMetadata.ProjectName, roundTrip.ProjectMetadata.ProjectName);
        Assert.Equal(first.ProjectMetadata.Revision, roundTrip.ProjectMetadata.Revision);
        Assert.Equal(first.ProjectMetadata.Pages, roundTrip.ProjectMetadata.Pages);
        Assert.NotEqual(
            DrawingPlanningJson.Deserialize(DrawingPlanningJson.Serialize(first)).PlanningInputHash,
            DrawingPlanningJson.Deserialize(DrawingPlanningJson.Serialize(second)).PlanningInputHash);
    }
}
