using System.Reflection;
using ComponentIntelligence.Electrical.PowerTopology;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical.PowerTopology;

public sealed class PowerTopologyAnalyzerTests
{
    private readonly PowerTopologyAnalyzer _analyzer = new();

    [Fact]
    public void PwrDirect001_DirectSourceDomainConsumer_IsAccepted()
    {
        var result = _analyzer.Analyze(Input(
            domains: ["A"],
            producers: [("P1", "A")],
            consumers: [("C1", "A")]));

        Assert.Equal(PowerTopologyAnalysisStatus.Accepted, result.Status);
        Assert.Equal(["A"], result.DomainIds);
        Assert.Empty(result.ConversionTopologicalOrder);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void PwrFanout002_OneProducerMultipleConsumers_IsAccepted()
    {
        var result = _analyzer.Analyze(Input(
            domains: ["A"],
            producers: [("P1", "A")],
            consumers: [("C3", "A"), ("C1", "A"), ("C2", "A")]));

        Assert.Equal(PowerTopologyAnalysisStatus.Accepted, result.Status);
        Assert.Equal(["C1", "C2", "C3"], result.Consumers.Select(item => item.ConsumerId));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void PwrConv003_MultiLevelConversion_HasCanonicalOrder()
    {
        var result = _analyzer.Analyze(Input(
            domains: ["C", "A", "B"],
            producers: [("P1", "A")],
            consumers: [("LOAD", "C")],
            conversions: [("Y", "B", "C"), ("X", "A", "B")]));

        Assert.Equal(PowerTopologyAnalysisStatus.Accepted, result.Status);
        Assert.Equal(["A", "B", "C"], result.DomainIds);
        Assert.Equal(["X", "Y"], result.ConversionTopologicalOrder);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void PwrOrder004_AllCollectionPermutations_HaveIdenticalLogicalResult()
    {
        var domains = new[] { Domain("A"), Domain("B"), Domain("C"), Domain("D") };
        var producers = new[] { Producer("P-A", "A"), Producer("P-D", "D") };
        var consumers = new[] { Consumer("C-C", "C"), Consumer("C-D", "D") };
        var conversions = new[] { Conversion("X", "A", "B"), Conversion("Y", "B", "C") };
        var baseline = Fingerprint(_analyzer.Analyze(new PowerTopologyInput
        {
            Domains = domains,
            Producers = producers,
            Consumers = consumers,
            Conversions = conversions
        }));

        foreach (var domainPermutation in Permutations(domains))
        foreach (var producerPermutation in Permutations(producers))
        foreach (var consumerPermutation in Permutations(consumers))
        foreach (var conversionPermutation in Permutations(conversions))
        {
            var candidate = _analyzer.Analyze(new PowerTopologyInput
            {
                Domains = domainPermutation,
                Producers = producerPermutation,
                Consumers = consumerPermutation,
                Conversions = conversionPermutation
            });
            Assert.Equal(baseline, Fingerprint(candidate));
        }
    }

    [Fact]
    public void PwrMissingSource005_MissingProducer_IsBlockedDeterministically()
    {
        var result = _analyzer.Analyze(Input(
            domains: ["A"],
            consumers: [("C1", "A")]));

        Assert.Equal(PowerTopologyAnalysisStatus.Blocked, result.Status);
        Assert.Contains(result.Diagnostics, item => item.Code == "PWR-MISSING-PRODUCER" && item.SubjectId == "DOMAIN:A");
        Assert.Contains(result.Diagnostics, item => item.Code == "PWR-UNREACHABLE-CONSUMER" && item.SubjectId == "CONSUMER:C1");
    }

    [Fact]
    public void PwrMissingCoverage006_DeclaredConversionOutputWithoutReachableInput_BlocksConsumerCoverage()
    {
        var result = _analyzer.Analyze(Input(
            domains: ["A", "B", "C"],
            producers: [("P1", "A")],
            consumers: [("C1", "C")],
            conversions: [("Y", "B", "C")]));

        Assert.Equal(PowerTopologyAnalysisStatus.Blocked, result.Status);
        Assert.Contains(result.Diagnostics, item => item.Code == "PWR-MISSING-PRODUCER" && item.SubjectId == "DOMAIN:B");
        Assert.Contains(result.Diagnostics, item => item.Code == "PWR-ORPHAN-CONVERSION" && item.SubjectId == "CONVERSION:Y");
        Assert.Contains(result.Diagnostics, item => item.Code == "PWR-UNREACHABLE-CONSUMER" && item.SubjectId == "CONSUMER:C1");
    }

    [Fact]
    public void PwrOrphanConv007_UnreachableConversionInput_IsBlocked()
    {
        var result = _analyzer.Analyze(Input(
            domains: ["A", "B"],
            conversions: [("X", "A", "B")]));

        Assert.Equal(PowerTopologyAnalysisStatus.Blocked, result.Status);
        Assert.Contains(result.Diagnostics, item => item.Code == "PWR-MISSING-PRODUCER" && item.SubjectId == "DOMAIN:A");
        Assert.Contains(result.Diagnostics, item => item.Code == "PWR-ORPHAN-CONVERSION" && item.SubjectId == "CONVERSION:X");
    }

    [Fact]
    public void PwrDupProducer008_DirectAndConversionProducersCannotShareDomain()
    {
        var result = _analyzer.Analyze(Input(
            domains: ["A", "B"],
            producers: [("P-A", "A"), ("P-B", "B")],
            consumers: [("C-B", "B")],
            conversions: [("X", "A", "B")]));

        Assert.Equal(PowerTopologyAnalysisStatus.Blocked, result.Status);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "PWR-DUPLICATE-PRODUCER");
        Assert.Equal("DOMAIN:B", diagnostic.SubjectId);
        Assert.Contains("CONVERSION:X", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("PRODUCER:P-B", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PwrCycle009_CycleIsDeterministicAndNeverReturnsPartialAcceptedOrder()
    {
        var first = _analyzer.Analyze(Input(
            domains: ["A", "B"],
            conversions: [("Y", "B", "A"), ("X", "A", "B")]));
        var reversed = _analyzer.Analyze(Input(
            domains: ["B", "A"],
            conversions: [("X", "A", "B"), ("Y", "B", "A")]));

        Assert.Equal(PowerTopologyAnalysisStatus.Blocked, first.Status);
        Assert.Empty(first.ConversionTopologicalOrder);
        var cycle = Assert.Single(first.Diagnostics, item => item.Code == "PWR-CYCLE");
        Assert.Equal("CONVERSIONS:X,Y", cycle.SubjectId);
        Assert.Equal(Fingerprint(first), Fingerprint(reversed));
    }

    [Fact]
    public void InvalidEmptyUnstableAndDuplicateStableIdentities_AreRejected()
    {
        var result = _analyzer.Analyze(new PowerTopologyInput
        {
            Domains = [Domain(""), Domain("A"), Domain("A")],
            Producers = [Producer(" P1 ", "A"), Producer("P2", "A"), Producer("P2", "A")],
            Consumers = [Consumer("C1", "A")]
        });

        Assert.Equal(PowerTopologyAnalysisStatus.Blocked, result.Status);
        Assert.Contains(result.Diagnostics, item => item.Code == "PWR-IDENTITY-INVALID" && item.SubjectId.StartsWith("DOMAIN_ID:", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, item => item.Code == "PWR-IDENTITY-INVALID" && item.SubjectId.StartsWith("PRODUCER_ID:", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, item => item.Code == "PWR-IDENTITY-DUPLICATE" && item.SubjectId == "DOMAIN:A");
        Assert.Contains(result.Diagnostics, item => item.Code == "PWR-IDENTITY-DUPLICATE" && item.SubjectId == "PRODUCER:P2");
    }

    [Fact]
    public void ExplicitKernelInputContract_ContainsNoWeakSignalOrDrawingFields()
    {
        var exposedNames = new[]
            {
                typeof(PowerDomainFact),
                typeof(PowerProducerFact),
                typeof(PowerConsumerFact),
                typeof(PowerConversionFact)
            }
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var forbidden in new[]
                 {
                     "FromEndpointId", "ToEndpointId", "Label", "Name", "TypeKey", "Model", "PartNumber",
                     "X", "Y", "Coordinates", "PageId", "DrawingRole", "Voltage"
                 })
            Assert.DoesNotContain(forbidden, exposedNames);
    }

    [Fact]
    public void MissingReferencedDomain_IsBlockedWithoutInventingDomainIdentity()
    {
        var result = _analyzer.Analyze(Input(
            domains: ["A"],
            producers: [("P1", "MISSING")],
            consumers: [("C1", "A")]));

        Assert.Equal(PowerTopologyAnalysisStatus.Blocked, result.Status);
        Assert.Equal(["A"], result.DomainIds);
        Assert.Contains(result.Diagnostics, item => item.Code == "PWR-DOMAIN-NOT-FOUND" && item.SubjectId == "PRODUCER:P1");
        Assert.DoesNotContain("MISSING", result.DomainIds);
    }

    private static PowerTopologyInput Input(
        string[]? domains = null,
        (string Id, string Domain)[]? producers = null,
        (string Id, string Domain)[]? consumers = null,
        (string Id, string Input, string Output)[]? conversions = null) => new()
    {
        Domains = (domains ?? []).Select(Domain).ToArray(),
        Producers = (producers ?? []).Select(item => Producer(item.Id, item.Domain)).ToArray(),
        Consumers = (consumers ?? []).Select(item => Consumer(item.Id, item.Domain)).ToArray(),
        Conversions = (conversions ?? []).Select(item => Conversion(item.Id, item.Input, item.Output)).ToArray()
    };

    private static PowerDomainFact Domain(string id) => new() { DomainId = id };
    private static PowerProducerFact Producer(string id, string domain) => new() { ProducerId = id, DomainId = domain };
    private static PowerConsumerFact Consumer(string id, string domain) => new() { ConsumerId = id, DomainId = domain };
    private static PowerConversionFact Conversion(string id, string input, string output) => new()
    {
        ConversionId = id,
        InputDomainId = input,
        OutputDomainId = output
    };

    private static string Fingerprint(PowerTopologyResult result) => string.Join("|",
        result.Status,
        string.Join(",", result.DomainIds),
        string.Join(",", result.Producers.Select(item => $"{item.ProducerId}>{item.DomainId}")),
        string.Join(",", result.Consumers.Select(item => $"{item.ConsumerId}<{item.DomainId}")),
        string.Join(",", result.ConversionTopologicalOrder),
        string.Join(";", result.Diagnostics.Select(item => $"{item.Code}:{item.SubjectId}:{item.Message}")));

    private static IEnumerable<IReadOnlyList<T>> Permutations<T>(IReadOnlyList<T> values)
    {
        if (values.Count == 0)
        {
            yield return Array.Empty<T>();
            yield break;
        }

        for (var index = 0; index < values.Count; index++)
        {
            var head = values[index];
            var tailSource = values.Where((_, candidateIndex) => candidateIndex != index).ToArray();
            foreach (var tail in Permutations(tailSource))
                yield return new[] { head }.Concat(tail).ToArray();
        }
    }
}
