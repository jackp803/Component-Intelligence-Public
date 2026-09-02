using System.Windows;
using System.Windows.Controls;
using ComponentIntelligence.Electrical.Editing;

namespace ComponentIntelligence.Desktop;

public sealed class CableAssemblyMemberPickerDialog : Window
{
    private readonly ListBox _members = new();

    public CableAssemblyMemberPickerDialog(IReadOnlyList<CableAssemblyMemberDraft> candidates)
    {
        Title = "加入線段 / Add Cable Segment";
        Width = 560;
        Height = 430;
        MinWidth = 460;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = "選擇既有 Cable Segment。這個動作只加入複合線成員，不會建立或改動畫布配線。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 10)
        });

        _members.ItemsSource = candidates;
        _members.DisplayMemberPath = nameof(CableAssemblyMemberDraft.DisplayLabel);
        _members.MouseDoubleClick += (_, _) => AcceptSelection();
        Grid.SetRow(_members, 1);
        root.Children.Add(_members);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        buttons.Children.Add(new Button
        {
            Content = "取消",
            IsCancel = true,
            Padding = new Thickness(16, 7, 16, 7),
            Margin = new Thickness(0, 0, 8, 0)
        });
        var add = new Button
        {
            Content = "加入",
            IsDefault = true,
            Padding = new Thickness(18, 7, 18, 7)
        };
        add.Click += (_, _) => AcceptSelection();
        buttons.Children.Add(add);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
    }

    public CableAssemblyMemberDraft? SelectedMember => _members.SelectedItem as CableAssemblyMemberDraft;

    private void AcceptSelection()
    {
        if (SelectedMember is null) return;
        DialogResult = true;
    }
}
