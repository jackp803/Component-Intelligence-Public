using System.Diagnostics;
using ComponentIntelligence.Desktop;
using netDxf;
using netDxf.Entities;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicCadFileLoaderTests
{
    [Fact]
    public async Task DwgConversionUsesDisposableInputAndOriginalHashWithoutWritingSource()
    {
        var root = Path.Combine(Path.GetTempPath(), "schematic-loader-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var original = Path.Combine(root, "company.dwg"); await File.WriteAllTextAsync(original, "DWG-test-only");
            var before = File.ReadAllBytes(original);
            var executor = new FakeExecutor();
            var result = await new SchematicCadFileLoader(executor, Path.Combine(root, "staging"), "fake-accoreconsole.exe").ReadAsync(original, 1);
            Assert.NotEqual(original, executor.InputPath);
            Assert.StartsWith(Path.Combine(root, "staging"), executor.InputPath);
            Assert.Equal(before, File.ReadAllBytes(original));
            Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(before)), result.SourceSha256);
            Assert.Single(result.Primitives);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task FailedConverterDoesNotProduceAnImportedAsset()
    {
        var root = Path.Combine(Path.GetTempPath(), "schematic-loader-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "bad.dwg"); await File.WriteAllTextAsync(source, "test");
            await Assert.ThrowsAsync<InvalidOperationException>(() => new SchematicCadFileLoader(new FakeExecutor(true), Path.Combine(root, "staging"), "fake.exe").ReadAsync(source, 1));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class FakeExecutor(bool fail = false) : IBlockInspectionProcessExecutor
    {
        public string InputPath { get; private set; } = "";
        public Task<BlockInspectionProcessResult> ExecuteAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
        {
            InputPath = startInfo.ArgumentList[1];
            Assert.Equal("/i", startInfo.ArgumentList[0]);
            if (fail) return Task.FromResult(new BlockInspectionProcessResult(1, "", "conversion failed"));
            var doc = new DxfDocument(); doc.Entities.Add(new Line(new Vector2(0, 0), new Vector2(20, 20)));
            doc.Save(Path.Combine(startInfo.WorkingDirectory, "display.dxf"));
            return Task.FromResult(new BlockInspectionProcessResult(0, "ok", ""));
        }
    }
}
