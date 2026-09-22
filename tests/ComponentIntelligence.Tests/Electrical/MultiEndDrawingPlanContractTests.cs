using ComponentIntelligence.Electrical.Drawing;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class MultiEndDrawingPlanContractTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void BranchTemplateFamiliesSurvivePlanHashRoundTrip(int count)
    {
        var plan = new DrawingPlanDocument
        {
            ProjectId = "P", SourcePlanningInputHash = new string('A', 64),
            SourcePagePlanHash = new string('B', 64),
            CableDetailTemplates = [new()
            {
                TemplateId = "T", EndAInterfaceLayoutFamily = "RJ45", EndBInterfaceLayoutFamily = "M12",
                BranchInterfaceLayoutFamilies = Enumerable.Repeat("M12", count).ToArray()
            }]
        };
        var json = DrawingPlanJson.Serialize(plan);
        var restored = DrawingPlanJson.Deserialize(json);
        Assert.Equal(count, restored.CableDetailTemplates[0].BranchInterfaceLayoutFamilies!.Count);
        Assert.Equal(json, DrawingPlanJson.Serialize(restored));
    }
}
