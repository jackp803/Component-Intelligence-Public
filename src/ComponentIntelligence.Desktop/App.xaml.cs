using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace ComponentIntelligence.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            SQLitePCL.Batteries_V2.Init();
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            base.OnStartup(e);
        }
        catch (Exception exception)
        {
            MessageBox.Show(FormatException(exception), "SQLite 初始化失敗", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var details = FormatException(e.Exception);
        var logPath = TryWriteUiErrorLog(details);
        MessageBox.Show(
            $"介面操作發生錯誤，但 Component Intelligence 已阻止程式直接閃退。\n\n{details}\n\n診斷紀錄：{logPath ?? "無法寫入"}",
            "Component Intelligence UI 錯誤",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static string? TryWriteUiErrorLog(string details)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ComponentIntelligence",
                "logs");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "ui-errors.log");
            File.AppendAllText(
                path,
                $"[{DateTimeOffset.Now:O}]\n{details}\n{new string('-', 80)}\n");
            return path;
        }
        catch
        {
            return null;
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
