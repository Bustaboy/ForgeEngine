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
        AttachPreviewHandlers();
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

    private void AttachPreviewHandlers()
    {
        this.FindControl<ComboBox>("ThemeComboBox")!.SelectionChanged += (_, _) => EmitPreviewIfReady();
        this.FindControl<ToggleSwitch>("AutosaveToggle")!.IsCheckedChanged += (_, _) => EmitPreviewIfReady();
        this.FindControl<ComboBox>("ResolutionComboBox")!.SelectionChanged += (_, _) => EmitPreviewIfReady();
        this.FindControl<NumericUpDown>("FpsLimitNumeric")!.ValueChanged += (_, _) => EmitPreviewIfReady();
        this.FindControl<NumericUpDown>("IconSizeNumeric")!.ValueChanged += (_, _) => EmitPreviewIfReady();
        this.FindControl<NumericUpDown>("HistoryLengthNumeric")!.ValueChanged += (_, _) => EmitPreviewIfReady();
        this.FindControl<ComboBox>("DefaultTemplateComboBox")!.SelectionChanged += (_, _) => EmitPreviewIfReady();
        this.FindControl<TextBox>("MusicTrackTextBox")!.PropertyChanged += (_, _) => EmitPreviewIfReady();
        this.FindControl<TextBox>("AmbientTrackTextBox")!.PropertyChanged += (_, _) => EmitPreviewIfReady();
        this.FindControl<ToggleSwitch>("CombatMusicOverrideToggle")!.IsCheckedChanged += (_, _) => EmitPreviewIfReady();
        this.FindControl<NumericUpDown>("MasterVolumeNumeric")!.ValueChanged += (_, _) => EmitPreviewIfReady();
        this.FindControl<NumericUpDown>("MusicVolumeNumeric")!.ValueChanged += (_, _) => EmitPreviewIfReady();
        this.FindControl<NumericUpDown>("AmbientVolumeNumeric")!.ValueChanged += (_, _) => EmitPreviewIfReady();
        this.FindControl<NumericUpDown>("UiVolumeNumeric")!.ValueChanged += (_, _) => EmitPreviewIfReady();
        this.FindControl<NumericUpDown>("SfxVolumeNumeric")!.ValueChanged += (_, _) => EmitPreviewIfReady();
        this.FindControl<NumericUpDown>("SpatialVoiceLimitNumeric")!.ValueChanged += (_, _) => EmitPreviewIfReady();
        this.FindControl<NumericUpDown>("CombatDuckingNumeric")!.ValueChanged += (_, _) => EmitPreviewIfReady();
        this.FindControl<NumericUpDown>("UiDuckingNumeric")!.ValueChanged += (_, _) => EmitPreviewIfReady();
        this.FindControl<ComboBox>("ReverbZoneComboBox")!.SelectionChanged += (_, _) => EmitPreviewIfReady();
        this.FindControl<NumericUpDown>("ProceduralIntensityNumeric")!.ValueChanged += (_, _) => EmitPreviewIfReady();
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
        SetTextValue("MusicTrackTextBox", preferences.Runtime.Audio.MusicTrack);
        SetTextValue("AmbientTrackTextBox", preferences.Runtime.Audio.AmbientTrack);
        var combatMusicToggle = this.FindControl<ToggleSwitch>("CombatMusicOverrideToggle");
        if (combatMusicToggle is not null)
        {
            combatMusicToggle.IsChecked = preferences.Runtime.Audio.CombatMusicOverride;
        }
        SetNumericValue("MasterVolumeNumeric", preferences.Runtime.Audio.MasterVolume);
        SetNumericValue("MusicVolumeNumeric", preferences.Runtime.Audio.MusicVolume);
        SetNumericValue("AmbientVolumeNumeric", preferences.Runtime.Audio.AmbientVolume);
        SetNumericValue("UiVolumeNumeric", preferences.Runtime.Audio.UiVolume);
        SetNumericValue("SfxVolumeNumeric", preferences.Runtime.Audio.SfxVolume);
        SetNumericValue("SpatialVoiceLimitNumeric", preferences.Runtime.Audio.SpatialVoiceLimit);
        SetNumericValue("CombatDuckingNumeric", preferences.Runtime.Audio.CombatDuckingStrength);
        SetNumericValue("UiDuckingNumeric", preferences.Runtime.Audio.UiDuckingStrength);
        SetComboText("ReverbZoneComboBox", preferences.Runtime.Audio.ReverbZonePreset);
        SetNumericValue("ProceduralIntensityNumeric", preferences.Runtime.Audio.ProceduralIntensity);

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

    private void EmitPreviewIfReady()
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
                Audio = new EditorPreferences.RuntimePreferences.AudioPreferences
                {
                    MusicTrack = GetTextValue("MusicTrackTextBox", "music_exploration"),
                    AmbientTrack = GetTextValue("AmbientTrackTextBox", "ambient_exploration_loop"),
                    CombatMusicOverride = this.FindControl<ToggleSwitch>("CombatMusicOverrideToggle")?.IsChecked ?? true,
                    MasterVolume = GetNumericValue("MasterVolumeNumeric", 85),
                    MusicVolume = GetNumericValue("MusicVolumeNumeric", 75),
                    AmbientVolume = GetNumericValue("AmbientVolumeNumeric", 60),
                    UiVolume = GetNumericValue("UiVolumeNumeric", 80),
                    SfxVolume = GetNumericValue("SfxVolumeNumeric", 80),
                    SpatialVoiceLimit = GetNumericValue("SpatialVoiceLimitNumeric", 24),
                    CombatDuckingStrength = GetNumericValue("CombatDuckingNumeric", 35),
                    UiDuckingStrength = GetNumericValue("UiDuckingNumeric", 15),
                    ReverbZonePreset = GetComboText("ReverbZoneComboBox"),
                    ProceduralIntensity = GetNumericValue("ProceduralIntensityNumeric", 55),
                },
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

    private void SetTextValue(string controlName, string value)
    {
        var textBox = this.FindControl<TextBox>(controlName);
        if (textBox is not null)
        {
            textBox.Text = value;
        }
    }

    private string GetTextValue(string controlName, string fallback)
    {
        var textBox = this.FindControl<TextBox>(controlName);
        var value = textBox?.Text?.Trim();
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
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
