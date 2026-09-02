using System.Text.Json;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Export;
using ContractPin = ComponentIntelligence.Contracts.ComponentPin;
using ContractPort = ComponentIntelligence.Contracts.ComponentPort;
using DomainPin = ComponentIntelligence.Electrical.Domain.ComponentPin;
using DomainPort = ComponentIntelligence.Electrical.Domain.ComponentPort;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class ElectricalPowerEvidenceV1Tests
{
    private static readonly DateTimeOffset EvidenceTime = DateTimeOffset.Parse("2026-08-27T00:00:00Z");

    [Fact]
    public void Bridge_CopiesExplicitPowerDomainAndTypedSourceIdentity_WithoutPromotingVoltageText()
    {
        var explicitSource = SourceComponent(
            "explicit",
            "Output",
            "DOMAIN-A",
            voltageText: "24 V DC",
            sourcePinId: "SRC-PIN-A",
            sourcePortDomainId: "DOMAIN-PORT-A");
        var explicitInstance = new ComponentProjectBridge().CreateInstance(explicitSource, "inst-a");
        var explicitPort = Assert.Single(explicitInstance.Ports);
        var explicitPin = Assert.Single(explicitPort.Pins);

        Assert.Equal("PWR", explicitPort.SourcePortId);
        Assert.Equal("DOMAIN-PORT-A", explicitPort.PowerDomainId);
        Assert.Equal("SRC-PIN-A", explicitPin.SourcePinId);
        Assert.Equal("DOMAIN-A", explicitPin.PowerDomainId);

        var voltageOnly = SourceComponent(
            "voltage-only",
            "Input",
            powerDomainId: null,
            voltageText: "54 V DC",
            sourcePinId: "SRC-PIN-B");
        var voltageOnlyInstance = new ComponentProjectBridge().CreateInstance(voltageOnly, "inst-b");
        var voltageOnlyPin = Assert.Single(Assert.Single(voltageOnlyInstance.Ports).Pins);

        Assert.Null(voltageOnlyPin.PowerDomainId);
        Assert.Equal(54d, voltageOnlyPin.Power?.Voltage?.NominalVoltage);
    }

    [Fact]
    public void ExplicitProducerAndConsumerDomains_TransportAsOpaqueMembershipFacts()
    {
        var project = new ElectricalProject { ProjectId = "power-membership" };
        project.Components.Add(new ComponentProjectBridge().CreateInstance(
            SourceComponent("source", "Output", "DOMAIN-SOURCE", "24 V DC", "SRC-SOURCE"), "source-inst"));
        project.Components.Add(new ComponentProjectBridge().CreateInstance(
            SourceComponent("load", "Input", "DOMAIN-LOAD", "24 V DC", "SRC-LOAD"), "load-inst"));

        var contract = ElectricalPowerEvidenceV1Builder.Build(project);

        Assert.Equal("electrical-power-evidence.v1", contract.SchemaVersion);
        Assert.Equal(new[] { "DOMAIN-LOAD", "DOMAIN-SOURCE" }, contract.Domains.Select(item => item.PowerDomainId));
        var producer = Assert.Single(contract.Participants, item => item.Role == "Producer");
        var consumer = Assert.Single(contract.Participants, item => item.Role == "Consumer");
        Assert.Equal("DOMAIN-SOURCE", producer.PowerDomainId);
        Assert.Equal("DOMAIN-LOAD", consumer.PowerDomainId);
        Assert.Equal("SRC-SOURCE", producer.SourcePinId);
        Assert.Equal("PWR", producer.SourcePortId);
    }

    [Fact]
    public void RoleWithoutExplicitDomain_IsBlocked_AndNoSyntheticDomainIsCreated()
    {
        var project = new ElectricalProject { ProjectId = "missing-domain" };
        project.Components.Add(new ComponentProjectBridge().CreateInstance(
            SourceComponent("load", "Input", null, "18...30 V DC", "SRC-LOAD"), "load-inst"));

        var contract = ElectricalPowerEvidenceV1Builder.Build(project);

        Assert.Empty(contract.Domains);
        var participant = Assert.Single(contract.Participants);
        Assert.Equal("Consumer", participant.Role);
        Assert.Null(participant.PowerDomainId);
        Assert.Equal("POWER_DOMAIN_ID_REQUIRED", participant.BlockingReason);
        Assert.Contains(contract.BlockingRequirements, item =>
            item.Code == "POWER_DOMAIN_ID_REQUIRED" && item.SubjectId == participant.EndpointId);
    }

    [Fact]
    public void IdenticalVoltages_WithDistinctExplicitDomainIds_RemainDistinct()
    {
        var project = new ElectricalProject { ProjectId = "same-voltage-distinct-domains" };
        project.Components.Add(new ComponentProjectBridge().CreateInstance(
            SourceComponent("a", "Output", "DOMAIN-A", "24 V DC", "PIN-A"), "a"));
        project.Components.Add(new ComponentProjectBridge().CreateInstance(
            SourceComponent("b", "Output", "DOMAIN-B", "24 V DC", "PIN-B"), "b"));

        var contract = ElectricalPowerEvidenceV1Builder.Build(project);

        Assert.Equal(2, contract.Domains.Count);
        Assert.Equal(new[] { "DOMAIN-A", "DOMAIN-B" }, contract.Domains.Select(item => item.PowerDomainId));
    }

    [Fact]
    public void DifferingVoltages_WithoutExplicitDomainIds_CreateNoDomains()
    {
        var project = new ElectricalProject { ProjectId = "different-voltage-no-domain" };
        project.Components.Add(new ComponentProjectBridge().CreateInstance(
            SourceComponent("a", "Output", null, "24 V DC", "PIN-A"), "a"));
        project.Components.Add(new ComponentProjectBridge().CreateInstance(
            SourceComponent("b", "Input", null, "48 V DC", "PIN-B"), "b"));

        var contract = ElectricalPowerEvidenceV1Builder.Build(project);

        Assert.Empty(contract.Domains);
        Assert.Equal(2, contract.BlockingRequirements.Count(item => item.Code == "POWER_DOMAIN_ID_REQUIRED"));
    }

    [Fact]
    public void CompleteExplicitConversion_TransportsExactDomainsRefsAndProvenance()
    {
        var source = SourceComponent("converter", "Input", "DOMAIN-IN", "24 V DC", "PIN-IN") with
        {
            PowerConversions =
            [
                new ComponentPowerConversion
                {
                    ConversionId = "CONV-1",
                    InputPowerDomainId = "DOMAIN-IN",
                    OutputPowerDomainId = "DOMAIN-OUT",
                    InputPortIds = ["PWR-IN"],
                    InputPinIds = ["PIN-IN"],
                    OutputPortIds = ["PWR-OUT"],
                    OutputPinIds = ["PIN-OUT"],
                    Evidence = [SourceEvidence("conversion-declaration")]
                }
            ]
        };
        var project = new ElectricalProject { ProjectId = "conversion" };
        var instance = new ComponentProjectBridge().CreateInstance(source, "converter-inst");
        project.Components.Add(instance);

        var domainConversion = Assert.Single(instance.PowerConversions);
        Assert.Equal("CONV-1", domainConversion.ConversionId);
        Assert.Equal("PWR-IN", Assert.Single(domainConversion.InputSourcePortIds));
        Assert.Equal("PIN-OUT", Assert.Single(domainConversion.OutputSourcePinIds));
        Assert.Equal("ManufacturerDatasheet", Assert.Single(domainConversion.Evidence).SourceType);

        var contract = ElectricalPowerEvidenceV1Builder.Build(project);
        var conversion = Assert.Single(contract.Conversions);
        Assert.Equal("CONV-1", conversion.ConversionId);
        Assert.Equal("DOMAIN-IN", conversion.InputPowerDomainId);
        Assert.Equal("DOMAIN-OUT", conversion.OutputPowerDomainId);
        Assert.Equal("PWR-IN", Assert.Single(conversion.InputSourcePortIds));
        Assert.Equal("PIN-OUT", Assert.Single(conversion.OutputSourcePinIds));
        Assert.Equal("Confirmed", conversion.EvidenceStatus);
        Assert.Null(conversion.BlockingReason);
        Assert.Equal("ManufacturerDatasheet", Assert.Single(conversion.Provenance).SourceType);
        Assert.Contains(contract.Domains, item => item.PowerDomainId == "DOMAIN-IN");
        Assert.Contains(contract.Domains, item => item.PowerDomainId == "DOMAIN-OUT");
    }

    [Fact]
    public void IncompleteConversion_RemainsUnknownAndBlockedWithoutInference()
    {
        var project = new ElectricalProject { ProjectId = "incomplete-conversion" };
        var component = BareComponent("converter");
        component.PowerConversions.Add(new PowerConversionEvidence
        {
            ConversionId = "CONV-INCOMPLETE",
            ComponentInstanceId = component.ComponentInstanceId,
            InputPowerDomainId = "DOMAIN-IN",
            OutputPowerDomainId = null
        });
        project.Components.Add(component);

        var contract = ElectricalPowerEvidenceV1Builder.Build(project);
        var conversion = Assert.Single(contract.Conversions);

        Assert.Equal("Unknown", conversion.EvidenceStatus);
        Assert.Equal("POWER_CONVERSION_FIELDS_REQUIRED", conversion.BlockingReason);
        Assert.Null(conversion.OutputPowerDomainId);
        var blocker = Assert.Single(contract.BlockingRequirements, item => item.Code == "POWER_CONVERSION_FIELDS_REQUIRED");
        Assert.Contains("outputPowerDomainId", blocker.MissingFields);
    }

    [Fact]
    public void ConsumerOrConverterDrawingRole_DoesNotCreatePowerConversion()
    {
        var project = NonPowerDirectWireProject();
        var drawingEvidence = new AutocadEngineeringDrawingEvidence
        {
            ComponentRoles =
            [
                Role("left", ComponentDrawingRole.ConsumerOrConverter),
                Role("right", ComponentDrawingRole.SensorOrControlDevice)
            ]
        };

        var graph = Assert.IsType<AutocadStagingGraphV2Contract>(
            new AutocadStagingGraphV2Builder().Prepare(
                project,
                Bindings("left:pin", "right:pin"),
                drawingEvidence).Graph);

        Assert.Empty(graph.PowerEvidence.Conversions);
        Assert.Contains(graph.DeviceRoles, item =>
            item.ComponentInstanceId == "left" && item.SourceDrawingRole == ComponentDrawingRole.ConsumerOrConverter);
    }

    [Fact]
    public void DuplicateParticipantAndConversionIdentities_BecomeHardBlockersWithoutLastWriteWins()
    {
        var project = new ElectricalProject { ProjectId = "identity-conflicts" };
        var first = BareComponent("a");
        first.Ports.Add(DomainPortWithPin("a:port", "D-A", "shared:pin", "D-A", PowerRole.Source));
        first.PowerConversions.Add(new PowerConversionEvidence
        {
            ConversionId = "CONV-DUP",
            ComponentInstanceId = "a",
            InputPowerDomainId = "D-A",
            OutputPowerDomainId = "D-B"
        });
        var second = BareComponent("b");
        second.Ports.Add(DomainPortWithPin("b:port", "D-B", "shared:pin", "D-B", PowerRole.Input));
        second.PowerConversions.Add(new PowerConversionEvidence
        {
            ConversionId = "CONV-DUP",
            ComponentInstanceId = "b",
            InputPowerDomainId = "D-C",
            OutputPowerDomainId = "D-D"
        });
        project.Components.Add(first);
        project.Components.Add(second);

        var contract = ElectricalPowerEvidenceV1Builder.Build(project);

        Assert.DoesNotContain(contract.Participants, item => item.EndpointId == "shared:pin");
        Assert.DoesNotContain(contract.Conversions, item => item.ConversionId == "CONV-DUP");
        Assert.Contains(contract.BlockingRequirements, item => item.Code == "DUPLICATE_POWER_PARTICIPANT_IDENTITY_CONFLICT");
        Assert.Contains(contract.BlockingRequirements, item => item.Code == "DUPLICATE_POWER_CONVERSION_IDENTITY_CONFLICT");
    }

    [Fact]
    public void DuplicateDomainIdentity_IsRejectedByBoundaryGuard()
    {
        var contract = new ElectricalPowerEvidenceV1Contract
        {
            Domains =
            [
                new ElectricalPowerEvidenceDomain { PowerDomainId = "DOMAIN-DUP" },
                new ElectricalPowerEvidenceDomain { PowerDomainId = "DOMAIN-DUP" }
            ]
        };

        Assert.Throws<InvalidDataException>(() => contract.EnsureUniqueStableIdentities());
    }

    [Fact]
    public void EquivalentInputPermutations_ProduceIdenticalPowerEvidenceSerialization()
    {
        var first = PermutationProject(reverse: false);
        var second = PermutationProject(reverse: true);

        var firstJson = JsonSerializer.Serialize(ElectricalPowerEvidenceV1Builder.Build(first));
        var secondJson = JsonSerializer.Serialize(ElectricalPowerEvidenceV1Builder.Build(second));

        Assert.Equal(firstJson, secondJson);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("electrical-power-evidence.v0")]
    [InlineData("lrdu-power-evidence.v1")]
    public void MissingOrUnsupportedPowerEvidenceSchema_IsRejected(string? schemaVersion)
    {
        Assert.Throws<NotSupportedException>(() =>
            ElectricalPowerEvidenceV1Contract.EnsureSupportedSchema(schemaVersion));
    }

    [Fact]
    public void ExactPowerEvidenceSchema_IsAccepted()
    {
        ElectricalPowerEvidenceV1Contract.EnsureSupportedSchema("electrical-power-evidence.v1");
    }

    private static ComponentIR SourceComponent(
        string id,
        string direction,
        string? powerDomainId,
        string voltageText,
        string sourcePinId,
        string? sourcePortDomainId = null)
    {
        var nominalText = voltageText.Contains("...") ? null : new string(voltageText.TakeWhile(character => char.IsDigit(character) || character == '.').ToArray());
        decimal? nominal = decimal.TryParse(nominalText, out var parsed) ? parsed : null;
        var range = voltageText.Contains("18...30", StringComparison.Ordinal)
            ? new NormalizedVoltage { Min = 18m, Max = 30m, Type = "DC" }
            : nominal is decimal value
                ? new NormalizedVoltage { Min = value, Max = value, Type = "DC" }
                : null;
        return new ComponentIR
        {
            Identity = new ComponentIrIdentity { ComponentId = $"DEF-{id}", Manufacturer = "TEST", Model = id },
            Power = new ComponentPower { OperatingVoltage = range },
            Ports =
            [
                new ContractPort
                {
                    PortId = "PWR",
                    PortName = "PWR",
                    SignalType = "Power",
                    Direction = direction,
                    VoltageDomain = voltageText,
                    PowerDomainId = sourcePortDomainId
                }
            ],
            Pins =
            [
                new ContractPin
                {
                    PinId = sourcePinId,
                    PortId = "PWR",
                    PinNumber = "1",
                    PinName = "L+",
                    Function = "L+",
                    SignalType = "Power",
                    Direction = direction,
                    VoltageDomain = voltageText,
                    PowerDomainId = powerDomainId
                }
            ]
        };
    }

    private static Evidence SourceEvidence(string raw) => new()
    {
        SourceType = ComponentSourceType.ManufacturerDatasheet,
        SourceUrl = new Uri("https://example.invalid/product"),
        DocumentUrl = new Uri("https://example.invalid/datasheet.pdf"),
        DocumentHashSha256 = "ABC123",
        PageNumber = 7,
        ExtractionMethod = ExtractionMethod.TableParser,
        RawValue = raw,
        RetrievedAt = EvidenceTime,
        VerificationStatus = VerificationStatus.SingleSource
    };

    private static ComponentInstance BareComponent(string id) => new()
    {
        ComponentInstanceId = id,
        ComponentDefinitionId = $"DEF-{id}",
        TypeKey = "TEST"
    };

    private static DomainPort DomainPortWithPin(
        string portId,
        string portDomainId,
        string pinId,
        string pinDomainId,
        PowerRole role) => new()
    {
        PortId = portId,
        Name = portId,
        PowerDomainId = portDomainId,
        Pins =
        {
            new DomainPin
            {
                PinId = pinId,
                PinNumber = "1",
                PowerDomainId = pinDomainId,
                Power = new PowerCapability { Role = role }
            }
        }
    };

    private static ElectricalProject PermutationProject(bool reverse)
    {
        var a = BareComponent("a");
        a.Ports.Add(DomainPortWithPin("a:port", "D-A", "a:pin", "D-A", PowerRole.Source));
        a.PowerConversions.Add(new PowerConversionEvidence
        {
            ConversionId = "CONV-A",
            ComponentInstanceId = "a",
            InputPowerDomainId = "D-A",
            OutputPowerDomainId = "D-B",
            InputSourcePortIds = reverse ? ["IN-2", "IN-1"] : ["IN-1", "IN-2"],
            OutputSourcePinIds = reverse ? ["OUT-2", "OUT-1"] : ["OUT-1", "OUT-2"]
        });
        var b = BareComponent("b");
        b.Ports.Add(DomainPortWithPin("b:port", "D-B", "b:pin", "D-B", PowerRole.Input));

        var project = new ElectricalProject { ProjectId = "permutation" };
        if (reverse)
        {
            project.Components.Add(b);
            project.Components.Add(a);
        }
        else
        {
            project.Components.Add(a);
            project.Components.Add(b);
        }
        return project;
    }

    private static ElectricalProject NonPowerDirectWireProject()
    {
        var project = new ElectricalProject { ProjectId = "drawing-role-no-conversion" };
        project.Nets.Add(new NetDefinition { NetId = "net-control", Label = "CONTROL" });
        project.Components.Add(NonPowerComponent("left", "left:port", "left:pin"));
        project.Components.Add(NonPowerComponent("right", "right:port", "right:pin"));
        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = "wire-1",
            FromEndpointId = "left:pin",
            ToEndpointId = "right:pin",
            NetId = "net-control",
            Kind = ConnectionKind.Wire
        });
        return project;
    }

    private static ComponentInstance NonPowerComponent(string id, string portId, string pinId) => new()
    {
        ComponentInstanceId = id,
        ComponentDefinitionId = $"def:{id}",
        TypeKey = "TEST",
        Ports =
        {
            new DomainPort
            {
                PortId = portId,
                Name = id,
                Pins =
                {
                    new DomainPin
                    {
                        PinId = pinId,
                        PinNumber = "1",
                        Function = "CONTROL",
                        Status = PinStatus.Normal
                    }
                }
            }
        }
    };

    private static AutocadComponentDrawingRoleEvidence Role(string componentId, ComponentDrawingRole role) => new()
    {
        ComponentInstanceId = componentId,
        Role = role,
        Status = DrawingEvidenceStatus.Confirmed,
        EvidenceSource = "engineer-confirmed"
    };

    private static IReadOnlyList<AutocadConnectionPointBinding> Bindings(params string[] endpointIds) => endpointIds
        .Select(endpointId => new AutocadConnectionPointBinding
        {
            EndpointId = endpointId,
            SymbolKey = $"SYM:{endpointId}",
            ConnectionPointId = "TERM01"
        }).ToArray();
}
