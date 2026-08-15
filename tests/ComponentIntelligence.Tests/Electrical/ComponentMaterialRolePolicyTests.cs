using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bridging;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class ComponentMaterialRolePolicyTests
{
    [Theory]
    [InlineData("Cable")]
    [InlineData("Cable Assembly")]
    [InlineData("Wire")]
    [InlineData("Wire Harness")]
    [InlineData("Cordset")]
    [InlineData("成品線組")]
    public void Classify_KnownConnectionMaterial_IsDeferred(string category)
    {
        var component = NewComponent(category);
        Assert.Equal(BomTopologyDisposition.DeferredConnectionMaterial, ComponentMaterialRolePolicy.Classify(component));
    }

    [Theory]
    [InlineData("Sensor")]
    [InlineData("PLC")]
    [InlineData("Wireless Sensor")]
    [InlineData("Power Supply")]
    [InlineData(null)]
    public void Classify_DeviceOrUnknown_RemainsTopologyNode(string? category)
    {
        var component = NewComponent(category);
        Assert.Equal(BomTopologyDisposition.TopologyNode, ComponentMaterialRolePolicy.Classify(component));
    }

    [Fact]
    public void Classify_MaterialRoleSpecification_CanRouteWhenCategoryIsGeneric()
    {
        var component = NewComponent("Accessory") with
        {
            Specifications =
            [
                new ComponentSpecification
                {
                    Key = "material_role",
                    Name = "Material Role",
                    Value = "Cable Assembly"
                }
            ]
        };

        Assert.Equal(BomTopologyDisposition.DeferredConnectionMaterial, ComponentMaterialRolePolicy.Classify(component));
    }

    private static ComponentIR NewComponent(string? category) => new()
    {
        Identity = new ComponentIrIdentity
        {
            ComponentId = "TEST",
            Manufacturer = "Vendor",
            Model = "Part-1"
        },
        Classification = new ComponentClassification
        {
            Category = category
        }
    };
}
