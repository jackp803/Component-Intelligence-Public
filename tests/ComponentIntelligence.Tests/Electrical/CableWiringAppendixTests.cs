using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class CableWiringAppendixTests
{
    [Fact]
    public void RepeatedBodiesOnDifferentPagesLinkToOnePhysicalCableSection()
    {
        var project = Project("first", "second");
        project.Cables.Add(Cable("cable"));
        project.Schematic!.Symbols.AddRange([Body("first-body", "first", "cable"), Body("second-body", "second", "cable")]);

        var plan = CableWiringAppendixPlan.Build(project);

        Assert.Equal(new[] { "first", "second" }, plan.MainPages.Select(p => p.PageId));
        var section = Assert.Single(plan.Sections);
        Assert.Equal("cable", section.CableInstanceId);
        Assert.Empty(section.ExistingDetailPages);
        Assert.Equal(new[] { "first", "second" }, section.SourceLinks.Select(l => l.SourcePageId));
    }

    [Fact]
    public void UnknownEmptyTopologyStillProducesDistinctPhysicalSections()
    {
        var project = Project("main");
        project.Cables.AddRange([Cable("cable-a"), Cable("cable-b")]);
        foreach (var cable in project.Cables) cable.ArchivedCable = cable.ArchivedCable! with { Mapping = [] };
        project.Schematic!.Symbols.AddRange([Body("a", "main", "cable-a"), Body("b", "main", "cable-b")]);

        var plan = CableWiringAppendixPlan.Build(project);

        Assert.Equal(new[] { "cable-a", "cable-b" }, plan.Sections.Select(s => s.CableInstanceId));
        Assert.All(plan.Sections, s => Assert.Single(s.SourceLinks));
        Assert.Equal(project.Cables[0].ArchivedCable!.Template.AssetSha256, project.Cables[1].ArchivedCable!.Template.AssetSha256);
    }

    [Fact]
    public void UnrepresentedArchiveEntriesDoNotCreateAppendixSections()
    {
        var project = Project("main");
        project.Cables.Add(Cable("unrepresented"));

        var plan = CableWiringAppendixPlan.Build(project);

        Assert.Empty(plan.Sections);
        Assert.Equal("main", Assert.Single(plan.MainPages).PageId);
    }

    [Fact]
    public void DedicatedDetailsMoveAfterMainSheetsInTheirOriginalRelativeOrder()
    {
        var project = Project("detail-1", "main-1", "detail-2", "main-2");
        project.Cables.Add(Cable("cable"));
        SetDetail(project, "detail-1", "cable");
        SetDetail(project, "detail-2", "cable");

        var plan = CableWiringAppendixPlan.Build(project);

        Assert.Equal(new[] { "main-1", "main-2" }, plan.MainPages.Select(p => p.PageId));
        Assert.Equal(new[] { "main-1", "main-2" }, plan.MainProject.Schematic!.Pages.Select(p => p.PageId));
        var section = Assert.Single(plan.Sections);
        Assert.Equal("cable", section.CableInstanceId);
        Assert.Equal(new[] { "detail-1", "detail-2" }, section.ExistingDetailPages.Select(p => p.PageId));
        Assert.Equal(new[] { "main-1", "main-2", "detail-1", "detail-2" },
            plan.MainPages.Concat(section.ExistingDetailPages).Select(p => p.PageId));
    }

    [Theory]
    [InlineData("component")]
    [InlineData("cable-body")]
    [InlineData("wire")]
    [InlineData("cable-wire")]
    [InlineData("inline-detail")]
    public void DedicatedBindingWithOtherContentRemainsAMainSheet(string content)
    {
        var project = Project("mixed", "main");
        project.Cables.Add(Cable("cable"));
        SetDetail(project, "mixed", "cable");
        switch (content)
        {
            case "component":
                project.Schematic!.Symbols.Add(new() { SymbolId = "component-body", ComponentInstanceId = "component", PageId = "mixed" });
                break;
            case "cable-body":
                project.Schematic!.Symbols.Add(Body("body", "mixed", "cable"));
                break;
            case "wire":
                project.Schematic!.Wires.Add(new() { WireId = "ordinary-wire", PageId = "mixed", Points = [new(20, 20), new(80, 20)] });
                break;
            case "cable-wire":
                AddWire(project, "mixed", "explicit-wire", "cable", [new(20, 20), new(80, 20)]);
                break;
            case "inline-detail":
                project.Schematic!.Pages[0].InlineCableDetails.Add(new("cable", null) { DetailId = "inline" });
                break;
        }
        var before = JsonSerializer.Serialize(project.Schematic!.Pages[0]);

        var plan = CableWiringAppendixPlan.Build(project);

        Assert.Equal(new[] { "mixed", "main" }, plan.MainPages.Select(p => p.PageId));
        Assert.Empty(Assert.Single(plan.Sections).ExistingDetailPages);
        Assert.Equal(before, JsonSerializer.Serialize(plan.MainPages[0]));
    }

    [Fact]
    public void InlineArchivedDetailRepresentsCableWithoutMovingItsEngineeringSheet()
    {
        var project = Project("main");
        project.Cables.Add(Cable("cable"));
        project.Schematic!.Pages[0].InlineCableDetails.Add(new("cable", null) { DetailId = "inline" });

        var plan = CableWiringAppendixPlan.Build(project);

        Assert.Equal("cable", Assert.Single(plan.Sections).CableInstanceId);
        Assert.Equal("main", Assert.Single(plan.MainPages).PageId);
        Assert.Single(plan.MainPages[0].InlineCableDetails);
        Assert.NotEmpty(plan.Sections[0].SourceLinks);
        Assert.All(plan.Sections[0].SourceLinks, link => { Assert.Equal("main", link.SourcePageId); AssertFinitePositive(link); });
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("no-ports")]
    [InlineData("no-pins")]
    public void MissingArchivedRuntimePinInventoryIsExcludedWithoutInventingDetails(string inventory)
    {
        var project = Project("main", "detail");
        var cable = Cable("cable");
        if (inventory == "ordinary") cable.ArchivedCable = null;
        else if (inventory == "no-ports") cable.ArchivedCable!.Ports.Clear();
        else cable.ArchivedCable!.Ports[0].Pins.Clear();
        project.Cables.Add(cable);
        project.Schematic!.Symbols.Add(Body("body", "main", "cable"));
        AddWire(project, "main", "wire", "cable", [new(20, 20), new(80, 20)]);
        SetDetail(project, "detail", "cable");
        var before = JsonSerializer.Serialize(project);

        var plan = CableWiringAppendixPlan.Build(project);

        Assert.Empty(plan.Sections);
        Assert.Equal(new[] { "main", "detail" }, plan.MainPages.Select(p => p.PageId));
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void ExportSnapshotPreservesCurrentInstanceOverrideAndLeavesSerializedInputUnchanged()
    {
        var project = Project("detail", "main");
        var cable = Cable("cable");
        cable.ArchivedCable = cable.ArchivedCable! with
        {
            Mapping = [], MappingConfirmed = false, HasMappingOverride = true
        };
        project.Cables.Add(cable);
        SetDetail(project, "detail", "cable");
        project.Schematic!.Symbols.Add(Body("body", "main", "cable"));
        project.Schematic.WireNamingReferences.Add("wire", "W-01");
        project.Schematic.UsedWireDesignations.Add("W-01");
        var before = JsonSerializer.Serialize(project);

        var plan = CableWiringAppendixPlan.Build(project);

        Assert.Equal(before, JsonSerializer.Serialize(project));
        Assert.NotSame(project, plan.MainProject);
        Assert.NotSame(project.Schematic, plan.MainProject.Schematic);
        Assert.NotSame(project.Schematic.Pages[1], plan.MainPages[0]);
        Assert.NotSame(project.Schematic.Pages[0], Assert.Single(plan.Sections[0].ExistingDetailPages));
        var exported = Assert.Single(plan.MainProject.Cables);
        Assert.True(exported.ArchivedCable!.HasMappingOverride);
        Assert.False(exported.ArchivedCable.MappingConfirmed);
        Assert.Empty(exported.ArchivedCable.Mapping);
        Assert.Single(exported.ArchivedCable.Template.Mapping);
        Assert.Equal("W-01", plan.MainProject.Schematic!.WireNamingReferences["wire"]);
        Assert.Equal("W-01", Assert.Single(plan.MainProject.Schematic.UsedWireDesignations));
        exported.ArchivedCable.Ports[0].Pins[0].PinNumber = "999";
        exported.ArchivedCable.Template.Ports[0].Pins[0].PinNumber = "888";
        plan.MainProject.Schematic.Symbols[0].Anchors.Add(new() { EndpointId = "export-only" });
        plan.Sections[0].ExistingDetailPages[0].InlineCableDetails.Add(new("cable", null));
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void ContinuationsResolveByPreservedPageIdentitiesAfterFinalExportOrdering()
    {
        var project = Project("detail", "main-1", "main-2");
        project.Cables.Add(Cable("cable"));
        SetDetail(project, "detail", "cable");
        project.Schematic!.Continuations.Add(new()
        {
            ContinuationId = "continuation", Signal = "signal",
            Source = new() { MarkerId = "source", PageId = "main-1", Position = new(100, 70) },
            Destination = new() { MarkerId = "destination", PageId = "detail", Position = new(80, 60) }
        });
        var before = JsonSerializer.Serialize(project);
        var originalContinuation = JsonSerializer.Serialize(project.Schematic.Continuations);

        var plan = CableWiringAppendixPlan.Build(project);

        Assert.Equal(originalContinuation, JsonSerializer.Serialize(plan.MainProject.Schematic!.Continuations));
        plan.MainProject.Schematic.Pages.AddRange(plan.Sections.SelectMany(s => s.ExistingDetailPages));
        Assert.Equal(new[] { "main-1", "main-2", "detail" }, plan.MainProject.Schematic.Pages.Select(p => p.PageId));
        var forward = Assert.Single(SchematicContinuationNavigation.Links(plan.MainProject, "main-1"));
        Assert.Equal(2, forward.DestinationPageIndex);
        Assert.Equal(new SchematicPoint(80, 60), forward.Destination);
        var reverse = Assert.Single(SchematicContinuationNavigation.Links(plan.MainProject, "detail"));
        Assert.Equal(0, reverse.DestinationPageIndex);
        Assert.Equal(new SchematicPoint(100, 70), reverse.Destination);
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Theory]
    [InlineData(0, 40, 20, 40, 40)]
    [InlineData(90, 20, 40, 30, 50)]
    [InlineData(180, 40, 20, 40, 40)]
    [InlineData(270, 20, 40, 30, 50)]
    public void BodyLinkBoundsAndReturnPositionFollowOrthogonalRotation(int rotation,
        double width, double height, double returnX, double returnY)
    {
        var project = Project("main");
        project.Cables.Add(Cable("cable"));
        project.Schematic!.Symbols.Add(Body("body", "main", "cable") with { Rotation = rotation });

        var link = Assert.Single(Assert.Single(CableWiringAppendixPlan.Build(project).Sections).SourceLinks);

        Assert.Equal("main", link.SourcePageId);
        Assert.Equal(new SchematicPoint(20, 30), link.TopLeft);
        Assert.Equal(width, link.Width);
        Assert.Equal(height, link.Height);
        Assert.Equal(new SchematicPoint(returnX, returnY), link.ReturnPosition);
        AssertFinitePositive(link);
    }

    [Fact]
    public void ExplicitCableWireSegmentsUseActualPolylineGeometryAndIgnoreOtherWires()
    {
        var project = Project("main");
        project.Cables.AddRange([Cable("cable"), Cable("unrepresented")]);
        AddWire(project, "main", "linked", "cable",
            [new(12, 18), new(62, 18), new(62, 18), new(62, 78), new(92, 108)]);
        project.Schematic!.Wires.Add(new()
        {
            WireId = "unassociated", PageId = "main", Points = [new(12, 18), new(62, 18)],
            Start = SchematicAttachment.Pin("cable-body", "cable-a-1")
        });
        AddWire(project, "main", "ordinary", null, [new(12, 18), new(62, 18)]);
        project.Connections.Add(new() { ConnectionId = "not-drawn", FromEndpointId = "a", ToEndpointId = "b", CableInstanceId = "unrepresented" });

        var section = Assert.Single(CableWiringAppendixPlan.Build(project).Sections);

        Assert.Equal("cable", section.CableInstanceId);
        Assert.Equal(3, section.SourceLinks.Count);
        Assert.Equal(new SchematicPoint(11, 17), section.SourceLinks[0].TopLeft);
        Assert.Equal(52, section.SourceLinks[0].Width);
        Assert.Equal(2, section.SourceLinks[0].Height);
        Assert.Equal(new SchematicPoint(37, 18), section.SourceLinks[0].ReturnPosition);
        Assert.Equal(new SchematicPoint(61, 17), section.SourceLinks[1].TopLeft);
        Assert.Equal(2, section.SourceLinks[1].Width);
        Assert.Equal(62, section.SourceLinks[1].Height);
        Assert.Equal(new SchematicPoint(62, 48), section.SourceLinks[1].ReturnPosition);
        Assert.Equal(new SchematicPoint(61, 77), section.SourceLinks[2].TopLeft);
        Assert.Equal(32, section.SourceLinks[2].Width);
        Assert.Equal(32, section.SourceLinks[2].Height);
        Assert.Equal(new SchematicPoint(77, 93), section.SourceLinks[2].ReturnPosition);
        Assert.All(section.SourceLinks, AssertFinitePositive);
    }

    [Fact]
    public void OverlappingEquivalentWiresShareDiagramWithoutMergingPhysicalInstances()
    {
        var project = Project("main");
        project.Cables.AddRange([Cable("cable-a"), Cable("cable-b")]);
        AddWire(project, "main", "wire-a", "cable-a", [new(10, 20), new(40, 20)]);
        AddWire(project, "main", "wire-b", "cable-b", [new(10, 20), new(40, 20)]);

        var plan = CableWiringAppendixPlan.Build(project);

        var section = Assert.Single(plan.Sections);
        Assert.Equal(new[] { "cable-a", "cable-b" }, section.CableInstanceIds);
        Assert.Equal(2, section.SourceLinks.Count);
        Assert.Equal(2, plan.MainProject.Cables.Count);
        Assert.Equal(2, plan.MainProject.Connections.Count);
    }

    [Fact]
    public void DuplicatePageIdsAreRejectedInsteadOfCreatingAmbiguousDestinations()
    {
        var project = Project("same", "same");

        var error = Assert.Throws<InvalidOperationException>(() => CableWiringAppendixPlan.Build(project));

        Assert.Contains("same", error.Message);
    }

    [Theory]
    [InlineData("body")]
    [InlineData("wire")]
    [InlineData("source")]
    [InlineData("destination")]
    public void MissingSourcePageIdentitiesAreReportedBeforeExport(string source)
    {
        var project = Project("main");
        project.Cables.Add(Cable("cable"));
        if (source == "body") project.Schematic!.Symbols.Add(Body("body", "missing", "cable"));
        else if (source == "wire") AddWire(project, "missing", "wire", "cable", [new(10, 20), new(40, 20)]);
        else project.Schematic!.Continuations.Add(new()
        {
            ContinuationId = "continuation", Signal = "signal",
            Source = new() { MarkerId = "source", PageId = source == "source" ? "missing" : "main", Position = new(20, 20) },
            Destination = new() { MarkerId = "destination", PageId = source == "destination" ? "missing" : "main", Position = new(40, 40) }
        });

        var error = Assert.Throws<InvalidOperationException>(() => CableWiringAppendixPlan.Build(project));

        Assert.Contains("missing", error.Message);
    }

    [Theory]
    [InlineData("cable")]
    [InlineData("connection")]
    public void DuplicatePhysicalOrConnectionIdentitiesAreRejected(string identity)
    {
        var project = Project("main");
        project.Cables.Add(Cable("cable"));
        if (identity == "cable") project.Cables.Add(Cable("cable"));
        else
        {
            AddWire(project, "main", "wire", "cable", [new(10, 20), new(40, 20)]);
            project.Connections.Add(new() { ConnectionId = "wire", FromEndpointId = "a", ToEndpointId = "b" });
        }

        Assert.Throws<InvalidOperationException>(() => CableWiringAppendixPlan.Build(project));
    }

    [Theory]
    [InlineData("zero-width")]
    [InlineData("invalid-rotation")]
    [InlineData("nonfinite-body")]
    [InlineData("nonfinite-wire")]
    public void InvalidParticipatingGeometryCannotYieldUnusableLinkRectangles(string geometry)
    {
        var project = Project("main");
        project.Cables.Add(Cable("cable"));
        if (geometry == "nonfinite-wire") AddWire(project, "main", "wire", "cable", [new(10, 20), new(double.NaN, 20)]);
        else project.Schematic!.Symbols.Add(Body("body", "main", "cable") with
        {
            Width = geometry == "zero-width" ? 0 : 40,
            Rotation = geometry == "invalid-rotation" ? 45 : 0,
            Position = geometry == "nonfinite-body" ? new(double.PositiveInfinity, 20) : new(20, 30)
        });

        Assert.Throws<InvalidOperationException>(() => CableWiringAppendixPlan.Build(project));
    }

    [Fact]
    public void MissingSchematicProducesAnIsolatedEmptyPlan()
    {
        var project = new ElectricalProject { ProjectId = "empty", Cables = [Cable("not-drawn")] };

        var plan = CableWiringAppendixPlan.Build(project);

        Assert.Empty(plan.MainPages);
        Assert.Empty(plan.Sections);
        Assert.NotSame(project, plan.MainProject);
        Assert.Null(plan.MainProject.Schematic);
    }

    private static void AssertFinitePositive(CableWiringAppendixSourceLink link)
    {
        Assert.True(double.IsFinite(link.TopLeft.X));
        Assert.True(double.IsFinite(link.TopLeft.Y));
        Assert.True(double.IsFinite(link.Width) && link.Width > 0);
        Assert.True(double.IsFinite(link.Height) && link.Height > 0);
        Assert.True(double.IsFinite(link.ReturnPosition.X));
        Assert.True(double.IsFinite(link.ReturnPosition.Y));
    }

    private static ElectricalProject Project(params string[] pages) => new()
    {
        ProjectId = "appendix-test", Name = "Synthetic appendix",
        Schematic = new() { Pages = pages.Select(id => new SchematicPage { PageId = id, Title = id }).ToList() }
    };

    private static CableInstance Cable(string id)
    {
        var template = new ArchivedCableTemplate
        {
            TemplateId = "shared-template", TemplateRevision = "saved-revision", AssetSha256 = new('a', 64),
            Ports = [SourcePort("a"), SourcePort("b")],
            Mapping = [new("a-1", "b-1")]
        };
        return new()
        {
            CableInstanceId = id, CableDefinitionId = template.TemplateId,
            ArchivedCable = new()
            {
                Template = template,
                Ports = template.Ports.Select(p => new ComponentPort
                {
                    PortId = id + "-" + p.PortId, SourcePortId = p.PortId, Name = p.Name,
                    Pins = p.Pins.Select(pin => new ComponentPin
                    {
                        PinId = id + "-" + pin.PinId, SourcePinId = pin.PinId, PinNumber = pin.PinNumber
                    }).ToList()
                }).ToList(),
                Mapping = [new("a-1", "b-1")]
            }
        };
    }

    private static ComponentPort SourcePort(string id) => new()
    {
        PortId = id, Name = id, Pins = [new() { PinId = id + "-1", PinNumber = "1" }, new() { PinId = id + "-10", PinNumber = "10" }]
    };

    private static SchematicSymbol Body(string symbolId, string pageId, string cableId) => new()
    {
        SymbolId = symbolId, PageId = pageId, CableInstanceId = cableId,
        Position = new(20, 30), Width = 40, Height = 20, ManualSize = true
    };

    private static void SetDetail(ElectricalProject project, string pageId, string cableId)
    {
        var pages = project.Schematic!.Pages;
        var index = pages.FindIndex(p => p.PageId == pageId);
        pages[index] = pages[index] with { CableDetail = new(cableId, null) { DetailId = "detail-" + pageId } };
    }

    private static void AddWire(ElectricalProject project, string pageId, string wireId, string? cableId,
        List<SchematicPoint> points)
    {
        project.Connections.Add(new()
        {
            ConnectionId = wireId, FromEndpointId = "a", ToEndpointId = "b", CableInstanceId = cableId,
            Kind = cableId is null ? ConnectionKind.Wire : ConnectionKind.Cable
        });
        project.Schematic!.Wires.Add(new() { WireId = wireId, PageId = pageId, ConnectionId = wireId, Points = points });
    }
}
