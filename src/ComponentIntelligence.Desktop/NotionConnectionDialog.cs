using System.Windows;
using System.Windows.Controls;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Desktop;

public sealed class NotionConnectionDialog : Window
{
    private const string TokenVariable = "COMPONENT_INTELLIGENCE_NOTION_TOKEN";
    private readonly PasswordBox _token = new();
    private readonly TextBox _components = new();
    private readonly TextBlock _status = new();
    private readonly Button _test = new();
    private readonly Button _save = new();

    public NotionConnectionDialog()
    {
        var current = NotionKnowledgeStoreOptions.FromEnvironment();
        Title = "Notion 中央電料庫 / Central Knowledge";
        Width = 720;
        Height = 480;
        MinWidth = 620;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _token.Password = current.Token ?? string.Empty;
        _components.Text = current.ComponentsDataSourceId;
        _token.MinHeight = 30;
        _components.MinHeight = 30;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock { Text = "Notion 中央電料知識庫", FontSize = 22, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock
        {
            Text = "這是 Component Intelligence 桌面程式自己的 Notion Integration Token（整合權杖），不是 OpenAI API。Token 不會寫入 GitHub 或 Notion 頁面；儲存後放在目前 Windows 使用者的環境設定。沒有連線時軟體仍完全 Local-first（本地優先）可用。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(0, 5, 0, 0)
        });
        root.Children.Add(header);

        var fields = new StackPanel();
        fields.Children.Add(Label("Integration Token（Notion 整合權杖）"));
        fields.Children.Add(_token);
        fields.Children.Add(new TextBlock
        {
            Text = "請在 Notion 建立 Internal Integration，給 Central Knowledge databases Read + Update 權限，再把 Token 貼在這裡。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 3, 0, 12)
        });
        fields.Children.Add(Label("Components Data Source ID（通常不需要改）"));
        fields.Children.Add(_components);
        fields.Children.Add(new TextBlock
        {
            Text = "其他 Documents / Ports / Pins / Specifications Data Source ID 使用程式內中央庫預設值；只有複製到別的 Notion workspace 時才需要環境變數覆寫。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 3, 0, 8)
        });
        Grid.SetRow(fields, 1);
        root.Children.Add(fields);

        _status.Text = current.IsEnabled ? "目前：已設定 Token（尚未在此視窗重新測試）" : "目前：未設定 Notion Token；軟體使用本機模式。";
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 8, 0, 10);
        Grid.SetRow(_status, 2);
        root.Children.Add(_status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var clear = new Button { Content = "清除 Token", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 0) };
        _test.Content = "測試連線 / Test";
        _test.Padding = new Thickness(12, 7, 12, 7);
        _test.Margin = new Thickness(0, 0, 8, 0);
        _save.Content = "儲存 / Save";
        _save.Padding = new Thickness(16, 7, 16, 7);
        var close = new Button { Content = "關閉", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        clear.Click += Clear_Click;
        _test.Click += Test_Click;
        _save.Click += Save_Click;
        buttons.Children.Add(clear);
        buttons.Children.Add(_test);
        buttons.Children.Add(_save);
        buttons.Children.Add(close);
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);

        Content = root;
    }

    public bool SettingsChanged { get; private set; }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        var token = _token.Password.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            _status.Text = "請先輸入 Notion Integration Token。";
            return;
        }
        await BusyAsync(async () =>
        {
            var options = BuildOptions(token);
            var lookup = await new NotionComponentKnowledgeStore(options).FindByIdentityAsync("__COMPONENT_INTELLIGENCE_CONNECTION_TEST__", Guid.NewGuid().ToString("N"));
            var failure = lookup.Diagnostics.FirstOrDefault(value => value.StartsWith("NOTION_CENTRAL_READ_FAILED", StringComparison.OrdinalIgnoreCase));
            _status.Text = failure is null
                ? "✓ Notion API 與 Components 中央資料表可讀取。測試使用不存在的假料號，不會新增任何資料。"
                : "✗ Notion 連線測試失敗：" + failure;
        });
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var token = _token.Password.Trim();
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("Token 不可空白；若要停用 Notion，請按「清除 Token」。");
            var components = _components.Text.Trim();
            if (string.IsNullOrWhiteSpace(components))
                throw new InvalidOperationException("Components Data Source ID 不可空白。");

            SetEnvironment(TokenVariable, token);
            SetEnvironment("COMPONENT_INTELLIGENCE_NOTION_COMPONENTS_DS", components);
            SettingsChanged = true;
            _status.Text = "✓ 已儲存到目前 Windows 使用者設定與本程式 Process。建議再按「測試連線」。";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Notion 設定無法儲存", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        SetEnvironment(TokenVariable, null);
        _token.Clear();
        SettingsChanged = true;
        _status.Text = "Notion Token 已清除；Component Intelligence 會繼續以本機模式運作。";
    }

    private NotionKnowledgeStoreOptions BuildOptions(string token) => NotionKnowledgeStoreOptions.FromEnvironment() with
    {
        Token = token,
        ComponentsDataSourceId = string.IsNullOrWhiteSpace(_components.Text) ? NotionKnowledgeStoreOptions.FromEnvironment().ComponentsDataSourceId : _components.Text.Trim()
    };

    private async Task BusyAsync(Func<Task> action)
    {
        _test.IsEnabled = false;
        _save.IsEnabled = false;
        try { await action(); }
        catch (Exception exception) { _status.Text = "✗ " + exception.Message; }
        finally { _test.IsEnabled = true; _save.IsEnabled = true; }
    }

    private static TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 5, 0, 4) };

    private static void SetEnvironment(string name, string? value)
    {
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
        try
        {
            Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            // The process value still makes the current app usable. The user-scoped persistence can be
            // retried on a Windows account that permits updating user environment variables.
        }
    }
}
