using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Export;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class ElectricalPowerEvidenceConversionEndpointIdentityTests
{
    [Fact]
    public void ComponentScopedTypedSourceReferences_MapExactlyToRuntimeEndpoints()
    {
        var project = new ElectricalProject { ProjectId = "exact-map" };
        var converter = Component("converter", "converter-def");
        converter.Ports.Add(Port("runtime-port-in", "SRC-IN", Pin("runtime-pin-in", "SRC-PIN-IN", "1")));
        converter.Ports.Add(Port("runtime-port-out", "SRC-OUT", Pin("runtime-pin-out", "SRC-PIN-OUT", "2")));
        converter.PowerConversions.Add(Conversion(
            "converter",
            ["SRC-IN"], ["SRC-PIN-IN"],
            ["SRC-OUT"], ["SRC-PIN-OUT"]));
        project.Components.Add(converter);

        // Same source identities on another component are legal because the join is component-scoped.
        var other = Component("other", "other-def");
        other.Ports.Add(Port("other-port", "SRC-IN", Pin("other-pin", "SRC-PIN-IN", "99")));
        project.Components.Add(other);

        var contract = ElectricalPowerEvidenceV1Builder.Build(project);
        var conversion = Assert.Single(contract.Conversions);

        Assert.Equal(new[] { "runtime-pin-in", "runtime-port-in" }, conversion.InputEndpointIds);
        Assert.Equal(new[] { "runtime-pin-out", "runtime-port-out" }, conversion.OutputEndpointIds);
        Assert.Equal("SRC-IN", Assert.Single(conversion.InputSourcePortIds));
        Assert.Equal("SRC-PIN-IN", Assert.Single(conversion.InputSourcePinIds));
        Assert.DoesNotContain(contract.BlockingRequirements, item => item.Code.Contains("SOURCE_PORT", StringComparison.Ordinal));
        Assert.DoesNotContain(contract.BlockingRequirements, item => item.Code.Contains("SOURCE_PIN", StringComparison.Ordinal));
    }

    [Fact]
    public void PortAndPinSourceIdentityCollision_RemainsTypedAndUnambiguous()
    {
        var project = new ElectricalProject { ProjectId = "typed-collision" };
        var converter = Component("converter", "def");
        converter.Ports.Add(Port("runtime-port", "SAME", Pin("runtime-pin", "SAME", "not-identity")));
        converter.PowerConversions.Add(Conversion("converter", ["SAME"], ["SAME"], ["SAME"], ["SAME"]));
        project.Components.Add(converter);

        var conversion = Assert.Single(ElectricalPowerEvidenceV1Builder.Build(project).Conversions);

        Assert.Equal(new[] { "runtime-pin", "runtime-port" }, conversion.InputEndpointIds);
        Assert.Equal(new[] { "runtime-pin", "runtime-port" }, conversion.OutputEndpointIds);
    }

    [Fact]
    public void MissingSideSourceReference_FailsClosedWithoutEndpointClaim()
    {
        var project = new ElectricalProject { ProjectId = "missing-ref" };
        var converter = Component("converter", "def");
        converter.Ports.Add(Port("runtime-out", "OUT"));
        converter.PowerConversions.Add(Conversion("converter", [], [], ["OUT"], []));
        project.Components.Add(converter);

        var contract = ElectricalPowerEvidenceV1Builder.Build(project);
        var conversion = Assert.Single(contract.Conversions);

        Assert.Empty(conversion.InputEndpointIds);
        Assert.Equal(new[] { "runtime-out" }, conversion.OutputEndpointIds);
        Assert.Contains(contract.BlockingRequirements, item =>
            item.Code == "POWER_CONVERSION_INPUT_SOURCE_REFERENCE_REQUIRED" && item.SubjectId == "CONV-1");
    }

    [Fact]
    public void ZeroMatchThatExistsOnlyOnDifferentComponent_IsCrossComponentBlocker()
    {
        var project = new ElectricalProject { ProjectId = "cross-component" };
        var converter = Component("converter", "converter-def");
        converter.Ports.Add(Port("converter-out", "OUT"));
        converter.PowerConversions.Add(Conversion("converter", ["FOREIGN"], [], ["OUT"], []));
        project.Components.Add(converter);

        var foreign = Component("foreign", "foreign-def");
        foreign.Ports.Add(Port("foreign-runtime", "FOREIGN"));
        project.Components.Add(foreign);

        var contract = ElectricalPowerEvidenceV1Builder.Build(project);
        var conversion = Assert.Single(contract.Conversions);

        Assert.Empty(conversion.InputEndpointIds);
        Assert.Contains(contract.BlockingRequirements, item =>
            item.Code == "POWER_CONVERSION_INPUT_SOURCE_PORT_CROSS_COMPONENT" &&
            item.SubjectId == "CONV-1" &&
            item.MissingFields.SequenceEqual(new[] { "FOREIGN" }));
    }

    [Fact]
    public void ZeroMatchWithNoForeignCandidate_IsUnresolvedBlocker()
    {
        var project = new ElectricalProject { ProjectId = "unresolved" };
        var converter = Component("converter", "def");
        converter.Ports.Add(Port("runtime-out", "OUT"));
        converter.PowerConversions.Add(Conversion("converter", [], ["MISSING-PIN"], ["OUT"], []));
        project.Components.Add(converter);

        var contract = ElectricalPowerEvidenceV1Builder.Build(project);

        Assert.Empty(Assert.Single(contract.Conversions).InputEndpointIds);
        Assert.Contains(contract.BlockingRequirements, item =>
            item.Code == "POWER_CONVERSION_INPUT_SOURCE_PIN_UNRESOLVED" &&
            item.MissingFields.SequenceEqual(new[] { "MISSING-PIN" }));
    }

    [Fact]
    public void MultipleRuntimeMatchesWithinComponent_AreAmbiguousAndFailClosed()
    {
        var project = new ElectricalProject { ProjectId = "ambiguous" };
        var converter = Component("converter", "def");
        converter.Ports.Add(Port("runtime-a", "DUP", Pin("runtime-pin-a", "PIN-DUP", "1")));
        converter.Ports.Add(Port("runtime-b", "DUP", Pin("runtime-pin-b", "PIN-DUP", "2")));
        converter.Ports.Add(Port("runtime-out", "OUT"));
        converter.PowerConversions.Add(Conversion("converter", ["DUP"], ["PIN-DUP"], ["OUT"], []));
        project.Components.Add(converter);

        var contract = ElectricalPowerEvidenceV1Builder.Build(project);
        var conversion = Assert.Single(contract.Conversions);

        Assert.Empty(conversion.InputEndpointIds);
        Assert.Contains(contract.BlockingRequirements, item => item.Code == "POWER_CONVERSION_INPUT_SOURCE_PORT_AMBIGUOUS");
        Assert.Contains(contract.BlockingRequirements, item => item.Code == "POWER_CONVERSION_INPUT_SOURCE_PIN_AMBIGUOUS");
    }

    [Fact]
    public void DuplicateAndReorderedSourceLists_NormalizeToIdenticalLogicalOutput()
    {
        var first = BuildPermutationProject(
            ["IN-B", "IN-A", "IN-A"],
            ["PIN-B", "PIN-A", "PIN-A"],
            ["OUT-B", "OUT-A", "OUT-A"]);
        var second = BuildPermutationProject(
            ["IN-A", "IN-B"],
            ["PIN-A", "PIN-B"],
            ["OUT-A", "OUT-B"]);

        var firstJson = JsonSerializer.Serialize(ElectricalPowerEvidenceV1Builder.Build(first));
        var secondJson = JsonSerializer.Serialize(ElectricalPowerEvidenceV1Builder.Build(second));

        Assert.Equal(firstJson, secondJson);
    }

    [Fact]
    public void NamesTypeKeyAndPinNumberNoise_CannotChangeEndpointMapping()
    {
        var project = new ElectricalProject { ProjectId = "noise" };
        var converter = Component("converter", "def");
        converter.TypeKey = "totally-different-display-type";
        converter.Ports.Add(new ComponentPort
        {
            PortId = "runtime-real",
            SourcePortId = "AUTHORITATIVE",
            Name = "looks-like-WRONG-ID",
            Pins =
            {
                new ComponentPin
                {
                    PinId = "runtime-pin-real",
                    SourcePinId = "AUTHORITATIVE-PIN",
                    PinNumber = "WRONG-LOOKING-NUMBER",
                    PinName = "another-source-looking-name"
                }
            }
        });
        converter.Ports.Add(Port("runtime-out", "OUT"));
        converter.PowerConversions.Add(Conversion(
            "converter", ["AUTHORITATIVE"], ["AUTHORITATIVE-PIN"], ["OUT"], []));
        project.Components.Add(converter);

        var conversion = Assert.Single(ElectricalPowerEvidenceV1Builder.Build(project).Conversions);

        Assert.Equal(new[] { "runtime-pin-real", "runtime-real" }, conversion.InputEndpointIds);
        Assert.DoesNotContain("looks-like-WRONG-ID", conversion.InputEndpointIds);
        Assert.DoesNotContain("WRONG-LOOKING-NUMBER", conversion.InputEndpointIds);
    }

    [Fact]
    public void LegacyV1JsonWithoutEndpointArrays_RemainsDeserializableWithEmptyArrays()
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

        Assert.Empty(conversion.InputEndpointIds);
        Assert.Empty(conversion.OutputEndpointIds);
        Assert.Equal("IN", Assert.Single(conversion.InputSourcePortIds));
        ElectricalPowerEvidenceV1Contract.EnsureSupportedSchema(contract.SchemaVersion);
    }

    private static ElectricalProject BuildPermutationProject(
        IReadOnlyList<string> inputPorts,
        IReadOnlyList<string> inputPins,
        IReadOnlyList<string> outputPorts)
    {
        var project = new ElectricalProject { ProjectId = "permutation" };
        var converter = Component("converter", "def");
        converter.Ports.Add(Port("runtime-in-a", "IN-A", Pin("runtime-pin-a", "PIN-A", "1")));
        converter.Ports.Add(Port("runtime-in-b", "IN-B", Pin("runtime-pin-b", "PIN-B", "2")));
        converter.Ports.Add(Port("runtime-out-a", "OUT-A"));
        converter.Ports.Add(Port("runtime-out-b", "OUT-B"));
        converter.PowerConversions.Add(Conversion("converter", inputPorts, inputPins, outputPorts, []));
        project.Components.Add(converter);
        return project;
    }

    private static ComponentInstance Component(string instanceId, string definitionId) => new()
    {
        ComponentInstanceId = instanceId,
        ComponentDefinitionId = definitionId,
        TypeKey = "TEST"
    };

    private static ComponentPort Port(string runtimeId, string sourceId, params ComponentPin[] pins)
    {
        var port = new ComponentPort
        {
            PortId = runtimeId,
            SourcePortId = sourceId,
            Name = $"display-{runtimeId}"
        };
        port.Pins.AddRange(pins);
        return port;
    }

    private static ComponentPin Pin(string runtimeId, string sourceId, string pinNumber) => new()
    {
        PinId = runtimeId,
        SourcePinId = sourceId,
        PinNumber = pinNumber,
        PinName = $"display-{runtimeId}"
    };

    private static PowerConversionEvidence Conversion(
        string componentInstanceId,
        IEnumerable<string> inputPorts,
        IEnumerable<string> inputPins,
        IEnumerable<string> outputPorts,
        IEnumerable<string> outputPins) => new()
    {
        ConversionId = "CONV-1",
        ComponentInstanceId = componentInstanceId,
        InputPowerDomainId = "DOMAIN-IN",
        OutputPowerDomainId = "DOMAIN-OUT",
        InputSourcePortIds = inputPorts.ToList(),
        InputSourcePinIds = inputPins.ToList(),
        OutputSourcePortIds = outputPorts.ToList(),
        OutputSourcePinIds = outputPins.ToList()
    };
}
