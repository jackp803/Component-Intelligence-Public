using ComponentIntelligence.Contracts;
using ComponentIntelligence.Repository;
using Xunit;

namespace ComponentIntelligence.Tests.Repository;

public sealed class EngineeringValidatedKnowledgeStoreTests
{
    [Fact]
    public async Task Read_RemovesLegacyThirdPartyPdfTablePins()
    {
        var inner = new FakeStore(ComponentWithPollutedPin());
        var guarded = new EngineeringValidatedKnowledgeStore(inner);

        var result = await guarded.FindByIdentityAsync("OMRON", "F03-20");

        Assert.NotNull(result.Component);
        Assert.Empty(result.Component!.Pins);
        Assert.Contains("CENTRAL_PIN_ENGINEERING_GATE_REJECTED_ON_READ:1", result.Diagnostics);
    }

    [Fact]
    public async Task Write_DoesNotForwardUnreviewedThirdPartyParserPins()
    {
        var inner = new FakeStore(null);
        var guarded = new EngineeringValidatedKnowledgeStore(inner);

        var result = await guarded.UpsertAsync(ComponentWithPollutedPin());

        Assert.True(result.Succeeded);
        Assert.NotNull(inner.LastWritten);
        Assert.Empty(inner.LastWritten!.Pins);
        Assert.Contains("CENTRAL_PIN_ENGINEERING_GATE_REJECTED_ON_WRITE:1", result.Diagnostics);
    }

    private static ComponentIR ComponentWithPollutedPin() => new()
    {
        Identity = new ComponentIrIdentity
        {
            ComponentId = "notion:f03-20",
            Manufacturer = "OMRON",
            Model = "F03-20",
            Mpn = "F03-20"
        },
        Pins =
        [
            new ComponentPin
            {
                PinNumber = "13",
                Function = "13 Adhesive tape",
                SignalType = "Reference",
                Description = "Pin assignment extracted from: PDF table / page 9",
                Evidence =
                [
                    new Evidence
                    {
                        SourceType = ComponentSourceType.TrustedThirdParty,
                        SourceUrl = new Uri("https://datasheet.octopart.com/F0320-Omron-datasheet-21402030.pdf"),
                        DocumentUrl = new Uri("https://datasheet.octopart.com/F0320-Omron-datasheet-21402030.pdf"),
                        ExtractionMethod = ExtractionMethod.UserInput,
                        RawValue = "13 Adhesive tape",
                        RetrievedAt = DateTimeOffset.UtcNow,
                        VerificationStatus = VerificationStatus.SingleSource
                    }
                ]
            }
        ]
    };

    private sealed class FakeStore(ComponentIR? component) : IComponentKnowledgeStore
    {
        public bool IsEnabled => true;
        public ComponentIR? LastWritten { get; private set; }

        public Task<ComponentKnowledgeLookup> FindByIdentityAsync(
            string manufacturer,
            string model,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ComponentKnowledgeLookup(component, ["INNER_READ"]));

        public Task<ComponentKnowledgeWriteResult> UpsertAsync(
            ComponentIR value,
            CancellationToken cancellationToken = default)
        {
            LastWritten = value;
            return Task.FromResult(new ComponentKnowledgeWriteResult(true, ["INNER_WRITE"]));
        }
    }
}
