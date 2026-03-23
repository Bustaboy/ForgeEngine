using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GameForge.Editor.EditorShell.ViewModels;

namespace GameForge.Editor.EditorShell.UI;

public partial class NewProjectWizardWindow : Window
{
    private readonly IReadOnlyList<MainWindowViewModel.ProjectTemplatePreset> _templates;
    private int _stepIndex;

    public NewProjectWizardWindow(string? preferredTemplateId = null)
    {
        InitializeComponent();
        _templates = MainWindowViewModel.GetProjectTemplatePresets();
        var templateList = this.FindControl<ListBox>("TemplateListBox");
        if (templateList is not null)
        {
            templateList.ItemsSource = _templates;
            var preferredIndex = _templates
                .Select((template, index) => new { template.Id, index })
                .FirstOrDefault(entry => string.Equals(entry.Id, preferredTemplateId, StringComparison.Ordinal))?.index ?? 0;
            templateList.SelectedIndex = preferredIndex;
        }

        UpdateStepUi();
    }

    public ProjectCreationRequest? Result { get; private set; }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        if (_stepIndex == 0)
        {
            Close();
            return;
        }

        _stepIndex--;
        UpdateStepUi();
    }

    private void OnNextClick(object? sender, RoutedEventArgs e)
    {
        if (!ValidateCurrentStep())
        {
            return;
        }

        _stepIndex = Math.Min(2, _stepIndex + 1);
        UpdateStepUi();
    }

    private void OnCreateClick(object? sender, RoutedEventArgs e)
    {
        if (!ValidateCurrentStep())
        {
            return;
        }

        var templateList = this.FindControl<ListBox>("TemplateListBox");
        var projectNameTextBox = this.FindControl<TextBox>("ProjectNameTextBox");
        var conceptTextBox = this.FindControl<TextBox>("ConceptTextBox");
        if (templateList?.SelectedItem is not MainWindowViewModel.ProjectTemplatePreset selectedTemplate || projectNameTextBox is null)
        {
            return;
        }

        Result = new ProjectCreationRequest(
            selectedTemplate,
            projectNameTextBox.Text?.Trim() ?? string.Empty,
            conceptTextBox?.Text?.Trim() ?? string.Empty);
        Close();
    }

    private bool ValidateCurrentStep()
    {
        var validationText = this.FindControl<TextBlock>("ValidationMessageText");
        if (validationText is not null)
        {
            validationText.Text = string.Empty;
        }

        if (_stepIndex == 0)
        {
            var templateList = this.FindControl<ListBox>("TemplateListBox");
            if (templateList?.SelectedItem is MainWindowViewModel.ProjectTemplatePreset)
            {
                return true;
            }

            if (validationText is not null)
            {
                validationText.Text = "Choose a template before continuing.";
            }

            return false;
        }

        if (_stepIndex == 1)
        {
            var projectNameTextBox = this.FindControl<TextBox>("ProjectNameTextBox");
            if (!string.IsNullOrWhiteSpace(projectNameTextBox?.Text))
            {
                return true;
            }

            if (validationText is not null)
            {
                validationText.Text = "Project name is required.";
            }

            return false;
        }

        return true;
    }

    private void UpdateStepUi()
    {
        var stepLabel = this.FindControl<TextBlock>("StepLabel");
        var templatePanel = this.FindControl<StackPanel>("TemplateStepPanel");
        var namingPanel = this.FindControl<StackPanel>("NamingStepPanel");
        var conceptPanel = this.FindControl<StackPanel>("ConceptStepPanel");
        var backButton = this.FindControl<Button>("BackButton");
        var nextButton = this.FindControl<Button>("NextButton");
        var createButton = this.FindControl<Button>("CreateButton");

        if (templatePanel is null || namingPanel is null || conceptPanel is null || backButton is null || nextButton is null || createButton is null || stepLabel is null)
        {
            return;
        }

        templatePanel.IsVisible = _stepIndex == 0;
        namingPanel.IsVisible = _stepIndex == 1;
        conceptPanel.IsVisible = _stepIndex == 2;

        stepLabel.Text = _stepIndex switch
        {
            0 => "Step 1/3: Choose Template",
            1 => "Step 2/3: Name Project",
            _ => "Step 3/3: Optional Concept Notes",
        };

        backButton.Content = _stepIndex == 0 ? "Cancel" : "Back";
        nextButton.IsVisible = _stepIndex < 2;
        createButton.IsVisible = _stepIndex == 2;
    }

    public sealed record ProjectCreationRequest(
        MainWindowViewModel.ProjectTemplatePreset Template,
        string ProjectName,
        string ConceptNotes);
}
