using System.IO;

namespace ComponentIntelligence.Desktop;

public sealed class AutocadCoreConsoleLocator
{
    public const string OverrideEnvironmentVariable = "COMPONENT_INTELLIGENCE_ACCORECONSOLE";

    public string? Resolve()
    {
        var configured = Environment.GetEnvironmentVariable(OverrideEnvironmentVariable)?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var exact = Path.GetFullPath(configured);
            if (!File.Exists(exact))
                throw new FileNotFoundException($"{OverrideEnvironmentVariable} points to a missing executable.", exact);
            return exact;
        }

        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        var candidates = new List<string>();
        foreach (var root in roots)
        {
            var autodesk = Path.Combine(root, "Autodesk");
            if (!Directory.Exists(autodesk)) continue;
            IEnumerable<string> versionDirectories;
            try
            {
                versionDirectories = Directory.EnumerateDirectories(autodesk, "AutoCAD *", SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var versionDirectory in versionDirectories)
            {
                var executable = Path.Combine(versionDirectory, "accoreconsole.exe");
                if (File.Exists(executable)) candidates.Add(Path.GetFullPath(executable));
            }
        }

        return candidates
            .OrderByDescending(path => Path.GetFileName(Path.GetDirectoryName(path)), StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}
