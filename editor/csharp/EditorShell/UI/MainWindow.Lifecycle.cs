using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit;
using Soul.Editor.EditorDiagnostics;
using Soul.Editor.EditorShell.ViewModels;
using System;
using System.Threading.Tasks;

namespace Soul.Editor.EditorShell.UI;

/// <summary>Window open/close, theme, onboarding visibility, and global key handling.</summary>
public partial class MainWindow
{
    private async void OnOpened(object? sender, EventArgs e)
    {
        if (_firstRunModalChecked)
        {
            return;
        }

        _firstRunModalChecked = true;

        try
        {
            await _viewModel.RefreshModelManagerAsync();
            var welcomeComplete = await _viewModel.IsFirstLaunchWelcomeCompleteAsync();
            if (!welcomeComplete)
            {
                await ShowFirstLaunchQuickSetupDialogAsync();
            }
        }
        catch (Exception ex)
        {
            EditorDiagnosticsLog.LogException("Quick setup startup check failed.", ex);
            _viewModel.SetStatusMessage(
                "First-launch model check hit a snag. You can still open Models & LLM from the menu to add a token and run Quick Setup.");
            await _viewModel.RefreshModelManagerAsync();
        }
    }
    private void SetStartupOnboardingVisibility(bool isVisible, string statusText)
    {
        if (_startupOnboardingOverlay is not null)
        {
            _startupOnboardingOverlay.IsVisible = isVisible;
        }

        if (_startupOnboardingStatusText is not null && !string.IsNullOrWhiteSpace(statusText))
        {
            _startupOnboardingStatusText.Text = statusText;
        }

        if (_startupOnboardingProgressBar is not null)
        {
            _startupOnboardingProgressBar.IsVisible = isVisible;
        }
    }
    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.CancelActiveModelOperation();

        var editor = this.FindControl<TextEditor>("CodeEditor");
        if (editor is not null)
        {
            editor.TextChanged -= OnCodeEditorTextChanged;
        }

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.ThemePreferenceChanged -= ApplyThemePreference;
        _viewModel.ViewportEntities.CollectionChanged -= OnViewportEntitiesChanged;
        RemoveHandler(DragDrop.DragOverEvent, OnWindowAssetDragOver);
        RemoveHandler(DragDrop.DropEvent, OnWindowAssetDrop);
        RemoveHandler(DragDrop.DragLeaveEvent, OnWindowAssetDragLeave);
        KeyDown -= OnMainWindowKeyDown;
        foreach (var entity in _viewModel.ViewportEntities)
        {
            entity.PropertyChanged -= OnViewportEntityPropertyChanged;
        }
        if (_viewportCanvas is not null)
        {
            _viewportCanvas.PointerMoved -= OnViewportPointerMoved;
            _viewportCanvas.PointerPressed -= OnViewportPointerPressed;
            _viewportCanvas.PointerReleased -= OnViewportPointerReleased;
            _viewportCanvas.RemoveHandler(DragDrop.DragOverEvent, OnViewportDragOver);
            _viewportCanvas.RemoveHandler(DragDrop.DragLeaveEvent, OnViewportDragLeave);
            _viewportCanvas.RemoveHandler(DragDrop.DropEvent, OnViewportDrop);
        }

        CloseAssetsFloatingWindow(keepPlacement: true);
        CloseAiInterviewFloatingWindow(keepPlacement: true);
        CloseActivityLogFloatingWindow(keepDockedState: true);
    }
    private void ApplyThemePreference(string themePreference)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = themePreference switch
        {
            "Light" => ThemeVariant.Light,
            "System" => ThemeVariant.Default,
            _ => ThemeVariant.Dark,
        };
    }
    private async void OnMainWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _isFocusModeEnabled)
        {
            OnToggleFocusModeClick(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && !IsTextEntryFocused())
        {
            await _viewModel.HandleDeleteShortcutAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _viewModel.SubmitAiInterviewAnswer();
            e.Handled = true;
            return;
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        if (e.Key == Key.O && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            OnQuickCommandsClick(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.N)
        {
            OnNewSceneClick(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.O && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            OnOpenProjectFolderClick(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.O)
        {
            OnOpenSceneClick(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            OnSaveSceneAsClick(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.S)
        {
            OnSaveSceneClick(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.OemComma)
        {
            OnOpenSettingsClick(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.P && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            await _viewModel.PlayRuntimeAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.I)
        {
            OnImportAssetClick(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void FocusSceneEntityNameEditor()
    {
        var nameEditor = this.FindControl<TextBox>("SceneEntityNameTextBox");
        if (nameEditor is null || !nameEditor.IsEnabled)
        {
            return;
        }

        nameEditor.Focus();
        nameEditor.SelectAll();
    }

    private bool IsTextEntryFocused()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        return focused is TextBox || focused is TextEditor;
    }

}
