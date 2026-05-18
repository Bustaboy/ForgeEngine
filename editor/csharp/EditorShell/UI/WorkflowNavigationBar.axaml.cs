using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Soul.Editor.EditorShell.Navigation;

namespace Soul.Editor.EditorShell.UI;

public partial class WorkflowNavigationBar : UserControl
{
    private readonly Dictionary<WorkflowSection, Button> _buttons = new();

    public event EventHandler<WorkflowSection>? SectionSelected;

    public WorkflowNavigationBar()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    public void BindSections(IReadOnlyList<(WorkflowSection Section, string Label)> sections, WorkflowSection active)
    {
        var panel = this.FindControl<StackPanel>("NavButtonPanel")!;
        panel.Children.Clear();
        _buttons.Clear();

        foreach (var (section, label) in sections)
        {
            var button = new Button
            {
                Content = label,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Tag = section,
            };
            button.Classes.Add("panel-action-button");
            if (section == active)
            {
                button.Classes.Add("panel-tab");
                button.Classes.Add("active");
            }

            button.Click += OnNavButtonClick;
            _buttons[section] = button;
            panel.Children.Add(button);
        }
    }

    public void SetActiveSection(WorkflowSection section)
    {
        foreach (var (key, button) in _buttons)
        {
            button.Classes.Remove("active");
            if (key == section)
            {
                button.Classes.Add("active");
            }
        }
    }

    private void OnNavButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WorkflowSection section })
        {
            SectionSelected?.Invoke(this, section);
        }
    }
}
