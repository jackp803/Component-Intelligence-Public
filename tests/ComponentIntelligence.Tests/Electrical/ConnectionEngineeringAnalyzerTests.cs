using ComponentIntelligence.Electrical.Cables;
using ComponentIntelligence.Electrical.Domain;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class ConnectionEngineeringAnalyzerTests
{
    [Fact]
    public void Analyze_CalculatesPowerOnlyWhenVoltageAndLoadCurrentAreKnown()
    {
        var project = NewProject();
        var source = AddSinglePinComponent(project, "src", "PS1", "PWR", "p1", "+24V", ElectricalLayer.Power);
        source.Power = new PowerCapability
        {
            Role = PowerRole.Source,
            Polarity = Polarity.Positive,
            Voltage = new VoltageSpecification { Type = VoltageType.Dc, NominalVoltage = 24 }
        };
        source.Digital = new DigitalCapability { MaxOutputCurrentAmp = 2.0 };

        var sink = AddSinglePinComponent(project, "load", "S1", "PWR", "p1", "+24V", ElectricalLayer.Power);
        sink.Power = new PowerCapability
        {
            Role = PowerRole.Input,
            Polarity = Polarity.Positive,
            Voltage = new VoltageSpecification { Type = VoltageType.Dc, MinVoltage = 18, MaxVoltage = 30 }
        };
        sink.Digital = new DigitalCapability { RequiredInputCurrentAmp = 0.3 };

        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = "conn-1",
            FromEndpointId = source.PinId,
            ToEndpointId = sink.PinId,
            Kind = ConnectionKind.Wire
        });

        var result = new ConnectionEngineeringAnalyzer().Analyze(project, "conn-1");

        Assert.Equal(ElectricalLayer.Power, result.Layer);
        Assert.Equal(24, result.NominalVoltage);
        Assert.Equal(0.3, result.RequiredCurrentAmp);
        Assert.Equal(2.0, result.SourceCapacityAmp);
        Assert.True(result.PowerWatt.HasValue);
        Assert.Equal(7.2, result.PowerWatt.Value, 3);
        Assert.Contains(result.MissingData, item => item.Contains("Cable Length", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.MissingData, item => item.Contains("Conductor Area", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_DoesNotTreatDrawnCableRouteAsEngineeringLength()
    {
        var project = NewProject();
        var a = AddPortComponent(project, "a", "A1", 0.25, 1.5);
        var b = AddPortComponent(project, "b", "B1", 0.25, 1.5);
        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = "conn-route",
            FromEndpointId = a.PortId,
            ToEndpointId = b.PortId,
            Kind = ConnectionKind.Cable
        });
        project.CableRoutes.Add(new CableRoute
        {
            CableRouteId = "route-1",
            ConnectionOrCableId = "conn-route",
            Segments =
            {
                new RouteSegment { SegmentId = "seg-1", StartXMm = 0, StartYMm = 0, EndXMm = 5000, EndYMm = 0 }
            }
        });

        var result = new ConnectionEngineeringAnalyzer().Analyze(project, "conn-route");

        Assert.Null(result.ProvidedLengthMm);
        Assert.Equal(CableLengthSource.Unknown, result.LengthSource);
        Assert.Contains(result.MissingData, item => item.Contains("Layout / Cable Route", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_PrefersPhysicalCableProvidedLengthOverConnectionFallback()
    {
        var project = NewProject();
        var a = AddPortComponent(project, "a", "A1", 0.25, 1.5);
        var b = AddPortComponent(project, "b", "B1", 0.25, 1.5);
        project.Cables.Add(new CableInstance
        {
            CableInstanceId = "cab-1",
            CableDefinitionId = "def-1",
            ProvidedLengthMm = 4800,
            LengthSource = CableLengthSource.Mechanical
        });
        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = "conn-cable",
            FromEndpointId = a.PortId,
            ToEndpointId = b.PortId,
            Kind = ConnectionKind.Cable,
            CableInstanceId = "cab-1",
            ProvidedLengthMm = 900,
            LengthSource = CableLengthSource.User
        });

        var result = new ConnectionEngineeringAnalyzer().Analyze(project, "conn-cable");

        Assert.Equal(4800, result.ProvidedLengthMm);
        Assert.Equal(CableLengthSource.Mechanical, result.LengthSource);
        Assert.DoesNotContain(result.MissingData, item => item.Contains("Cable Length", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_Rs485PortRequiresTwistedPairAndCountsKnownPins()
    {
        var project = NewProject();
        var a = AddRs485Port(project, "a", "DEV1", ConnectorGender.Female);
        var b = AddRs485Port(project, "b", "DEV2", ConnectorGender.Male);
        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = "conn-rs485",
            FromEndpointId = a.PortId,
            ToEndpointId = b.PortId,
            Kind = ConnectionKind.Cable
        });

        var result = new ConnectionEngineeringAnalyzer().Analyze(project, "conn-rs485");

        Assert.Equal(ElectricalLayer.Communication, result.Layer);
        Assert.Equal("RS485", result.Protocol);
        Assert.Equal(RequirementLevel.Required, result.TwistedPair);
        Assert.Equal(2, result.RequiredCoreCount);
        Assert.Contains("M12", result.ConnectorA ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(result.MissingData, item => item.Contains("Protocol", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_WarnsWhenSelectedAreaIsOutsideTerminationRange()
    {
        var project = NewProject();
        var a = AddPortComponent(project, "a", "A1", 0.5, 1.5);
        var b = AddPortComponent(project, "b", "B1", 0.25, 1.0);
        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = "conn-area",
            FromEndpointId = a.PortId,
            ToEndpointId = b.PortId,
            Kind = ConnectionKind.Cable,
            ConductorAreaMm2 = 1.5
        });

        var result = new ConnectionEngineeringAnalyzer().Analyze(project, "conn-area");

        Assert.Equal(0.5, result.TerminationMinAreaMm2);
        Assert.Equal(1.0, result.TerminationMaxAreaMm2);
        Assert.Contains(result.Warnings, item => item.Contains("大於端點允許最大值", StringComparison.OrdinalIgnoreCase));
    }

    private static ElectricalProject NewProject() => new() { ProjectId = "p1", Name = "test" };

    private static ComponentPin AddSinglePinComponent(
        ElectricalProject project,
        string id,
        string reference,
        string portName,
        string pinNumber,
        string function,
        ElectricalLayer layer)
    {
        var pin = new ComponentPin
        {
            PinId = $"{id}:pin:{pinNumber}",
            PinNumber = pinNumber,
            Function = function,
            Layer = layer,
            Status = PinStatus.Normal,
            IsRequired = true
        };
        var port = new ComponentPort
        {
            PortId = $"{id}:port",
            Name = portName,
            Connector = new ConnectorDefinition { ConnectorId = $"{id}:connector", Family = "Terminal", MountType = ConnectorMountType.Device },
            Pins = { pin }
        };
        project.Components.Add(new ComponentInstance
        {
            ComponentInstanceId = id,
            ComponentDefinitionId = id,
            TypeKey = "TEST",
            ReferenceDesignator = reference,
            Ports = { port }
        });
        return pin;
    }

    private static ComponentPort AddRs485Port(ElectricalProject project, string id, string reference, ConnectorGender gender)
    {
        var port = new ComponentPort
        {
            PortId = $"{id}:rs485",
            Name = "RS485",
            Protocol = "RS485",
            Connector = new ConnectorDefinition
            {
                ConnectorId = $"{id}:connector",
                Family = "M12",
                Coding = "A",
                PinCount = 2,
                Gender = gender,
                MountType = ConnectorMountType.Device
            },
            Pins =
            {
                new ComponentPin { PinId = $"{id}:A", PinNumber = "1", Function = "A", Protocol = "RS485", Layer = ElectricalLayer.Communication, DifferentialRole = DifferentialRole.Positive, Status = PinStatus.Normal, IsRequired = true },
                new ComponentPin { PinId = $"{id}:B", PinNumber = "2", Function = "B", Protocol = "RS485", Layer = ElectricalLayer.Communication, DifferentialRole = DifferentialRole.Negative, Status = PinStatus.Normal, IsRequired = true }
            }
        };
        project.Components.Add(new ComponentInstance
        {
            ComponentInstanceId = id,
            ComponentDefinitionId = id,
            TypeKey = "RS485_DEVICE",
            ReferenceDesignator = reference,
            Ports = { port }
        });
        return port;
    }

    private static ComponentPort AddPortComponent(ElectricalProject project, string id, string reference, double minArea, double maxArea)
    {
        var port = new ComponentPort
        {
            PortId = $"{id}:port",
            Name = "X1",
            Connector = new ConnectorDefinition
            {
                ConnectorId = $"{id}:connector",
                Family = "Terminal",
                MinTerminationAreaMm2 = minArea,
                MaxTerminationAreaMm2 = maxArea,
                MountType = ConnectorMountType.Device
            }
        };
        project.Components.Add(new ComponentInstance
        {
            ComponentInstanceId = id,
            ComponentDefinitionId = id,
            TypeKey = "TEST",
            ReferenceDesignator = reference,
            Ports = { port }
        });
        return port;
    }
}
