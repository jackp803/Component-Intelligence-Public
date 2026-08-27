using System.Text.Json;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Export;
using ContractComponentIR = ComponentIntelligence.Contracts.ComponentIR;
using ContractComponentIrIdentity = ComponentIntelligence.Contracts.ComponentIrIdentity;
using ContractPowerConversion = ComponentIntelligence.Contracts.ComponentPowerConversion;
using ContractPort = ComponentIntelligence.Contracts.ComponentPort;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class ElectricalPowerEvidenceConversionEndpointInjectivityTests
{
    [Fact]
    public void DistinctSourcePortRefsWithSameRuntimePortId_FailClosed()
    {
        var project = ProjectWithConverter("port-collision");
        var converter = project.Components[0];
        converter.Ports.Add(Port("runtime-shared", "PORT-A"));
        converter.Ports.Add(Port("runtime-shared", "PORT-B"));
        converter.Ports.Add(Port("runtime-out", "OUT"));
        converter.PowerConversions.Add(Conversion(["PORT-A", "PORT-B"], [], ["OUT"], []));

        var contract = ElectricalPowerEvidenceV1Builder.Build(project);
        var conversion = Assert.Single(contract.Conversions);

        Assert.Empty(conversion.InputEndpointIds);
        Assert.Equal(new[] { "runtime-out" }, conversion.OutputEndpointIds);
        AssertCollisionBlocker(contract, "INPUT", "runtime-shared", "Port:PORT-A", "Port:PORT-B");
    }

    [Fact]
    public void DistinctSourcePinRefsWithSameRuntimePinId_FailClosed()
    {
        var project = ProjectWithConverter("pin-collision");
        var converter = project.Components[0];
        converter.Ports.Add(Port("runtime-a", "A", Pin("runtime-pin-shared", "PIN-A", "1")));
        converter.Ports.Add(Port("runtime-b", "B", Pin("runtime-pin-shared", "PIN-B", "2")));
        converter.Ports.Add(Port("runtime-out", "OUT"));
        converter.PowerConversions.Add(Conversion([], ["PIN-A", "PIN-B"], ["OUT"], []));

        var contract = ElectricalPowerEvidenceV1Builder.Build(project);
        var conversion = Assert.Single(contract.Conversions);

        Assert.Empty(conversion.InputEndpointIds);
        AssertCollisionBlocker(contract, "INPUT", "runtime-pin-shared", "Pin:PIN-A", "Pin:PIN-B");
    }

    [Fact]
    public void PortAndPinWithSameRuntimeEndpointId_OnSameSide_FailClosed()
    {
        var project = ProjectWithConverter("typed-runtime-collision");
        var converter = project.Components[0];
        converter.Ports.Add(Port("runtime-shared", "PORT-IN"));
        converter.Ports.Add(Port("runtime-holder", "HOLDER", Pin("runtime-shared", "PIN-IN", "7")));
        converter.Ports.Add(Port("runtime-out", "OUT"));
        converter.PowerConversions.Add(Conversion(["PORT-IN"], ["PIN-IN"], ["OUT"], []));

        var contract = ElectricalPowerEvidenceV1Builder.Build(project);
        var conversion = Assert.Single(contract.Conversions);

        Assert.Empty(conversion.InputEndpointIds);
        AssertCollisionBlocker(contract, "INPUT", "runtime-shared", "Pin:PIN-IN", "Port:PORT-IN");
    }

    [Fact]
    public void DistinctSourceRefsWithDistinctRuntimeEndpoints_RemainAccepted()
    {
        var project = ProjectWithConverter("distinct");
        var converter = project.Components[0];
        converter.Ports.Add(Port("runtime-port-a", "PORT-A"));
        converter.Ports.Add(Port("runtime-port-b", "PORT-B", Pin("runtime-pin-b", "PIN-B", "2")));
        converter.Ports.Add(Port("runtime-out", "OUT"));
        converter.PowerConversions.Add(Conversion(["PORT-A", "PORT-B"], ["PIN-B"], ["OUT"], []));

        var contract = ElectricalPowerEvidenceV1Builder.Build(project);
        var conversion = Assert.Single(contract.Conversions);

        Assert.Equal(new[] { "runtime-pin-b", "runtime-port-a", "runtime-port-b" }, conversion.InputEndpointIds);
        Assert.DoesNotContain(contract.BlockingRequirements, item => item.Code.Contains("RUNTIME_ENDPOINT_ID_COLLISION", StringComparison.Ordinal));
    }

    [Fact]
    public void SameSourceRefDuplicatedAndReordered_NormalizesWithoutFalseCollision()
    {
        var first = ProjectWithConverter("same-ref-first");
        var firstConverter = first.Components[0];
        firstConverter.Ports.Add(Port("runtime-in", "IN"));
        firstConverter.Ports.Add(Port("runtime-out", "OUT"));
        firstConverter.PowerConversions.Add(Conversion(["IN", "IN", "IN"], [], ["OUT", "OUT"], []));

        var second = ProjectWithConverter("same-ref-second");
        var secondConverter = second.Components[0];
        secondConverter.Ports.Add(Port("runtime-out", "OUT"));
        secondConverter.Ports.Add(Port("runtime-in", "IN"));
        secondConverter.PowerConversions.Add(Conversion(["IN"], [], ["OUT"], []));

        var firstContract = ElectricalPowerEvidenceV1Builder.Build(first);
        var secondContract = ElectricalPowerEvidenceV1Builder.Build(second);

        Assert.Equal(new[] { "runtime-in" }, Assert.Single(firstContract.Conversions).InputEndpointIds);
        Assert.DoesNotContain(firstContract.BlockingRequirements, item => item.Code.Contains("RUNTIME_ENDPOINT_ID_COLLISION", StringComparison.Ordinal));
        Assert.Equal(JsonSerializer.Serialize(firstContract), JsonSerializer.Serialize(secondContract));
    }

    [Fact]
    public void DistinctRefsAcrossInputAndOutputSharingRuntimeId_FailClosedOnBothSides()
    {
        var project = ProjectWithConverter("cross-side-collision");
        var converter = project.Components[0];
        converter.Ports.Add(Port("runtime-shared", "INPUT-PORT"));
        converter.Ports.Add(Port("runtime-holder", "OUTPUT-HOLDER", Pin("runtime-shared", "OUTPUT-PIN", "3")));
        converter.PowerConversions.Add(Conversion(["INPUT-PORT"], [], [], ["OUTPUT-PIN"]));

        var contract = ElectricalPowerEvidenceV1Builder.Build(project);
        var conversion = Assert.Single(contract.Conversions);

        Assert.Empty(conversion.InputEndpointIds);
        Assert.Empty(conversion.OutputEndpointIds);
        AssertCollisionBlocker(contract, "INPUT", "runtime-shared", "Pin:OUTPUT-PIN", "Port:INPUT-PORT");
        AssertCollisionBlocker(contract, "OUTPUT", "runtime-shared", "Pin:OUTPUT-PIN", "Port:INPUT-PORT");
    }

    [Fact]
    public void InputOutputAndObjectOrderPermutations_ProduceIdenticalCollisionOutput()
    {
        var first = BuildPermutationCollisionProject(reverseObjects: false, reverseReferences: false);
        var second = BuildPermutationCollisionProject(reverseObjects: true, reverseReferences: true);

        var firstJson = JsonSerializer.Serialize(ElectricalPowerEvidenceV1Builder.Build(first));
        var secondJson = JsonSerializer.Serialize(ElectricalPowerEvidenceV1Builder.Build(second));

        Assert.Equal(firstJson, secondJson);
    }

    [Fact]
    public void WeakSignalsAndGeometryNoise_DoNotAffectCollisionDetection()
    {
        var plain = BuildNoiseCollisionProject(noisy: false);
        var noisy = BuildNoiseCollisionProject(noisy: true);

        var plainContract = ElectricalPowerEvidenceV1Builder.Build(plain);
        var noisyContract = ElectricalPowerEvidenceV1Builder.Build(noisy);
        var plainCollision = Assert.Single(plainContract.BlockingRequirements, item =>
            item.Code == "POWER_CONVERSION_INPUT_RUNTIME_ENDPOINT_ID_COLLISION");
        var noisyCollision = Assert.Single(noisyContract.BlockingRequirements, item =>
            item.Code == "POWER_CONVERSION_INPUT_RUNTIME_ENDPOINT_ID_COLLISION");

        Assert.Equal(plainCollision.MissingFields, noisyCollision.MissingFields);
        Assert.Empty(Assert.Single(plainContract.Conversions).InputEndpointIds);
        Assert.Empty(Assert.Single(noisyContract.Conversions).InputEndpointIds);
    }

    [Fact]
    public void SameSourceRefMatchingTwoRuntimeObjectsWithSameId_RemainsAmbiguousBeforeIdDeduplication()
    {
        var project = ProjectWithConverter("same-source-object-ambiguity");
        var converter = project.Components[0];
        converter.Ports.Add(Port("runtime-shared", "DUP"));
        converter.Ports.Add(Port("runtime-shared", "DUP"));
        converter.Ports.Add(Port("runtime-out", "OUT"));
        converter.PowerConversions.Add(Conversion(["DUP"], [], ["OUT"], []));

        var contract = ElectricalPowerEvidenceV1Builder.Build(project);

        Assert.Empty(Assert.Single(contract.Conversions).InputEndpointIds);
        Assert.Contains(contract.BlockingRequirements, item =>
            item.Code == "POWER_CONVERSION_INPUT_SOURCE_PORT_AMBIGUOUS" && item.SubjectId == "CONV-1");
    }

    [Fact]
    public void ComponentProjectBridgeLossyPortNormalizationCollision_FailsClosedEndToEnd()
    {
        var source = new ContractComponentIR
        {
            Identity = new ContractComponentIrIdentity
            {
                ComponentId = "CONVERTER-DEF",
                Manufacturer = "TEST",
                Model = "PUNCTUATION-COLLISION"
            },
            Ports =
            [
                new ContractPort { PortId = "A_B" },
                new ContractPort { PortId = "A-B" },
                new ContractPort { PortId = "OUT" }
            ],
            PowerConversions =
            [
                new ContractPowerConversion
                {
                    ConversionId = "CONV-1",
                    InputPowerDomainId = "DOMAIN-IN",
                    OutputPowerDomainId = "DOMAIN-OUT",
                    InputPortIds = ["A_B", "A-B"],
                    OutputPortIds = ["OUT"]
                }
            ]
        };

        var instance = new ComponentProjectBridge().CreateInstance(source, "converter");
        var collidedPorts = instance.Ports
            .Where(port => port.SourcePortId is "A_B" or "A-B")
            .OrderBy(port => port.SourcePortId, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(2, collidedPorts.Length);
        Assert.NotSame(collidedPorts[0], collidedPorts[1]);
        Assert.Equal(collidedPorts[0].PortId, collidedPorts[1].PortId);

        var project = new ElectricalProject { ProjectId = "bridge-e2e" };
        project.Components.Add(instance);
        var contract = ElectricalPowerEvidenceV1Builder.Build(project);

        Assert.Empty(Assert.Single(contract.Conversions).InputEndpointIds);
        AssertCollisionBlocker(contract, "INPUT", collidedPorts[0].PortId, "Port:A-B", "Port:A_B");
    }

    [Fact]
    public void LegacyV1PayloadWithoutEndpointArrays_RemainsCompatible()
    {
        const string json = """
            {
              "schemaVersion":"electrical-power-evidence.v1",
              "domains":[],
              "participants":[],
              "conversions":[{
                "conversionId":"CONV-LEGACY",
                "componentInstanceId":"converter",
                "inputPowerDomainId":"D-IN",
                "outputPowerDomainId":"D-OUT",
                "inputSourcePortIds":["IN"],
                "inputSourcePinIds":[],
                "outputSourcePortIds":["OUT"],
                "outputSourcePinIds":[],
                "evidenceStatus":"Confirmed",
                "provenance":[]
              }],
              "blockingRequirements":[]
            }
            """;

        var contract = Assert.IsType<ElectricalPowerEvidenceV1Contract>(
            JsonSerializer.Deserialize<ElectricalPowerEvidenceV1Contract>(json));
        var conversion = Assert.Single(contract.Conversions);

        Assert.Equal("electrical-power-evidence.v1", contract.SchemaVersion);
        Assert.Empty(conversion.InputEndpointIds);
        Assert.Empty(conversion.OutputEndpointIds);
        ElectricalPowerEvidenceV1Contract.EnsureSupportedSchema(contract.SchemaVersion);
    }

    private static ElectricalProject BuildPermutationCollisionProject(bool reverseObjects, bool reverseReferences)
    {
        var project = ProjectWithConverter("permutation-collision");
        var converter = project.Components[0];
        var ports = new[]
        {
            Port("runtime-input-shared", "IN-A"),
            Port("runtime-input-shared", "IN-B"),
            Port("runtime-output-shared", "OUT-A"),
            Port("runtime-output-shared", "OUT-B")
        };
        foreach (var port in reverseObjects ? ports.Reverse() : ports)
            converter.Ports.Add(port);

        var inputRefs = reverseReferences ? new[] { "IN-B", "IN-A" } : new[] { "IN-A", "IN-B" };
        var outputRefs = reverseReferences ? new[] { "OUT-B", "OUT-A" } : new[] { "OUT-A", "OUT-B" };
        converter.PowerConversions.Add(Conversion(inputRefs, [], outputRefs, []));
        return project;
    }

    private static ElectricalProject BuildNoiseCollisionProject(bool noisy)
    {
        var project = ProjectWithConverter("noise-collision");
        var converter = project.Components[0];
        converter.ComponentDefinitionId = noisy ? "MODEL-NOISE-B" : "MODEL-NOISE-A";
        converter.TypeKey = noisy ? "TYPE-NOISE-B" : "TYPE-NOISE-A";

        var first = Port("runtime-shared", "PORT-A");
        var second = Port("runtime-shared", "PORT-B");
        first.Name = noisy ? "misleading-output-name" : "plain-a";
        second.Name = noisy ? "misleading-input-name" : "plain-b";
        first.Pins.Add(new ComponentPin
        {
            PinId = "noise-pin-a",
            SourcePinId = "NOISE-A",
            PinNumber = noisy ? "999" : "1",
            PinName = noisy ? "page-42-left" : "plain",
            Power = new PowerCapability
            {
                Role = PowerRole.Unknown,
                Voltage = new VoltageSpecification { NominalVoltage = noisy ? 480 : 24, Type = VoltageType.Dc }
            }
        });
        converter.Ports.Add(first);
        converter.Ports.Add(second);
        converter.Ports.Add(Port("runtime-out", "OUT"));
        converter.PowerConversions.Add(Conversion(["PORT-A", "PORT-B"], [], ["OUT"], []));

        project.TopologyPlacements.Add(new TopologyPlacement
        {
            ObjectId = "converter",
            ObjectKind = noisy ? "PAGE-99-LOOKING-KIND" : "component",
            X = noisy ? 9999 : 0,
            Y = noisy ? -9999 : 0
        });
        var routes = new[]
        {
            new CableRoute { CableRouteId = "route-a", ConnectionOrCableId = "noise-a" },
            new CableRoute { CableRouteId = "route-b", ConnectionOrCableId = "noise-b" }
        };
        foreach (var route in noisy ? routes.Reverse() : routes)
            project.CableRoutes.Add(route);

        return project;
    }

    private static ElectricalProject ProjectWithConverter(string projectId)
    {
        var project = new ElectricalProject { ProjectId = projectId };
        project.Components.Add(new ComponentInstance
        {
            ComponentInstanceId = "converter",
            ComponentDefinitionId = "converter-def",
            TypeKey = "TEST"
        });
        return project;
    }

    private static ComponentPort Port(string runtimeId, string sourceId, params ComponentPin[] pins)
    {
        var port = new ComponentPort
        {
            PortId = runtimeId,
            SourcePortId = sourceId,
            Name = $"display-{sourceId}"
        };
        port.Pins.AddRange(pins);
        return port;
    }

    private static ComponentPin Pin(string runtimeId, string sourceId, string pinNumber) => new()
    {
        PinId = runtimeId,
        SourcePinId = sourceId,
        PinNumber = pinNumber,
        PinName = $"display-{sourceId}"
    };

    private static PowerConversionEvidence Conversion(
        IEnumerable<string> inputPorts,
        IEnumerable<string> inputPins,
        IEnumerable<string> outputPorts,
        IEnumerable<string> outputPins) => new()
    {
        ConversionId = "CONV-1",
        ComponentInstanceId = "converter",
        InputPowerDomainId = "DOMAIN-IN",
        OutputPowerDomainId = "DOMAIN-OUT",
        InputSourcePortIds = inputPorts.ToList(),
        InputSourcePinIds = inputPins.ToList(),
        OutputSourcePortIds = outputPorts.ToList(),
        OutputSourcePinIds = outputPins.ToList()
    };

    private static void AssertCollisionBlocker(
        ElectricalPowerEvidenceV1Contract contract,
        string side,
        string runtimeEndpointId,
        params string[] sourceRefs)
    {
        var blocker = Assert.Single(contract.BlockingRequirements, item =>
            item.Code == $"POWER_CONVERSION_{side}_RUNTIME_ENDPOINT_ID_COLLISION" &&
            item.SubjectId == "CONV-1");
        Assert.Contains($"runtimeEndpointId:{runtimeEndpointId}", blocker.MissingFields);
        foreach (var sourceRef in sourceRefs)
            Assert.Contains(sourceRef, blocker.MissingFields);
    }
}
