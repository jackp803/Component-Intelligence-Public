using System.Windows;
using System.Windows.Controls;

namespace ComponentIntelligence.Desktop;

public enum ConnectionEditAction { PinMapping, CableSettings, InsertInterface, DeleteConnection }

public sealed class ConnectionActionDialog : Window
{
    public ConnectionEditAction Action { get; private set; }

    public ConnectionActionDialog(string context)
    {
        Title = "編輯連線";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new StackPanel { Margin = new Thickness(20) };
        body.Children.Add(new TextBlock { Text = context, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        foreach (var (label, action) in new[] {
            ("腳位對應 / Pin Mapping", ConnectionEditAction.PinMapping),
            ("線材設定 / Cable Settings", ConnectionEditAction.CableSettings),
            ("插入介面 / Insert Interface", ConnectionEditAction.InsertInterface),
            ("刪除連線 / Delete Connection", ConnectionEditAction.DeleteConnection) })
        {
            var button = new Button { Content = label, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 8) };
            button.Click += (_, _) => { Action = action; DialogResult = true; };
            body.Children.Add(button);
        }
        body.Children.Add(new Button { Content = "取消", IsCancel = true, Padding = new Thickness(12, 8, 12, 8) });
        Content = body;
    }
}
