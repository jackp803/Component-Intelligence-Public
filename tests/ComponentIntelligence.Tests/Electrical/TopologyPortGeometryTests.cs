using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class TopologyPortGeometryTests
{
    [Theory]
    [InlineData("Input", TopologyScreenSide.Left)]
    [InlineData("OUTPUT", TopologyScreenSide.Right)]
    [InlineData("Bidirectional", TopologyScreenSide.Right)]
    public void DeclaredDirection_DeterminesTopologyScreenSide(string direction, TopologyScreenSide expected)
    {
        var port = new ComponentPort { PortId = "device:port", Name = "PORT" };
        port.Capabilities.Add($"DIRECTION:{direction}");

        Assert.Equal(expected, TopologyPortGeometry.DetermineScreenSide(port));
    }

    [Theory]
    [InlineData("Input Port", "Passive", TopologyScreenSide.Left)]
    [InlineData("Output Port", "Passive", TopologyScreenSide.Right)]
    [InlineData("Power Input", "Output", TopologyScreenSide.Left)]
    [InlineData("Power Output", "Input", TopologyScreenSide.Right)]
    public void DeclaredPortRole_IsPrimaryTopologyVisualSemantic(
        string role,
        string direction,
        TopologyScreenSide expected)
    {
        var port = new ComponentPort { PortId = "device:port", Name = "PORT" };
        port.Capabilities.Add($"ROLE:{role}");
        port.Capabilities.Add($"DIRECTION:{direction}");

        Assert.Equal(expected, TopologyPortGeometry.DetermineScreenSide(port));
    }

    [Fact]
    public void F03_20_PassiveInputAndOutputRoles_RenderOnOppositeSides()
    {
        var input = new ComponentPort { PortId = "OMRON_F03-20_INPUT", Name = "INPUT" };
        input.Capabilities.Add("ROLE:Input Port");
        input.Capabilities.Add("DIRECTION:Passive");

        var output = new ComponentPort { PortId = "OMRON_F03-20_OUTPUT", Name = "OUTPUT" };
        output.Capabilities.Add("ROLE:Output Port");
        output.Capabilities.Add("DIRECTION:Passive");

        Assert.Equal(TopologyScreenSide.Left, TopologyPortGeometry.DetermineScreenSide(input));
        Assert.Equal(TopologyScreenSide.Right, TopologyPortGeometry.DetermineScreenSide(output));
    }

    [Fact]
    public void AmbiguousInputOutputRole_FallsBackToDeclaredDirection()
    {
        var port = new ComponentPort { PortId = "device:mixed", Name = "MIXED" };
        port.Capabilities.Add("ROLE:Input/Output Interface");
        port.Capabilities.Add("DIRECTION:Input");

        Assert.Equal(TopologyScreenSide.Left, TopologyPortGeometry.DetermineScreenSide(port));
    }

    [Fact]
    public void PassivePortWithoutDirectionalRole_RemainsNeutralRight()
    {
        var port = new ComponentPort { PortId = "device:passive", Name = "PASSIVE" };
        port.Capabilities.Add("ROLE:Terminal Interface");
        port.Capabilities.Add("DIRECTION:Passive");

        Assert.Equal(TopologyScreenSide.Right, TopologyPortGeometry.DetermineScreenSide(port));
    }

    [Theory]
    [InlineData(0,   240, 240,  1,  0)]
    [InlineData(90,  170, 310,  0,  1)]
    [InlineData(180, 100, 240, -1,  0)]
    [InlineData(270, 170, 170,  0, -1)]
    public void SinglePort_FollowsComponentRightEdgeRotation(
        int degrees,
        double expectedX,
        double expectedY,
        double expectedOutwardX,
        double expectedOutwardY)
    {
        var placement = Placement(degrees);

        var anchor = TopologyPortGeometry.Calculate(placement, 0, 1);

        Assert.Equal(expectedX, anchor.X, 6);
        Assert.Equal(expectedY, anchor.Y, 6);
        Assert.Equal(expectedOutwardX, anchor.OutwardX, 6);
        Assert.Equal(expectedOutwardY, anchor.OutwardY, 6);
    }

    [Fact]
    public void MultiplePorts_PreserveSpacingWhenRotated()
    {
        var placement = new TopologyPlacement
        {
            ObjectId = "switch-1",
            ObjectKind = "COMPONENT",
            X = 20,
            Y = 40,
            Width = 140,
            Height = 76,
            RotationDegrees = 90
        };

        var first = TopologyPortGeometry.Calculate(placement, 0, 6);
        var last = TopologyPortGeometry.Calculate(placement, 5, 6);

        Assert.Equal(first.Y, last.Y, 6);
        Assert.True(first.X > last.X);
        Assert.Equal(0d, first.OutwardX, 6);
        Assert.Equal(1d, first.OutwardY, 6);
    }

    [Theory]
    [InlineData(0,   100, 240)]
    [InlineData(90,  130, 240)]
    [InlineData(180, 100, 240)]
    [InlineData(270, 130, 240)]
    public void ScreenLeftAnchor_RemainsOnVisibleLeftEdgeAfterRotation(int degrees, double expectedX, double expectedY)
    {
        var anchor = TopologyPortGeometry.CalculateScreenSide(
            Placement(degrees),
            TopologyScreenSide.Left,
            0,
            1);

        Assert.Equal(expectedX, anchor.X, 6);
        Assert.Equal(expectedY, anchor.Y, 6);
        Assert.Equal(-1d, anchor.OutwardX, 6);
        Assert.Equal(0d, anchor.OutwardY, 6);
    }

    [Theory]
    [InlineData(0,   240, 240)]
    [InlineData(90,  210, 240)]
    [InlineData(180, 240, 240)]
    [InlineData(270, 210, 240)]
    public void ScreenRightAnchor_RemainsOnVisibleRightEdgeAfterRotation(int degrees, double expectedX, double expectedY)
    {
        var anchor = TopologyPortGeometry.CalculateScreenSide(
            Placement(degrees),
            TopologyScreenSide.Right,
            0,
            1);

        Assert.Equal(expectedX, anchor.X, 6);
        Assert.Equal(expectedY, anchor.Y, 6);
        Assert.Equal(1d, anchor.OutwardX, 6);
        Assert.Equal(0d, anchor.OutwardY, 6);
    }

    [Fact]
    public void ScreenSidePorts_AreDistributedAlongRotatedEdgeWithoutLeavingPerimeter()
    {
        var placement = Placement(90);

        var first = TopologyPortGeometry.CalculateScreenSide(placement, TopologyScreenSide.Right, 0, 5);
        var last = TopologyPortGeometry.CalculateScreenSide(placement, TopologyScreenSide.Right, 4, 5);

        Assert.Equal(210d, first.X, 6);
        Assert.Equal(210d, last.X, 6);
        Assert.True(first.Y < last.Y);
        Assert.All(new[] { first, last }, anchor =>
        {
            Assert.Equal(1d, anchor.OutwardX, 6);
            Assert.Equal(0d, anchor.OutwardY, 6);
        });
    }

    private static TopologyPlacement Placement(int degrees) => new()
    {
        ObjectId = "device-1",
        ObjectKind = "COMPONENT",
        X = 100,
        Y = 200,
        Width = 140,
        Height = 80,
        RotationDegrees = degrees
    };
}
