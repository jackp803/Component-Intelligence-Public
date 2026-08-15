using System.Windows;

namespace ComponentIntelligence.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            SQLitePCL.Batteries_V2.Init();
            base.OnStartup(e);
        }
        catch (Exception exception)
        {
            MessageBox.Show(FormatException(exception), "SQLite 初始化失敗", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    internal static string FormatException(Exception exception)
    {
        var lines = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
            lines.Add($"{current.GetType().FullName}: {current.Message}");
        return string.Join(Environment.NewLine + "→ ", lines);
    }
}
