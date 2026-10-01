namespace ComponentIntelligence.Runtime;

public sealed class CentralWorkbookPathSettings
{
    private readonly string _settingsPath;
    private readonly string? _launchDefault;

    public CentralWorkbookPathSettings(string databasePath, string sharedSettingsPath, string? launchDefault)
    {
        _launchDefault = launchDefault?.Trim();
        _settingsPath = string.IsNullOrWhiteSpace(_launchDefault) ? sharedSettingsPath :
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!, "central-workbook.txt");
    }

    public string? Load()
    {
        if (File.Exists(_settingsPath))
        {
            var selected = File.ReadAllText(_settingsPath).Trim();
            if (!string.IsNullOrWhiteSpace(selected)) return selected;
        }
        return string.IsNullOrWhiteSpace(_launchDefault) ? null : _launchDefault;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, Path.GetFullPath(path.Trim()));
    }
}
