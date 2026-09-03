using ComponentIntelligence.SymbolArchive;

namespace ComponentIntelligence.Tests.SymbolArchive;

public sealed class SymbolArchiveRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ci-symbol-archive-{Guid.NewGuid():N}");

    public SymbolArchiveRepositoryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ContractEnums_HaveExactV1Values()
    {
        Assert.Equal(new[] { "Schematic", "ConnectorDetail", "PanelFootprint", "TopologyVisual" }, Enum.GetNames<SymbolRole>());
        Assert.Equal(new[] { "ApprovedCustom", "Manufacturer", "LibraryStandard", "GeneratedGeneric" }, Enum.GetNames<SymbolSourceType>());
        Assert.Equal(new[] { "Candidate", "Approved", "Superseded", "Rejected" }, Enum.GetNames<SymbolRevisionStatus>());
        Assert.Equal(new[] { "NotRequested", "Unavailable", "Succeeded", "Failed" }, Enum.GetNames<DeepInspectionStatus>());
    }

    [Fact]
    public void MissingSidecar_LoadsEmptyV1Document()
    {
        var document = Repository().Load();
        Assert.Equal(SymbolArchiveRepository.SchemaVersion, document.SchemaVersion);
        Assert.Empty(document.Bindings);
    }

    [Fact]
    public void MalformedOrWrongSchema_FailsClosed()
    {
        File.WriteAllText(Path.Combine(_root, SymbolArchiveRepository.FileName), "{");
        Assert.Throws<InvalidDataException>(() => Repository().Load());
        File.WriteAllText(Path.Combine(_root, SymbolArchiveRepository.FileName), "{\"schemaVersion\":\"wrong\",\"bindings\":[]}");
        Assert.Throws<InvalidDataException>(() => Repository().Load());
    }

    [Theory]
    [InlineData("C:/formal/symbol.dwg")]
    [InlineData("D:/formal/symbol.dwg")]
    [InlineData("//server/share/symbol.dwg")]
    [InlineData("../outside/symbol.dwg")]
    public void AssetPath_MustStayArchiveRelative(string assetPath)
    {
        Assert.Throws<InvalidDataException>(() => Repository().ValidateAndNormalize(Document(assetPath: assetPath)));
    }

    [Fact]
    public void Sha256_IsValidatedAndNormalizedLowercase()
    {
        var normalized = Repository().ValidateAndNormalize(Document(hash: new string('A', 64)));
        Assert.Equal(new string('a', 64), normalized.Bindings[0].Revisions[0].AssetHashSha256);
        Assert.Throws<InvalidDataException>(() => Repository().ValidateAndNormalize(Document(hash: "abc")));
    }

    [Fact]
    public void DuplicateRevisionApprovedOrEndpoint_FailsClosed()
    {
        var revision = Revision("rev-001", SymbolRevisionStatus.Approved, "P1");
        var duplicateRevision = Document() with
        {
            Bindings = [new ComponentSymbolBinding { ComponentId = "C1", Role = SymbolRole.Schematic, Revisions = [revision, revision] }]
        };
        Assert.Throws<InvalidDataException>(() => Repository().ValidateAndNormalize(duplicateRevision));

        var twoApproved = Document() with
        {
            Bindings = [new ComponentSymbolBinding
            {
                ComponentId = "C1", Role = SymbolRole.Schematic,
                Revisions = [revision, Revision("rev-002", SymbolRevisionStatus.Approved, "P2")]
            }]
        };
        Assert.Throws<InvalidDataException>(() => Repository().ValidateAndNormalize(twoApproved));

        var duplicateEndpoint = Document() with
        {
            Bindings = [new ComponentSymbolBinding
            {
                ComponentId = "C1", Role = SymbolRole.Schematic,
                Revisions = [revision with { PortBindings = [Port("P1", "T1"), Port("P1", "T2")] }]
            }]
        };
        Assert.Throws<InvalidDataException>(() => Repository().ValidateAndNormalize(duplicateEndpoint));
    }

    [Fact]
    public void IndependentRolesForSameComponent_CoexistAndSerializeDeterministically()
    {
        var repository = Repository();
        var document = new SymbolArchiveDocument
        {
            Bindings =
            [
                new ComponentSymbolBinding { ComponentId = "C1", Role = SymbolRole.PanelFootprint, Revisions = [Revision("rev-001", SymbolRevisionStatus.Approved, "P2")] },
                new ComponentSymbolBinding { ComponentId = "C1", Role = SymbolRole.Schematic, Revisions = [Revision("rev-001", SymbolRevisionStatus.Approved, "P1")] }
            ]
        };
        repository.Save(document);
        var first = File.ReadAllText(repository.ArchivePath);
        repository.Save(repository.Load());
        var second = File.ReadAllText(repository.ArchivePath);
        Assert.Equal(first, second);
        Assert.Equal(2, repository.Load().Bindings.Count);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private SymbolArchiveRepository Repository() => new(_root);
    private static SymbolArchiveDocument Document(string assetPath = "Documents/M/M/autocad/schematic/rev-001/symbol.dwg", string? hash = null) => new()
    {
        Bindings = [new ComponentSymbolBinding
        {
            ComponentId = "C1", Role = SymbolRole.Schematic,
            Revisions = [new SymbolRevisionRecord
            {
                Revision = "rev-001", SourceType = SymbolSourceType.ApprovedCustom, AssetPath = assetPath,
                AssetHashSha256 = hash ?? new string('a', 64), Status = SymbolRevisionStatus.Approved
            }]
        }]
    };
    private static SymbolRevisionRecord Revision(string revision, SymbolRevisionStatus status, string endpoint) => new()
    {
        Revision = revision,
        SourceType = SymbolSourceType.ApprovedCustom,
        AssetPath = $"Documents/M/M/autocad/schematic/{revision}/symbol.dwg",
        AssetHashSha256 = new string(revision.EndsWith("1", StringComparison.Ordinal) ? 'a' : 'b', 64),
        Status = status,
        PortBindings = [Port(endpoint, "TERM")]
    };
    private static SymbolPortBinding Port(string endpoint, string connection) => new() { EngineeringEndpointId = endpoint, ConnectionPointId = connection };
}
