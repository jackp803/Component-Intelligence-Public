using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Domain;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class ComponentPhysicalKnowledgeMapperTests
{
    [Fact]
    public void VerifiedDimensions_CreateWidthHeightDepthAndDinRailMounting()
    {
        var source = Component(
            new ComponentSpecification
            {
                Key = "dimensions",
                Name = "Dimensions",
                Section = "Physical Characteristics",
                Value = "18 × 81 × 65",
                Status = VerificationStatus.Verified,
                Evidence = [Evidence("18 x 81 x 65 mm")]
            },
            new ComponentSpecification
            {
                Key = "installation",
                Name = "Installation",
                Section = "Physical Characteristics",
                Value = "DIN-rail; wall mounting with optional kit",
                Status = VerificationStatus.Verified,
                Evidence = [Evidence("DIN-rail mounting; Wall mounting (with optional kit)")]
            });

        var footprint = ComponentPhysicalKnowledgeMapper.TryCreateFootprint(source);

        Assert.NotNull(footprint);
        Assert.Equal(18d, footprint!.WidthMm, 6);
        Assert.Equal(81d, footprint.HeightMm, 6);
        Assert.Equal(65d, footprint.DepthMm!.Value, 6);
        Assert.Equal(MountingType.DinRail, footprint.MountingType);
    }

    [Fact]
    public void InferredOrUnitlessDimensions_DoNotBecomePhysicalTruth()
    {
        var inferred = Component(new ComponentSpecification
        {
            Key = "dimensions",
            Name = "Dimensions",
            Value = "18 × 81 × 65 mm",
            Status = VerificationStatus.Inferred
        });
        var unitless = Component(new ComponentSpecification
        {
            Key = "dimensions",
            Name = "Dimensions",
            Value = "18 × 81 × 65",
            Status = VerificationStatus.Verified
        });

        Assert.Null(ComponentPhysicalKnowledgeMapper.TryCreateFootprint(inferred));
        Assert.Null(ComponentPhysicalKnowledgeMapper.TryCreateFootprint(unitless));
    }

    private static ComponentIR Component(params ComponentSpecification[] specifications) => new()
    {
        Identity = new ComponentIrIdentity
        {
            ComponentId = "test-component",
            Manufacturer = "MOXA",
            Model = "EDS-2005-EL"
        },
        Specifications = specifications
    };

    private static Evidence Evidence(string raw) => new()
    {
        SourceType = ComponentSourceType.ManufacturerProductPage,
        SourceUrl = new Uri("https://example.com/product"),
        ExtractionMethod = ExtractionMethod.Html,
        RawValue = raw,
        RetrievedAt = DateTimeOffset.UtcNow,
        VerificationStatus = VerificationStatus.Verified
    };
}
