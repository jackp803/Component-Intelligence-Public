using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;

namespace ComponentIntelligence.Desktop;

public sealed class CableAssemblyEditorDialog : Window
{
    private readonly ElectricalProject _project;
    private readonly CableAssemblyEditorService _service;
    private readonly StackPanel _memberPanel = new();
    private readonly TextBlock _validationSummary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _saveButton = new();
    private bool _dirty;
    private bool _closingFromButton;

    public CableAssemblyEditorDialog(
        ElectricalProject project,
        CableAssemblyEditDraft draft,
        CableAssemblyEditorService service)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        Draft = draft ?? throw new ArgumentNullException(nameof(draft));
        _service = service ?? throw new ArgumentNullException(nameof(service));

        Title = "編輯複合線 / Cable Assembly";
        Width = 900;
        Height = 760;
        MinWidth = 720;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = Draft.IsNew ? "建立複合線" : $"編輯複合線 {Draft.ReferenceDesignator ?? string.Empty}",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var construction = new ComboBox
        {
            ItemsSource = new[]
            {
                new ConstructionChoice("尚未設定", CableConstructionType.Unknown),
                new ConstructionChoice("外購成品線", CableConstructionType.Purchased),
                new ConstructionChoice("自製 / 加工線", CableConstructionType.Custom)
            },
            DisplayMemberPath = nameof(ConstructionChoice.Label),
            SelectedValuePath = nameof(ConstructionChoice.Value),
            SelectedValue = Draft.CableConstructionType,
            MinWidth = 220,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        construction.SelectionChanged += (_, _) =>
        {
            if (construction.SelectedValue is not CableConstructionType value) return;
            Draft.CableConstructionType = value;
            MarkDirtyAndRefresh();
        };
        var constructionField = LabeledField("線材類型", construction);
        Grid.SetRow(constructionField, 1);
        root.Children.Add(constructionField);

        var memberArea = new DockPanel { Margin = new Thickness(0, 12, 0, 12) };
        var add = new Button
        {
            Content = "+ 加入線段",
            Padding = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 8)
        };
        add.Click += AddMember_Click;
        DockPanel.SetDock(add, Dock.Top);
        memberArea.Children.Add(add);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroll.Content = _memberPanel;
        memberArea.Children.Add(scroll);
        Grid.SetRow(memberArea, 2);
        root.Children.Add(memberArea);

