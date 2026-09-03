using ComponentIntelligence.Electrical.Export;
using ComponentIntelligence.SymbolArchive;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SymbolExportManifestTests
{
    [Fact]
    public void ManifestPinsExactResolutionAndSerializesDeterministically()
    {
        var resolution = new SymbolResolution
        {
            ComponentId = "C1", Role = SymbolRole.Schematic, SourceType = SymbolSourceType.ApprovedCustom,
            Revision = "rev-002", AssetPath = "Documents/M/M/autocad/schematic/rev-002/symbol.dwg",
            Sha256 = new string('a', 64)
        };
        var first = SymbolExportManifest.FromResolutions([resolution]);
        var second = SymbolExportManifest.FromResolutions([resolution]);
        var entry = Assert.Single(first.Symbols);
        Assert.Equal("rev-002", entry.Revision);
        Assert.Equal(new string('a', 64), entry.Sha256);
        Assert.Equal(first.ToDeterministicJson(), second.ToDeterministicJson());
    }
}
