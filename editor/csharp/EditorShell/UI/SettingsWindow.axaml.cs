using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GameForge.Editor.EditorShell.ViewModels;

namespace GameForge.Editor.EditorShell.UI;

public partial class SettingsWindow : Window
{
    private readonly IReadOnlyList<MainWindowViewModel.ProjectTemplatePreset> _templates;
    private bool _isInitializing;

    public SettingsWindow(EditorPreferences preferences)
    {
        InitializeComponent();
        _templates = MainWindowViewModel.GetProjectTemplatePresets();
        PopulateTemplateOptions();
        ApplyPreferences(preferences.Sanitize());
    }

    public EditorPreferences? Result { get; private set; }
    public event Action<EditorPreferences>? PreferencesPreviewChanged;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void PopulateTemplateOptions()
    {
        var combo = this.FindControl<ComboBox>("DefaultTemplateComboBox");
        if (combo is null)
        {
            return;
        }

        combo.ItemsSource = _templates;
        combo.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(MainWindowViewModel.ProjectTemplatePreset.DisplayName));
    }

    private void ApplyPreferences(EditorPreferences preferences)
    {
        _isInitializing = true;
        SetComboText("ThemeComboBox", preferences.General.Theme);
        var autosaveToggle = this.FindControl<ToggleSwitch>("AutosaveToggle");
        if (autosaveToggle is not null)
        {
            autosaveToggle.IsChecked = preferences.General.AutosaveEnabled;
        }

        SetComboText("ResolutionComboBox", preferences.Runtime.VulkanResolution);
        SetNumericValue("FpsLimitNumeric", preferences.Runtime.FpsLimit);
        SetNumericValue("IconSizeNumeric", preferences.Editor.IconSize);
        SetNumericValue("HistoryLengthNumeric", preferences.Editor.HistoryLength);

        var templateCombo = this.FindControl<ComboBox>("DefaultTemplateComboBox");
        if (templateCombo is not null)
        {
            templateCombo.SelectedItem = _templates.FirstOrDefault(template =>
                string.Equals(template.Id, preferences.Editor.DefaultTemplateId, StringComparison.Ordinal));
        }

        _isInitializing = false;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var validationMessage = this.FindControl<TextBlock>("ValidationMessageText");
        if (validationMessage is not null)
        {
            validationMessage.Text = string.Empty;
        }

        var theme = GetComboText("ThemeComboBox");
        var resolution = GetComboText("ResolutionComboBox");
        if (string.IsNullOrWhiteSpace(theme) || string.IsNullOrWhiteSpace(resolution))
        {
            if (validationMessage is not null)
            {
                validationMessage.Text = "Theme and resolution are required.";
            }

            return;
        }

        Result = BuildPreferencesFromControls();
        Close();
    }

    private void OnPreferenceControlChanged(object? sender, EventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        PreferencesPreviewChanged?.Invoke(BuildPreferencesFromControls());
    }

    private EditorPreferences BuildPreferencesFromControls()
    {
        var theme = GetComboText("ThemeComboBox");
        var resolution = GetComboText("ResolutionComboBox");
        var autosaveToggle = this.FindControl<ToggleSwitch>("AutosaveToggle");
        var fpsLimit = GetNumericValue("FpsLimitNumeric", 60);
        var iconSize = GetNumericValue("IconSizeNumeric", 58);
        var historyLength = GetNumericValue("HistoryLengthNumeric", 120);
        var templateCombo = this.FindControl<ComboBox>("DefaultTemplateComboBox");
        var templateId = (templateCombo?.SelectedItem as MainWindowViewModel.ProjectTemplatePreset)?.Id
            ?? MainWindowViewModel.GetProjectTemplatePresets()[0].Id;

        return new EditorPreferences
        {
            General = new EditorPreferences.GeneralPreferences
            {
                Theme = theme,
                AutosaveEnabled = autosaveToggle?.IsChecked ?? true,
            },
            Runtime = new EditorPreferences.RuntimePreferences
            {
                VulkanResolution = resolution,
                FpsLimit = fpsLimit,
            },
            Editor = new EditorPreferences.EditorPanePreferences
            {
                IconSize = iconSize,
                HistoryLength = historyLength,
                DefaultTemplateId = templateId,
            },
        }.Sanitize();
    }

    private void SetComboText(string controlName, string value)
    {
        var combo = this.FindControl<ComboBox>(controlName);
        if (combo is null)
        {
            return;
        }

        foreach (var item in combo.Items.Cast<object>())
        {
            if (item is ComboBoxItem comboItem && string.Equals(comboItem.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = comboItem;
                return;
            }

            if (item is MainWindowViewModel.ProjectTemplatePreset template && string.Equals(template.DisplayName, value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = template;
                return;
            }
        }
    }

    private string GetComboText(string controlName)
    {
        var combo = this.FindControl<ComboBox>(controlName);
        return combo?.SelectedItem switch
        {
            ComboBoxItem comboItem => comboItem.Content?.ToString() ?? string.Empty,
            MainWindowViewModel.ProjectTemplatePreset template => template.DisplayName,
            _ => string.Empty,
        };
    }

    private void SetNumericValue(string controlName, int value)
    {
        var numeric = this.FindControl<NumericUpDown>(controlName);
        if (numeric is not null)
        {
            numeric.Value = value;
        }
    }

    private int GetNumericValue(string controlName, int fallback)
    {
        var numeric = this.FindControl<NumericUpDown>(controlName);
        if (numeric?.Value is decimal value)
        {
            return (int)value;
        }

        return fallback;
    }
}