        var validationBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(205, 205, 205)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(248, 248, 248)),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 12),
            Child = _validationSummary
        };
        Grid.SetRow(validationBorder, 3);
        root.Children.Add(validationBorder);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button
        {
            Content = "取消",
            IsCancel = true,
            Padding = new Thickness(16, 7, 16, 7),
            Margin = new Thickness(0, 0, 8, 0)
        };
        cancel.Click += (_, _) =>
        {
            _closingFromButton = true;
            DialogResult = false;
        };
        _saveButton.Content = "儲存";
        _saveButton.IsDefault = true;
        _saveButton.Padding = new Thickness(18, 7, 18, 7);
        _saveButton.Click += Save_Click;
        buttons.Children.Add(cancel);
        buttons.Children.Add(_saveButton);
        Grid.SetRow(buttons, 4);
        root.Children.Add(buttons);

        Content = root;
        Closing += Dialog_Closing;
        RebuildMembers();
        RefreshValidation();
    }

    public CableAssemblyEditDraft Draft { get; }

    private void RebuildMembers()
    {
        _memberPanel.Children.Clear();
        foreach (var member in Draft.Members)
            _memberPanel.Children.Add(BuildMemberRow(member));
    }

    private FrameworkElement BuildMemberRow(CableAssemblyMemberDraft member)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = member.DisplayLabel,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold
        });
        if (!string.IsNullOrWhiteSpace(member.EndpointSummary))
        {
            panel.Children.Add(new TextBlock
            {
                Text = member.EndpointSummary,
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 8)
            });
        }

        var role = new ComboBox
        {
            ItemsSource = new[]
            {
                new RoleChoice("尚未設定", CableAssemblySegmentRoleType.Unknown),
                new RoleChoice("主幹", CableAssemblySegmentRoleType.Trunk),
                new RoleChoice("分支", CableAssemblySegmentRoleType.Branch),
                new RoleChoice("其他", CableAssemblySegmentRoleType.Other)
            },
            DisplayMemberPath = nameof(RoleChoice.Label),
            SelectedValuePath = nameof(RoleChoice.Value),
            SelectedValue = member.SegmentRoleType,
            MinWidth = 140
        };
        var branch = new TextBox
        {
            Text = member.SegmentRoleIndex?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            MinWidth = 70,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "分支編號必須是大於 0 的整數"
        };
        var other = new TextBox
        {
            Text = member.SegmentRoleName ?? string.Empty,
            MinWidth = 150,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "其他角色名稱"
        };
        var roleRow = new StackPanel { Orientation = Orientation.Horizontal };
        roleRow.Children.Add(role);
        roleRow.Children.Add(branch);
        roleRow.Children.Add(other);
        panel.Children.Add(LabeledField("角色", roleRow));

        void UpdateRoleVisibility()
        {
            branch.Visibility = member.SegmentRoleType == CableAssemblySegmentRoleType.Branch
                ? Visibility.Visible
                : Visibility.Collapsed;
            other.Visibility = member.SegmentRoleType == CableAssemblySegmentRoleType.Other
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        role.SelectionChanged += (_, _) =>
        {
            if (role.SelectedValue is not CableAssemblySegmentRoleType value) return;
            member.SegmentRoleType = value;
            if (value == CableAssemblySegmentRoleType.Branch)
            {
                member.SegmentRoleName = null;
                if (member.SegmentRoleIndex is null or <= 0)
                {
                    member.SegmentRoleIndex = _service.SuggestNextBranchIndex(Draft);
                    branch.Text = member.SegmentRoleIndex.Value.ToString(CultureInfo.InvariantCulture);
                }
            }
            else
            {
                member.SegmentRoleIndex = null;
                branch.Text = string.Empty;
                if (value != CableAssemblySegmentRoleType.Other)
                {
                    member.SegmentRoleName = null;
                    other.Text = string.Empty;
                }
            }
            UpdateRoleVisibility();
            MarkDirtyAndRefresh();
        };
        branch.TextChanged += (_, _) =>
        {
            if (member.SegmentRoleType != CableAssemblySegmentRoleType.Branch) return;
            member.SegmentRoleIndex = int.TryParse(branch.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
            MarkDirtyAndRefresh();
        };
        other.TextChanged += (_, _) =>
        {
            if (member.SegmentRoleType != CableAssemblySegmentRoleType.Other) return;
            member.SegmentRoleName = string.IsNullOrWhiteSpace(other.Text) ? null : other.Text.Trim();
            MarkDirtyAndRefresh();
        };
        UpdateRoleVisibility();

        var length = new TextBox
        {
            Text = member.ProvidedLengthMm.HasValue
                ? (member.ProvidedLengthMm.Value / 1000d).ToString("0.###", CultureInfo.InvariantCulture)
                : string.Empty,
            MinWidth = 100
        };
        var lengthSource = new TextBlock
        {
            Text = $"來源：{LengthSourceLabel(member.LengthSource)}",
            Foreground = Brushes.DimGray,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        var lengthRow = new StackPanel { Orientation = Orientation.Horizontal };
        lengthRow.Children.Add(length);
        lengthRow.Children.Add(lengthSource);
        panel.Children.Add(LabeledField("長度 (m)", lengthRow));
        length.TextChanged += (_, _) =>
        {
            _service.SetLengthMetres(member, length.Text);
            lengthSource.Text = $"來源：{LengthSourceLabel(member.LengthSource)}";
            MarkDirtyAndRefresh();
        };

        var remove = new Button
        {
            Content = "從複合線移除",
            Padding = new Thickness(10, 5, 10, 5),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0)
        };
        remove.Click += (_, _) =>
        {
            var answer = MessageBox.Show(
                this,
                "只會移除此複合線的成員關係；Cable Segment、連線與畫布路徑都會保留。確定繼續？",
                "移除複合線成員",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            _service.RemoveMember(Draft, member.CableInstanceId);
            _dirty = true;
            RebuildMembers();
            RefreshValidation();
        };
        panel.Children.Add(remove);

        panel.Children.Add(new Expander
        {
            Header = "詳細資訊 / Details",
            Margin = new Thickness(0, 7, 0, 0),
            Content = new TextBlock
            {
                Text = $"CableInstanceId: {member.CableInstanceId}",
                FontFamily = new FontFamily("Consolas"),
                TextWrapping = TextWrapping.Wrap
            }
        });

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(210, 210, 210)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 10),
            Child = panel
        };
    }

    private void AddMember_Click(object sender, RoutedEventArgs e)
    {
        var candidates = _service.GetEligibleMembers(_project, Draft);
        if (candidates.Count == 0)
        {
            MessageBox.Show(this, "目前沒有可加入的既有 Cable Segment。", "加入線段", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var picker = new CableAssemblyMemberPickerDialog(candidates) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedMember is null) return;
        _service.AddMember(_project, Draft, picker.SelectedMember.CableInstanceId);
        _dirty = true;
        RebuildMembers();
        RefreshValidation();
    }

    private void MarkDirtyAndRefresh()
    {
        _dirty = true;
        RefreshValidation();
    }

    private void RefreshValidation()
    {
        var validation = _service.Validate(_project, Draft);
        _saveButton.IsEnabled = validation.CanSave;
        if (validation.Issues.Count == 0)
        {
            _validationSummary.Text = "驗證通過。";
            _validationSummary.Foreground = Brushes.DarkGreen;
            return;
        }

        _validationSummary.Text = string.Join(Environment.NewLine, validation.Issues.Select(issue =>
            $"{(issue.IsBlocking ? "錯誤" : "提醒")}：{issue.Message}"));
        _validationSummary.Foreground = validation.CanSave ? Brushes.DarkGoldenrod : Brushes.Firebrick;
        _validationSummary.ToolTip = string.Join(Environment.NewLine, validation.Issues.Select(issue =>
            $"{issue.Code}: {string.Join(", ", issue.SourceObjectIds)}"));
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!_service.Validate(_project, Draft).CanSave) return;
        _closingFromButton = true;
        DialogResult = true;
    }

    private void Dialog_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closingFromButton || !_dirty || DialogResult == true) return;
        var answer = MessageBox.Show(
            this,
            "尚有未儲存的複合線變更。要捨棄這些變更嗎？",
            "未儲存的變更",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer == MessageBoxResult.No) e.Cancel = true;
    }

    private static FrameworkElement LabeledField(string label, FrameworkElement control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        panel.Children.Add(control);
        return panel;
    }

    private static string LengthSourceLabel(CableLengthSource source) => source switch
    {
        CableLengthSource.Mechanical => "機構提供",
        CableLengthSource.Imported => "匯入資料",
        CableLengthSource.User => "使用者輸入",
        _ => "尚未設定"
    };

    private sealed record ConstructionChoice(string Label, CableConstructionType Value);
    private sealed record RoleChoice(string Label, CableAssemblySegmentRoleType Value);
}
