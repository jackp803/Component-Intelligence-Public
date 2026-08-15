using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bom;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Cables;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Validation;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class ElectricalAdvancedTests
{
    [Fact]
    public void ComponentBridge_DoesNotGuessMultiPortPinOwnership()
    {
        var component = new ComponentIR
        {
            Identity = new ComponentIrIdentity { ComponentId = "ifm-o5d100", Manufacturer = "IFM", Model = "O5D100" },
            Ports = new[]
            {
                new ComponentIntelligence.Contracts.ComponentPort { PortId = "X1", PortType = "M12", ConnectorFamily = "M12" },
                new ComponentIntelligence.Contracts.ComponentPort { PortId = "X2", PortType = "M12", ConnectorFamily = "M12" }
            },
            Pins = new[]
            {
                new ComponentIntelligence.Contracts.ComponentPin { PinNumber = "1", Function = "+24V", SignalType = "Power" },
                new ComponentIntelligence.Contracts.ComponentPin { PinNumber = "3", Function = "0V", SignalType = "Power" }
            }
        };

        var instance = new ComponentProjectBridge().CreateInstance(component, "cmp-1", "S01");

        Assert.Equal(3, instance.Ports.Count);
        var unassigned = Assert.Single(instance.Ports, port => port.Name == "UNASSIGNED-PINS");
        Assert.Contains("NEEDS_PORT_MAPPING", unassigned.Capabilities);
        Assert.Equal(2, unassigned.Pins.Count);
        Assert.Equal(GroundReferenceType.PowerReturn, unassigned.Pins.Single(pin => pin.PinNumber == "3").GroundReferenceType);
    }

    [Fact]
    public void ComponentBridge_GenericGndRemainsUnknownGround()
    {
        var component = new ComponentIR
        {
            Identity = new ComponentIrIdentity { ComponentId = "dev", Manufacturer = "Vendor", Model = "X" },
            Pins = new[] { new ComponentIntelligence.Contracts.ComponentPin { PinNumber = "5", Function = "GND" } }
        };

        var instance = new ComponentProjectBridge().CreateInstance(component, "cmp-1");

        Assert.Equal(GroundReferenceType.Unknown, instance.Ports.Single().Pins.Single().GroundReferenceType);
    }

    [Fact]
    public void ProjectHistory_UndoAndRedoRestoreWholeProjectSnapshot()
    {
        var project = NewProject();
        var history = new ProjectMutationHistory();
        history.RecordBeforeMutation(project, "Add net");
        project.Nets.Add(new NetDefinition { NetId = "n1", Label = "54V+", Layer = ElectricalLayer.Power });

        Assert.True(history.TryUndo(project, out var undone, out var undoDescription));
        Assert.Equal("Add net", undoDescription);
        Assert.Empty(undone.Nets);

        Assert.True(history.TryRedo(undone, out var redone, out var redoDescription));
        Assert.Equal("Add net", redoDescription);
        Assert.Single(redone.Nets);
    }

    [Fact]
    public void CablePlanner_HardRejectsMissingTwistedPair_AndRanksExactHybridCableFirst()
    {
        var requirement = new CableRequirement
        {
            RequirementId = "req",
            MinVoltageRating = 60,
            Shielding = RequirementLevel.Required,
            MinTwistedPairCount = 1,
            MaxCableEntries = 2,
            CommunicationStandards = { "RS485" },
            Conductors =
            {
                new ConductorRequirement { RequirementId = "24V", MinAreaMm2 = 0.75, Layer = ElectricalLayer.Power },
                new ConductorRequirement { RequirementId = "0V", MinAreaMm2 = 0.75, Layer = ElectricalLayer.Power },
                new ConductorRequirement { RequirementId = "A", MinAreaMm2 = 0.20, PairGroup = "RS485", PairLevel = RequirementLevel.Required, Layer = ElectricalLayer.Communication },
                new ConductorRequirement { RequirementId = "B", MinAreaMm2 = 0.20, PairGroup = "RS485", PairLevel = RequirementLevel.Required, Layer = ElectricalLayer.Communication }
            }
        };

        var hybrid = Candidate("hybrid", true, true, 4, true, "RS485",
            Core("pwr1", 0.75), Core("pwr2", 0.75), PairCore("a", 0.205, "P1"), PairCore("b", 0.205, "P1"));
        var oversize = Candidate("oversize", false, true, 8, true, "RS485",
            Core("1", 1.5), Core("2", 1.5), PairCore("3", 0.5, "P1"), PairCore("4", 0.5, "P1"), Core("5", 0.5), Core("6", 0.5), Core("7", 0.5), Core("8", 0.5));
        var noPair = Candidate("no-pair", true, true, 4, true, "RS485",
            Core("1", 0.75), Core("2", 0.75), Core("3", 0.25), Core("4", 0.25));

        var solutions = new CablePlanningEngine().FindSolutions(requirement, new[] { oversize, noPair, hybrid });

        Assert.DoesNotContain(
            solutions.Where(solution => solution.SolutionType == CableSolutionType.SingleCable).SelectMany(solution => solution.Members),
            member => member.Definition.CableDefinitionId == "no-pair");
        Assert.Equal("hybrid", solutions[0].Members.Single().Definition.CableDefinitionId);
        Assert.Equal(CableSolutionType.SingleCable, solutions[0].SolutionType);
    }

    [Fact]
    public void CablePlanner_RequiredLogicalPairCannotSplitAcrossDifferentPhysicalPairs()
    {
        var requirement = new CableRequirement
        {
            RequirementId = "rs485",
            Conductors =
            {
                new ConductorRequirement { RequirementId = "A", MinAreaMm2 = 0.20, PairGroup = "RS485", PairLevel = RequirementLevel.Required },
                new ConductorRequirement { RequirementId = "B", MinAreaMm2 = 0.20, PairGroup = "RS485", PairLevel = RequirementLevel.Required }
            }
        };
        var invalid = Candidate("split-pairs", true, true, 4, false, null,
            PairCore("p1-large", 0.25, "P1"), PairCore("p1-small", 0.10, "P1"),
            PairCore("p2-large", 0.25, "P2"), PairCore("p2-small", 0.10, "P2"));

        var solutions = new CablePlanningEngine().FindSolutions(requirement, new[] { invalid });

        Assert.Empty(solutions);
    }

    [Fact]
    public void CablePlanner_CanUseTwoPhysicalCablesWhenConnectorEntryPolicyAllowsIt()
    {
        var requirement = new CableRequirement
        {
            RequirementId = "req",
            Shielding = RequirementLevel.Required,
            MinTwistedPairCount = 1,
            MaxCableEntries = 2,
            CommunicationStandards = { "RS485" },
            Conductors =
            {
                new ConductorRequirement { RequirementId = "24V", MinAreaMm2 = 0.75 },
                new ConductorRequirement { RequirementId = "0V", MinAreaMm2 = 0.75 },
                new ConductorRequirement { RequirementId = "A", MinAreaMm2 = 0.20, PairGroup = "RS485", PairLevel = RequirementLevel.Required },
                new ConductorRequirement { RequirementId = "B", MinAreaMm2 = 0.20, PairGroup = "RS485", PairLevel = RequirementLevel.Required }
            }
        };
        var power = Candidate("power", true, true, 2, false, null, Core("p1", 0.75), Core("p2", 0.75));
        var rs485 = Candidate("rs485", true, true, 2, true, "RS485", PairCore("a", 0.205, "P1"), PairCore("b", 0.205, "P1"));

        var solutions = new CablePlanningEngine().FindSolutions(requirement, new[] { power, rs485 });

        var combination = Assert.Single(solutions, solution => solution.SolutionType == CableSolutionType.MultiCableCombination);
        Assert.Equal(2, combination.Members.Count);
    }

    [Fact]
    public void WireSize_Awg24NormalizesToReferenceAreaWithoutInferringAmpacity()
    {
        var area = WireSize.AwgToAreaMm2(24);
        Assert.InRange(area, 0.204, 0.206);
    }

    [Fact]
    public void DerivedBom_IncludesRealJumperAndCustomCableAssemblyWithProvenance()
    {
        var project = NewProject();
        project.TerminalBlocks.Add(new TerminalBlock
        {
            TerminalBlockId = "tb1",
            ReferenceDesignator = "TB1",
            FunctionTag = "54V+",
            Positions = { Terminal("p1", "TB1:1"), Terminal("p2", "TB1:2") },
            Jumpers =
            {
                new ShortingJumper
                {
                    JumperId = "j1",
                    Manufacturer = "WAGO",
                    PartNumber = "2002-402",
                    ConnectionPointIds = { "cp1", "cp2" }
                }
            }
        });
        project.CableAssemblies.Add(new CableAssembly
        {
            CableAssemblyId = "ca1",
            ReferenceDesignator = "W001",
            IsCustom = true
        });

        var lines = new DerivedBomEngine().Build(project, generatedAt: DateTimeOffset.Parse("2026-08-14T00:00:00Z"));

        var jumper = Assert.Single(lines, line => line.Kind == ElectricalBomItemKind.ShortingJumper);
        Assert.Equal(MaterialResolutionStatus.Resolved, jumper.ResolutionStatus);
        Assert.Equal("DERIVED", jumper.Source);
        Assert.Contains("j1", jumper.SourceObjectIds);
        var cable = Assert.Single(lines, line => line.Kind == ElectricalBomItemKind.CableAssembly);
        Assert.Equal(MaterialResolutionStatus.CustomDefined, cable.ResolutionStatus);
    }

    [Fact]
    public void DerivedBom_CableLengthUsesProvidedMechanicalLengthAndIgnoresRouteGeometry()
    {
        var project = NewProject();
        project.Cables.Add(new CableInstance
        {
            CableInstanceId = "cable1",
            CableDefinitionId = "def1",
            ReferenceDesignator = "W001",
            ProvidedLengthMm = 5000,
            LengthSource = CableLengthSource.Mechanical
        });
        project.CableRoutes.Add(new CableRoute
        {
            CableRouteId = "route1",
            ConnectionOrCableId = "cable1",
            Segments = { new RouteSegment { SegmentId = "s1", StartXMm = 0, StartYMm = 0, EndXMm = 1000, EndYMm = 0 } }
        });
        var definition = new CableDefinition { CableDefinitionId = "def1", Manufacturer = "Vendor", PartNumber = "CABLE-1" };
        var policy = new CableLengthPolicy { PercentageAllowance = 10, FixedAllowanceMm = 100, ServiceLoopMmPerEnd = 200 };

        var line = Assert.Single(
            new DerivedBomEngine().Build(project, new[] { definition }, policy),
            item => item.Kind == ElectricalBomItemKind.CableProduct);

        Assert.Equal(6.0m, decimal.Round(line.Quantity, 3));
        Assert.Equal(MaterialResolutionStatus.Resolved, line.ResolutionStatus);
        Assert.Contains("Mechanical", line.Reason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("route1", line.SourceObjectIds);
    }

    [Fact]
    public void PreExportReview_OutOfScopePinIsVisibleButPreResolved()
    {
        var project = NewProject();
        var component = new ComponentInstance
        {
            ComponentInstanceId = "servo",
            ComponentDefinitionId = "servo-def",
            TypeKey = "SERVO_DRIVE",
            ReferenceDesignator = "DRV01",
            ResponsibilityScope = ResponsibilityScope.OutOfScope,
            Ports =
            {
                new ComponentIntelligence.Electrical.Domain.ComponentPort
                {
                    PortId = "ac",
                    Name = "AC INPUT",
                    Pins = { new ComponentIntelligence.Electrical.Domain.ComponentPin { PinId = "L1", PinNumber = "L1", IsRequired = true, Status = PinStatus.Normal } }
                }
            }
        };
        project.Components.Add(component);

        var item = Assert.Single(new PreExportReviewService().BuildReview(project));

        Assert.Equal(EndpointDisposition.OutOfScope, item.Disposition);
        Assert.True(item.IsResolved);
    }

    private static ElectricalProject NewProject() => new() { ProjectId = Guid.NewGuid().ToString("N") };

    private static CableProductCandidate Candidate(string id, bool approved, bool standard, int coreCount, bool shielded, string? protocol, params CableCoreDefinition[] cores)
    {
        var definition = new CableDefinition
        {
            CableDefinitionId = id,
            CoreCount = coreCount,
            Shielded = shielded,
            VoltageRating = 300
        };
        if (protocol is not null) definition.CommunicationCapabilities.Add(protocol);
        definition.Cores.AddRange(cores);
        return new CableProductCandidate { Definition = definition, ApprovedMaterial = approved, StandardProduct = standard };
    }

    private static CableCoreDefinition Core(string id, double area) => new() { CoreId = id, CoreNumber = id, AreaMm2 = area };
    private static CableCoreDefinition PairCore(string id, double area, string pair) => new() { CoreId = id, CoreNumber = id, AreaMm2 = area, PairGroup = pair };

    private static TerminalPosition Terminal(string id, string label) => new()
    {
        TerminalPositionId = id,
        PositionLabel = label
    };
}
