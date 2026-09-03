using System.Diagnostics;
using ComponentIntelligence.Cache;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.SymbolArchive;

namespace ComponentIntelligence.Tests.SymbolArchive;

public sealed class AutocadBlockDeepInspectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ci-cp3a-inspect-{Guid.NewGuid():N}");
    private readonly string? _oldOverride = Environment.GetEnvironmentVariable(AutocadCoreConsoleLocator.OverrideEnvironmentVariable);

    public AutocadBlockDeepInspectorTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task ProcessReceivesDisposableCopyNeverOriginalAndParsesAllowedProtocol()
    {
        var source = Touch("source.dwg", "dwg");
        var exe = Touch("accoreconsole.exe", "fake");
        var lisp = Touch("cp3a-inspect.lsp", "(princ)");
        Environment.SetEnvironmentVariable(AutocadCoreConsoleLocator.OverrideEnvironmentVariable, exe);
        var executor = new FakeExecutor { EmitProtocol = true };
        var inspector = new AutocadBlockDeepInspector(new AutocadCoreConsoleLocator(), executor, Path.Combine(_root, "stage"), lisp);
        var result = await inspector.InspectAsync(Candidate(source));
        Assert.Equal(DeepInspectionStatus.Succeeded, result.Status);
        Assert.NotNull(executor.StartInfo);
        Assert.DoesNotContain(source, executor.StartInfo!.ArgumentList);
        Assert.Contains(executor.StartInfo.ArgumentList, argument => Path.GetFileName(argument) == "input.dwg");
        Assert.Equal(new[] { "B1" }, result.Metadata!.BlockNames);
        Assert.Equal("TAG", Assert.Single(result.Metadata.Attributes).Name);
        Assert.Equal("hello", Assert.Single(result.Metadata.TextLabels));
        Assert.Equal(await HashService.Sha256FileAsync(source), result.SourceHashAfter);
    }

    [Fact]
    public void MalformedProtocol_FailsStrictParser()
    {
        Assert.Throws<InvalidDataException>(() => AutocadBlockDeepInspector.ParseProtocol(["WIRE\tP1\tP2"]));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(AutocadCoreConsoleLocator.OverrideEnvironmentVariable, _oldOverride);
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private BlockArchiveCandidate Candidate(string source) => new()
    {
        SourcePath = source, RelativePath = Path.GetFileName(source), FileName = Path.GetFileName(source),
        Extension = Path.GetExtension(source).ToLowerInvariant(), FileSize = new FileInfo(source).Length,
        ModifiedAt = File.GetLastWriteTimeUtc(source), Sha256 = HashService.Sha256(File.ReadAllBytes(source))
    };
    private string Touch(string name, string content)
    {
        var path = Path.Combine(_root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content); return path;
    }

    private sealed class FakeExecutor : IBlockInspectionProcessExecutor
    {
        public ProcessStartInfo? StartInfo { get; private set; }
        public bool EmitProtocol { get; init; }
        public Task<BlockInspectionProcessResult> ExecuteAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
        {
            StartInfo = startInfo;
            if (EmitProtocol)
                File.WriteAllLines(Path.Combine(startInfo.WorkingDirectory, "metadata.tsv"),
                    ["BLOCK\tB1", "ATTR\tTAG\tVALUE", "TEXT\thello", "BBOX\t0\t0\t10\t20"]);
            return Task.FromResult(new BlockInspectionProcessResult(0, "", ""));
        }
    }
}
