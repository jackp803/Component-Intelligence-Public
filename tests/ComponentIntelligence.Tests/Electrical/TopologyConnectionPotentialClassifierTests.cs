using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class TopologyConnectionPotentialClassifierTests
{
    [Theory]
    [InlineData("DC OUTPUT +V", TopologyPotentialClass.PositiveDc)]
    [InlineData("DC OUTPUT -V", TopologyPotentialClass.NegativeOrReturnDc)]
    [InlineData("0V", TopologyPotentialClass.NegativeOrReturnDc)]
    [InlineData("FG", TopologyPotentialClass.ProtectiveOrFunctionalEarth)]
    public void PinEngineeringLabelClassifiesCanvasPotential(
        string pinName,
        TopologyPotentialClass expected)
    {
        var project = ProjectWithConnection(pinName);

        var actual = TopologyConnectionPotentialClassifier.Classify(project, project.Connections.Single());

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void StructuredPinPolarityTakesPriorityOverGenericPowerLayer()
    {
        var project = ProjectWithConnection("POWER");
        project.Components[0].Ports[0].Pins[0].Power = new PowerCapability
        {
            Polarity = Polarity.Negative,
            Voltage = new VoltageSpecification { Type = VoltageType.Dc, NominalVoltage = 24 }
        };

        var actual = TopologyConnectionPotentialClassifier.Classify(project, project.Connections.Single());

        Assert.Equal(TopologyPotentialClass.NegativeOrReturnDc, actual);
    }

    [Fact]
    public void ConflictingEndpointPolaritiesRemainUnknownInsteadOfMisleadingColour()
    {
        var project = ProjectWithConnection("+V");
        project.Components[0].Ports[0].Pins[0].Power = new PowerCapability { Polarity = Polarity.Positive };
        project.Components[1].Ports[0].Pins[0].Power = new PowerCapability { Polarity = Polarity.Negative };

        var actual = TopologyConnectionPotentialClassifier.Classify(project, project.Connections.Single());

        Assert.Equal(TopologyPotentialClass.Unknown, actual);
    }

    private static ElectricalProject ProjectWithConnection(string firstPinName)
    {
        var project = new ElectricalProject { ProjectId = "potential-style" };
        project.Components.Add(Component("A", "A:PIN", firstPinName));
        project.Components.Add(Component("B", "B:PIN", firstPinName));
        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = "C1",
            FromEndpointId = "A:PIN",
            ToEndpointId = "B:PIN",
            Kind = ConnectionKind.Wire
        });
        return project;
    }

    private static ComponentInstance Component(string id, string pinId, string pinName) => new()
    {
        ComponentInstanceId = id,
        ComponentDefinitionId = id,
        TypeKey = "TEST",
        Ports =
        {
            new ComponentPort
            {
                PortId = id + ":PORT",
                Name = "POWER",
                Pins =
                {
                    new ComponentPin
                    {
                        PinId = pinId,
                        PinNumber = "1",
                        PinName = pinName,
                        Layer = ElectricalLayer.Power,
                        Status = PinStatus.Normal
                    }
                }
            }
        }
    };
}
