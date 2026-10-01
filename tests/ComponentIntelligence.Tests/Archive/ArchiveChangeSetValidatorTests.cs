using ComponentIntelligence.Archive;
using Xunit;

namespace ComponentIntelligence.Tests.Archive;

public sealed class ArchiveChangeSetValidatorTests
{
    [Fact]
    public void ValidChangeSet_Passes()
    {
        var report = Validate(ValidChangeSet());
        Assert.Equal(ArchiveValidationStatus.PASS, report.Status);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void MissingIdentity_IsRejected()
    {
        var input = ValidChangeSet() with { Manufacturer = "" };
        var report = Validate(input);
        Assert.Equal(ArchiveValidationStatus.REJECT, report.Status);
        Assert.Contains(report.Issues, issue => issue.RuleId == "ARCHIVE-IDENTITY-001" && issue.Severity == ArchiveValidationSeverity.ERROR);
    }

    [Fact]
    public void DuplicateIds_AreCaseInsensitive()
    {
        var first = ValidComponent("CMP-1");
        var second = ValidComponent("cmp-1");
        var report = Validate(ValidChangeSet() with { Components = new[] { first, second } });
        Assert.Equal(ArchiveValidationStatus.REJECT, report.Status);
        Assert.Contains(report.Issues, issue => issue.RuleId == "ARCHIVE-ID-001");
    }

    [Fact]
    public void MultipleComponentRowsForOneTargetIdentity_AreRejected()
    {
        var first = ValidComponent("CMP-1");
        var second = ValidComponent("CMP-2");

        var report = Validate(ValidChangeSet() with { Components = new[] { first, second } });

        Assert.Equal(ArchiveValidationStatus.REJECT, report.Status);
        Assert.Contains(report.Issues, issue => issue.RuleId == "ARCHIVE-IDENTITY-003");
    }

    [Fact]
    public void BlankPortIdentity_IsRejected()
    {
        var port = ValidPort("", "CMP-1", Array.Empty<ArchivePinChange>()) with { PortName = " " };
        var component = ValidComponent("CMP-1") with { Ports = new[] { port } };

        var report = Validate(ValidChangeSet() with { Components = new[] { component } });

        Assert.Equal(ArchiveValidationStatus.REJECT, report.Status);
        Assert.Contains(report.Issues, issue => issue.RuleId == "ARCHIVE-PORT-001");
    }

    [Fact]
    public void BlankPinIdentity_IsRejected()
    {
        var pin = ValidPin("", "PORT-1", " ");
        var component = ValidComponent("CMP-1") with
        {
            Ports = new[] { ValidPort("PORT-1", "CMP-1", new[] { pin }) }
        };

        var report = Validate(ValidChangeSet() with { Components = new[] { component } });

        Assert.Equal(ArchiveValidationStatus.REJECT, report.Status);
        Assert.Contains(report.Issues, issue => issue.RuleId == "ARCHIVE-PIN-003");
    }

    [Fact]
    public void DeclaredActualPinCount_MustMatchPinRows_EvenWhenExpectedCountIsUnknown()
    {
        var pin = ValidPin("PIN-1", "PORT-1", "1");
        var port = ValidPort("PORT-1", "CMP-1", new[] { pin }) with { PinCount = null, ActualPinCount = 2 };
        var component = ValidComponent("CMP-1") with { Ports = new[] { port } };

        var report = Validate(ValidChangeSet() with { Components = new[] { component } });

        Assert.Equal(ArchiveValidationStatus.REJECT, report.Status);
        Assert.Contains(report.Issues, issue => issue.RuleId == "ARCHIVE-PIN-004");
    }

    [Fact]
    public void PortAndPinOwnership_MustMatchParents()
    {
        var pin = ValidPin("PIN-1", "WRONG-PORT", "1");
        var port = ValidPort("PORT-1", "WRONG-COMPONENT", new[] { pin });
        var component = ValidComponent("CMP-1") with { Ports = new[] { port } };

        var report = Validate(ValidChangeSet() with { Components = new[] { component } });

        Assert.Equal(ArchiveValidationStatus.REJECT, report.Status);
        Assert.Contains(report.Issues, issue => issue.RuleId == "ARCHIVE-OWNER-001");
        Assert.Contains(report.Issues, issue => issue.RuleId == "ARCHIVE-OWNER-002");
    }

    [Fact]
    public void KnownPinCount_MustEqualPhysicalPinRows_AndBlocksReady()
    {
        var pins = Enumerable.Range(1, 4).Select(number => ValidPin($"PIN-{number}", "PORT-1", number.ToString())).ToArray();
        var port = ValidPort("PORT-1", "CMP-1", pins) with { PinCount = 5, ActualPinCount = 4 };
        var component = ValidComponent("CMP-1") with { TopologyStatus = ArchiveTopologyStatus.Ready, Ports = new[] { port } };

        var report = Validate(ValidChangeSet() with { Components = new[] { component } });

        Assert.Equal(ArchiveValidationStatus.REJECT, report.Status);
        Assert.Contains(report.Issues, issue => issue.RuleId == "ARCHIVE-PIN-001");
        Assert.Contains(report.Issues, issue => issue.RuleId == "ARCHIVE-READY-001");
    }

    [Fact]
    public void DuplicatePinNumbersWithinPort_AreRejected()
    {
        var pins = new[]
        {
            ValidPin("PIN-A", "PORT-1", "1"),
            ValidPin("PIN-B", "PORT-1", "1")
        };
        var component = ValidComponent("CMP-1") with { Ports = new[] { ValidPort("PORT-1", "CMP-1", pins) } };

        var report = Validate(ValidChangeSet() with { Components = new[] { component } });

        Assert.Equal(ArchiveValidationStatus.REJECT, report.Status);
        Assert.Contains(report.Issues, issue => issue.RuleId == "ARCHIVE-PIN-002");
    }

    [Theory]
    [InlineData(ArchivePinStatus.NC)]
    [InlineData(ArchivePinStatus.Reserved)]
    public void NcAndReservedWithoutEvidence_RequireReview(ArchivePinStatus status)
    {
        var pin = ValidPin("PIN-1", "PORT-1", "1") with { PinStatus = status, Evidence = Array.Empty<ArchiveEvidenceReference>() };
        var component = ValidComponent("CMP-1") with { Ports = new[] { ValidPort("PORT-1", "CMP-1", new[] { pin }) } };

        var report = Validate(ValidChangeSet() with { Components = new[] { component } });

        Assert.Equal(ArchiveValidationStatus.REVIEW_REQUIRED, report.Status);
        Assert.Contains(report.Issues, issue => issue.RuleId == "ARCHIVE-EVIDENCE-001" && issue.Severity == ArchiveValidationSeverity.REVIEW);
    }

    [Theory]
    [InlineData(@"C:\temp\datasheet.pdf")]
    [InlineData(@"D:\Drive\drawing.pdf")]
    [InlineData("../secret.pdf")]
    public void RootedOrEscapingArchivePaths_AreRejected(string path)
    {
        var component = ValidComponent("CMP-1") with { DatasheetPath = path };
        var report = Validate(ValidChangeSet() with { Components = new[] { component } });
        Assert.Equal(ArchiveValidationStatus.REJECT, report.Status);
        Assert.Contains(report.Issues, issue => issue.RuleId == "ARCHIVE-PATH-001");
    }

    [Fact]
    public void ValidDocumentsRelativePath_Passes()
    {
        var component = ValidComponent("CMP-1") with { DatasheetPath = "Documents/TEST/VALID/datasheet.pdf" };
        var report = Validate(ValidChangeSet() with { Components = new[] { component } });
        Assert.Equal(ArchiveValidationStatus.PASS, report.Status);
    }

    [Fact]
    public void CreateWithExistingComponent_IsRejected()
    {
        var report = Validate(ValidChangeSet() with { Operation = ArchiveOperation.CREATE, ExistingComponentId = "CMP-OLD" });
        Assert.Equal(ArchiveValidationStatus.REJECT, report.Status);
        Assert.Contains(report.Issues, issue => issue.RuleId == "ARCHIVE-OP-001");
    }

    [Fact]
    public void StableIdMutation_IsRejected()
    {
        var pin = ValidPin("PIN-NEW", "PORT-NEW", "1") with { PriorStablePinId = "PIN-OLD" };
        var port = ValidPort("PORT-NEW", "CMP-NEW", new[] { pin }) with { PriorStablePortId = "PORT-OLD" };
        var component = ValidComponent("CMP-NEW") with
        {
            PriorStableComponentId = "CMP-OLD",
            Ports = new[] { port }
        };

        var report = Validate(ValidChangeSet() with { Operation = ArchiveOperation.UPDATE, Components = new[] { component } });

        Assert.Equal(ArchiveValidationStatus.REJECT, report.Status);
        Assert.Contains(report.Issues, issue => issue.RuleId == "ARCHIVE-STABLE-ID-001");
        Assert.Contains(report.Issues, issue => issue.RuleId == "ARCHIVE-STABLE-ID-002");
        Assert.Contains(report.Issues, issue => issue.RuleId == "ARCHIVE-STABLE-ID-003");
    }

    internal static ArchiveChangeSet ValidChangeSet() => new()
    {
        JobId = "JOB-VALID",
        PolicyVersion = "v2",
        PolicyRevision = "test",
        Operation = ArchiveOperation.CREATE,
        Manufacturer = "TEST",
        Model = "VALID",
        Components = new[] { ValidComponent("CMP-1") }
    };

    internal static ArchiveComponentChange ValidComponent(string id) => new()
    {
        ComponentId = id,
        Manufacturer = "TEST",
        Model = "VALID",
        TopologyStatus = ArchiveTopologyStatus.Review,
        LayoutStatus = ArchiveLayoutStatus.Review,
        Ports = Array.Empty<ArchivePortChange>()
    };

    internal static ArchivePortChange ValidPort(string id, string componentId, IReadOnlyList<ArchivePinChange> pins) => new()
    {
        PortId = id,
        ComponentId = componentId,
        PortName = id,
        Direction = "Mixed",
        TopologyEndpointMode = ArchiveTopologyEndpointMode.Pins,
        PinCount = pins.Count,
        ActualPinCount = pins.Count,
        Pins = pins
    };

    internal static ArchivePinChange ValidPin(string id, string portId, string number) => new()
    {
        PinId = id,
        PortId = portId,
        PinNumber = number,
        PinStatus = ArchivePinStatus.Used
    };

    private static ArchiveValidationReport Validate(ArchiveChangeSet changeSet)
    {
        var type = typeof(ArchiveChangeSet).Assembly.GetType("ComponentIntelligence.Archive.ArchiveChangeSetValidator");
        Assert.NotNull(type);
        var instance = Activator.CreateInstance(type!);
        Assert.NotNull(instance);
        var method = type!.GetMethod("Validate");
        Assert.NotNull(method);
        return Assert.IsType<ArchiveValidationReport>(method!.Invoke(instance, new object[] { changeSet }));
    }
}
