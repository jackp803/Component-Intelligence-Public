using System.Security.Cryptography;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.SymbolArchive;

namespace ComponentIntelligence.Tests.SymbolArchive;

public sealed class CableArchiveConsumptionTests
{
    [Fact]
    public async Task SameArchivePinsCableTemplateAndRejectsChangedSourceWithoutWritingIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "cable-archive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "cable.dxf"); await File.WriteAllTextAsync(path, "fixture");
            var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
            var repo = new SymbolArchiveRepository(root);
            var entry = new CableArchiveEntry { Template = new() {
                TemplateId = "Y", TemplateRevision = "rev-001", AssetSha256 = hash,
                Ports = [new() { PortId = "P", Name = "M12", Pins = [new() { PinId = "P1", PinNumber = "1" }] }] },
                AssetPath = "cable.dxf", MillimetresPerUnit = 1,
                ContactBindings = [new() { EngineeringEndpointId = "P1", ConnectionPointId = "X1TERM01" }] };
            repo.Save(new() { CableTemplates = [entry] });
            Assert.Equal("ci-symbol-archive.v3", repo.Load().SchemaVersion);
            Assert.Empty(repo.Load().Bindings);
            var before = await File.ReadAllBytesAsync(repo.ArchivePath);
            var resolved = await new CableArchiveResolver(repo).ResolveAsync("Y", "rev-001");
            Assert.Equal(path, resolved.AbsolutePath);
            Assert.Equal(SymbolRevisionStatus.Candidate, resolved.Entry.Status);
            Assert.False(resolved.Entry.Template.MappingConfirmed);
            Assert.Equal(before, await File.ReadAllBytesAsync(repo.ArchivePath));
            await File.WriteAllTextAsync(path, "changed");
            await Assert.ThrowsAsync<InvalidDataException>(() => new CableArchiveResolver(repo).ResolveAsync("Y", "rev-001"));
            Assert.Equal(before, await File.ReadAllBytesAsync(repo.ArchivePath));
            Assert.Throws<InvalidDataException>(() => repo.Save(new() { CableTemplates = [entry with { AssetPath = "../escape.dxf" }] }));
            Assert.Throws<InvalidDataException>(() => repo.Save(new() { CableTemplates = [entry, entry] }));
        }
        finally { Directory.Delete(root, true); }
    }
}
