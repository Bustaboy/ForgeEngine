using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using AvaloniaEdit;
using Soul.Editor.EditorDiagnostics;
using Soul.Editor.EditorShell;
using Soul.Editor.EditorShell.ViewModels;
using System;
using System.Threading.Tasks;

namespace Soul.Editor.EditorShell.UI;

/// <summary>Modal dialogs and secondary windows opened from the main shell.</summary>
public partial class MainWindow
{
    private void ClampOwnedDialogSize(Window dialog, double preferredWidth, double preferredHeight)
    {
        const double margin = 28;
        var maxW = Math.Max(360, Bounds.Width - margin);
        var maxH = Math.Max(320, Bounds.Height - margin);
        dialog.MaxWidth = maxW;
        dialog.MaxHeight = maxH;
        dialog.Width = Math.Min(preferredWidth, maxW);
        dialog.Height = Math.Min(preferredHeight, maxH);
    }

    private async Task ShowFirstLaunchQuickSetupDialogAsync()
    {
        var shouldRunQuickSetup = false;
        var shouldOpenModels = false;

        var titleBrush = DialogThemeBrush("EditorDialogTitleBrush", "#EEF4FF");
        var mutedBrush = DialogThemeBrush("EditorDialogMutedBrush", "#CFE5FF");
        var subtleBrush = DialogThemeBrush("EditorDialogAccentMutedBrush", "#9FC2E5");

        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock
        {
            Text = "Welcome to Soul Loom",
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            Foreground = titleBrush,
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(new TextBlock
        {
            Text = "Quick Setup downloads LoomGuard, Free-Will, and Coding — the recommended first-boot path for Soul Loom V1.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = mutedBrush,
        });
        body.Children.Add(new TextBlock
        {
            Text = "Before the first download: add your Hugging Face token in Settings → Models & LLM. The same screen has shortcuts to create an account, sign in, and open access tokens.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = subtleBrush,
        });
        body.Children.Add(new TextBlock
        {
            Text = "LoomGuard stays useful after setup: guardrails, critique passes, and lightweight local decisions.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = subtleBrush,
        });
        body.Children.Add(new TextBlock
        {
            Text = "Prefer to review each model yourself first? Open Models & LLM instead of running Quick Setup here.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = subtleBrush,
        });

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = body,
        };

        var laterButton = new Button { Content = "Later", MinWidth = 88 };
        laterButton.Classes.Add("shell-dialog-secondary");
        var settingsButton = new Button { Content = "Open Models & LLM", MinWidth = 150 };
        settingsButton.Classes.Add("shell-dialog-secondary");
        var quickButton = new Button { Content = "Run Quick Setup", MinWidth = 170 };
        quickButton.Classes.Add("shell-dialog-primary");

        var actions = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { laterButton, settingsButton, quickButton },
        };

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
        };
        Grid.SetRow(scroll, 0);
        Grid.SetRow(actions, 1);
        root.Children.Add(scroll);
        root.Children.Add(actions);

        var frame = new Border
        {
            Padding = new Thickness(18),
            Child = root,
        };
        frame.Classes.Add("shell-dialog-frame");

        var modal = new Window
        {
            Title = "Welcome — Quick Setup",
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = DialogThemeBrush("EditorDialogWindowBgBrush", "#08111B"),
            Content = new Border { Margin = new Thickness(14), Child = frame },
        };
        ClampOwnedDialogSize(modal, 680, 420);

        laterButton.Click += (_, _) => modal.Close();
        settingsButton.Click += (_, _) =>
        {
            shouldOpenModels = true;
            modal.Close();
        };
        quickButton.Click += (_, _) =>
        {
            shouldRunQuickSetup = true;
            modal.Close();
        };

        await modal.ShowDialog(this);
        if (!shouldRunQuickSetup)
        {
            if (shouldOpenModels)
            {
                await ShowSettingsWindowAsync("Models");
            }

            return;
        }

        var completed = await _viewModel.RunQuickStartSetupAsync();
        if (completed)
        {
            await ShowQuickSetupSummaryDialogAsync();
        }
    }

    private async Task ShowQuickSetupSummaryDialogAsync()
    {
        await _viewModel.RefreshModelManagerAsync();
        var summary = await _viewModel.BuildQuickSetupSummaryAsync();
        var shouldOpenModels = false;
        var shouldCreateFirstPrototype = false;

        var titleBrush = DialogThemeBrush("EditorDialogTitleBrush", "#EEF4FF");
        var mutedBrush = DialogThemeBrush("EditorDialogMutedBrush", "#CFE5FF");
        var subtleBrush = DialogThemeBrush("EditorDialogAccentMutedBrush", "#9FC2E5");
        var bodyBrush = DialogThemeBrush("EditorDialogAccentMutedBrush", "#AFC2DF");

        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock
        {
            Text = "Quick Setup complete",
            FontSize = 21,
            FontWeight = FontWeight.Bold,
            Foreground = titleBrush,
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(new TextBlock
        {
            Text = "Core model status below uses the same Installed / Not found labels as Models & LLM. While downloads run, progress appears in the main window overlay and in each model row.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = mutedBrush,
        });
        body.Children.Add(new TextBlock
        {
            Text = "LoomGuard is meant to stay installed for guardrails, critique, and quick local decisions.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = subtleBrush,
        });
        body.Children.Add(new TextBlock
        {
            Text = summary,
            TextWrapping = TextWrapping.Wrap,
            Foreground = bodyBrush,
        });

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = body,
        };

        var closeButton = new Button { Content = "Close", MinWidth = 90 };
        closeButton.Classes.Add("shell-dialog-secondary");
        var openModelsButton = new Button { Content = "Open Models & LLM", MinWidth = 150 };
        openModelsButton.Classes.Add("shell-dialog-secondary");
        var createFirstPrototypeButton = new Button { Content = "Create First Prototype", MinWidth = 180 };
        createFirstPrototypeButton.Classes.Add("shell-dialog-primary");

        var actions = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { closeButton, openModelsButton, createFirstPrototypeButton },
        };

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
        };
        Grid.SetRow(scroll, 0);
        Grid.SetRow(actions, 1);
        root.Children.Add(scroll);
        root.Children.Add(actions);

        var frame = new Border
        {
            Padding = new Thickness(18),
            Child = root,
        };
        frame.Classes.Add("shell-dialog-frame");

        var modal = new Window
        {
            Title = "Quick Setup complete",
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = DialogThemeBrush("EditorDialogWindowBgBrush", "#08111B"),
            Content = new Border { Margin = new Thickness(14), Child = frame },
        };
        ClampOwnedDialogSize(modal, 640, 420);

        closeButton.Click += (_, _) => modal.Close();
        openModelsButton.Click += (_, _) =>
        {
            shouldOpenModels = true;
            modal.Close();
        };
        createFirstPrototypeButton.Click += (_, _) =>
        {
            shouldCreateFirstPrototype = true;
            modal.Close();
        };

        await modal.ShowDialog(this);
        if (shouldOpenModels)
        {
            await ShowSettingsWindowAsync("Models");
        }
        else if (shouldCreateFirstPrototype)
        {
            OnNewProjectClick(this, new RoutedEventArgs());
        }
    }
    private async Task ShowBenchmarkModalAsync(BenchmarkResultEnvelope benchmark)
    {
        var summary = string.Join(Environment.NewLine, benchmark.Recommendations
            .Where(entry => entry.Recommended)
            .Select(entry => $"- {entry.ModelId} ({entry.Role}): {entry.Reason}"));

        if (string.IsNullOrWhiteSpace(summary))
        {
            summary = "- No GPU-qualified models detected; using CPU-safe fallback.";
        }

        var modal = new Window
        {
            Title = "First-Run Hardware Wizard",
            Width = 560,
            Height = 360,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "First Run Detected", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold },
                    new TextBlock { Text = $"GPU: {benchmark.Hardware.GpuName} ({benchmark.Hardware.GpuVramGb} GB)" },
                    new TextBlock { Text = $"CPU cores: {benchmark.Hardware.CpuCores}" },
                    new TextBlock { Text = $"Models prepared now: {benchmark.PrepareModelsInvoked}" },
                    new TextBlock { Text = "Recommended models:", Margin = new Avalonia.Thickness(0, 6, 0, 0) },
                    new TextBlock { Text = summary, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new Button
                    {
                        Content = "Continue",
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Margin = new Avalonia.Thickness(0, 12, 0, 0),
                    },
                },
            },
        };

        if (modal.Content is StackPanel panel && panel.Children[^1] is Button button)
        {
            button.Click += (_, _) => modal.Close();
        }

        await modal.ShowDialog(this);
    }

    private async Task<string?> ShowRenameHierarchyDialogAsync(string currentName)
    {
        var textBox = new TextBox
        {
            Text = currentName,
            MinWidth = 320,
        };

        string? result = null;
        var modal = new Window
        {
            Title = "Rename entity",
            Width = 420,
            Height = 190,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "New display name", FontWeight = FontWeight.SemiBold },
                    textBox,
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            new Button { Content = "Cancel", MinWidth = 86 },
                            new Button { Content = "Apply", MinWidth = 86 },
                        }
                    },
                },
            },
        };

        if (modal.Content is StackPanel panel
            && panel.Children[^1] is StackPanel actions
            && actions.Children[0] is Button cancelButton
            && actions.Children[1] is Button applyButton)
        {
            cancelButton.Click += (_, _) => modal.Close();
            applyButton.Click += (_, _) =>
            {
                result = textBox.Text?.Trim();
                modal.Close();
            };
        }

        await modal.ShowDialog(this);
        return result;
    }

    private async Task ShowSettingsWindowAsync(string? initialTab = null)
    {
        var originalPreferences = _viewModel.GetPreferencesSnapshot();
        try
        {
            await _viewModel.RefreshModelManagerAsync();
            var settingsWindow = new SettingsWindow(originalPreferences, initialTab);
            settingsWindow.PreferencesPreviewChanged += preview => _viewModel.ApplyPreferencesPreview(preview);
            async Task RefreshSettingsModelsAsync()
            {
                await _viewModel.RefreshModelManagerAsync();
                settingsWindow.UpdateModelManagerState(
                    _viewModel.ModelManagerEntries,
                    _viewModel.ModelRecommendationSummary,
                    _viewModel.LoomGuardKeepInstalledMessage,
                    _viewModel.ModelManagerStatus,
                    _viewModel.CanRunModelManagerActions);
                settingsWindow.UpdateHuggingFaceTokenState(
                    _viewModel.GetHuggingFaceTokenStatus(),
                    _viewModel.IsHuggingFaceTokenConfigured());
            }

            settingsWindow.QuickStartModelsRequested += async () =>
            {
                await _viewModel.RunQuickStartSetupAsync();
                await RefreshSettingsModelsAsync();
            };
            settingsWindow.DownloadLoomGuardRequested += async () =>
            {
                await _viewModel.DownloadManagedModelAsync("LoomGuard");
                await RefreshSettingsModelsAsync();
            };
            settingsWindow.DownloadCodingModelRequested += async () =>
            {
                await _viewModel.DownloadManagedModelAsync("coding");
                await RefreshSettingsModelsAsync();
            };
            settingsWindow.RunModelOnboardingRequested += async () =>
            {
                await _viewModel.RunModelOnboardingAsync();
                await RefreshSettingsModelsAsync();
            };
            settingsWindow.SetupRecommendedModelsRequested += async () =>
            {
                await _viewModel.SetupRecommendedModelsAsync();
                await RefreshSettingsModelsAsync();
            };
            settingsWindow.SetupFreeWillModelRequested += async () =>
            {
                await _viewModel.SetupFreeWillModelAsync();
                await RefreshSettingsModelsAsync();
            };
            settingsWindow.RetryModelOperationRequested += async () =>
            {
                await _viewModel.RetryLastModelOperationAsync();
                await RefreshSettingsModelsAsync();
            };
            settingsWindow.RefreshModelsRequested += RefreshSettingsModelsAsync;
            settingsWindow.SaveHuggingFaceTokenRequested += async token =>
            {
                await _viewModel.SaveHuggingFaceTokenAsync(token);
                await RefreshSettingsModelsAsync();
            };
            settingsWindow.ClearHuggingFaceTokenRequested += async () =>
            {
                await _viewModel.ClearHuggingFaceTokenAsync();
                await RefreshSettingsModelsAsync();
            };
            settingsWindow.UpdateModelManagerState(
                _viewModel.ModelManagerEntries,
                _viewModel.ModelRecommendationSummary,
                _viewModel.LoomGuardKeepInstalledMessage,
                _viewModel.ModelManagerStatus,
                _viewModel.CanRunModelManagerActions);
            settingsWindow.UpdateHuggingFaceTokenState(
                _viewModel.GetHuggingFaceTokenStatus(),
                _viewModel.IsHuggingFaceTokenConfigured());
            await settingsWindow.ShowDialog(this);
            if (settingsWindow.Result is null)
            {
                _viewModel.ApplyPreferencesPreview(originalPreferences);
                return;
            }

            await _viewModel.ApplyAndSavePreferencesAsync(settingsWindow.Result);
        }
        catch (Exception ex)
        {
            EditorDiagnosticsLog.LogException("Unable to open Preferences & Settings window.", ex);
            _viewModel.ApplyPreferencesPreview(originalPreferences);
            _viewModel.SetStatusMessage($"Unable to open Preferences & Settings: {ex.Message}");
        }
    }

    private async void OnOpenSettingsClick(object? sender, RoutedEventArgs e)
    {
        await ShowSettingsWindowAsync();
    }

    private async void OnOpenModelsAndLlmSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.IsDownloadErrorVisible)
        {
            _viewModel.DismissModelErrorDialog();
        }

        await ShowSettingsWindowAsync("Models");
    }

    private async void OnSaveCodeClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.SaveCodeEditsAsync();
    }
    private async Task<string> ShowSaveChangesPromptAsync()
    {
        var result = "Cancel";
        var dialog = new Window
        {
            Title = "Unsaved Scene Changes",
            Width = 420,
            Height = 190,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#0B111D")),
            Content = new Border
            {
                Margin = new Thickness(14),
                Padding = new Thickness(16),
                Background = new SolidColorBrush(Color.Parse("#101722")),
                BorderBrush = new SolidColorBrush(Color.Parse("#2B3446")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Child = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Save changes to the active scene before exiting?",
                            Foreground = new SolidColorBrush(Color.Parse("#EEF4FF")),
                            FontSize = 16,
                            FontWeight = FontWeight.SemiBold,
                            TextWrapping = TextWrapping.Wrap,
                        },
                        new TextBlock
                        {
                            Text = _viewModel.SceneNameLabel,
                            Foreground = new SolidColorBrush(Color.Parse("#9FC2E5")),
                            TextWrapping = TextWrapping.Wrap,
                        },
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            Spacing = 8,
                            Children =
                            {
                                new Button { Content = "Don't Save", MinWidth = 96 },
                                new Button { Content = "Cancel", MinWidth = 84 },
                                new Button { Content = "Save", MinWidth = 84 },
                            },
                        },
                    },
                },
            },
        };

        if (dialog.Content is Border { Child: StackPanel root } && root.Children[2] is StackPanel actions)
        {
            ((Button)actions.Children[0]).Click += (_, _) => { result = "DontSave"; dialog.Close(); };
            ((Button)actions.Children[1]).Click += (_, _) => { result = "Cancel"; dialog.Close(); };
            ((Button)actions.Children[2]).Click += (_, _) => { result = "Save"; dialog.Close(); };
        }

        await dialog.ShowDialog(this);
        return result;
    }
    private async Task<bool> ShowPublishDryRunConfirmationAsync()
    {
        var decision = false;
        var modal = new Window
        {
            Width = 520,
            Height = 260,
            Title = "Publish to Steam (Dry-Run)",
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new Border
            {
                Padding = new Thickness(18),
                Background = new SolidColorBrush(Color.Parse("#0D1320")),
                Child = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Confirm Steam publish dry-run?",
                            Foreground = new SolidColorBrush(Color.Parse("#EEF4FF")),
                            FontWeight = Avalonia.Media.FontWeight.SemiBold,
                            FontSize = 16,
                        },
                        new TextBlock
                        {
                            Text = "This runs readiness gate + generates a local audit trail only. No Steam upload happens in V1.",
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(Color.Parse("#9FC2E5")),
                        },
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            Spacing = 8,
                            Children =
                            {
                                new Button
                                {
                                    Content = "Cancel",
                                    MinWidth = 90,
                                },
                                new Button
                                {
                                    Content = "Run Dry-Run",
                                    MinWidth = 110,
                                },
                            }
                        },
                    }
                }
            }
        };

        if (modal.Content is Border { Child: StackPanel panel }
            && panel.Children[^1] is StackPanel actions
            && actions.Children.Count == 2
            && actions.Children[0] is Button cancel
            && actions.Children[1] is Button confirm)
        {
            cancel.Click += (_, _) =>
            {
                decision = false;
                modal.Close();
            };
            confirm.Click += (_, _) =>
            {
                decision = true;
                modal.Close();
            };
        }

        await modal.ShowDialog(this);
        return decision;
    }

    private async Task<bool> ShowUploadToSteamStubConfirmationAsync()
    {
        var decision = false;
        var modal = new Window
        {
            Width = 560,
            Height = 300,
            Title = "Upload to Steam (Stub)",
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new Border
            {
                Padding = new Thickness(18),
                Background = new SolidColorBrush(Color.Parse("#0D1320")),
                Child = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Confirm Steam upload stub?",
                            Foreground = new SolidColorBrush(Color.Parse("#EEF4FF")),
                            FontWeight = Avalonia.Media.FontWeight.SemiBold,
                            FontSize = 16,
                        },
                        new TextBlock
                        {
                            Text = "This generates a fresh ZIP with release notes, writes a local audit log, and simulates upload progress. No real Steam API request is sent in V1.",
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(Color.Parse("#9FC2E5")),
                        },
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            Spacing = 8,
                            Children =
                            {
                                new Button
                                {
                                    Content = "Cancel",
                                    MinWidth = 90,
                                },
                                new Button
                                {
                                    Content = "Upload (Stub)",
                                    MinWidth = 120,
                                },
                            }
                        },
                    }
                }
            }
        };

        if (modal.Content is Border { Child: StackPanel panel }
            && panel.Children[^1] is StackPanel actions
            && actions.Children.Count == 2
            && actions.Children[0] is Button cancel
            && actions.Children[1] is Button confirm)
        {
            cancel.Click += (_, _) =>
            {
                decision = false;
                modal.Close();
            };
            confirm.Click += (_, _) =>
            {
                decision = true;
                modal.Close();
            };
        }

        await modal.ShowDialog(this);
        return decision;
    }

    private async Task<bool> ShowModelRemovalConfirmationAsync(string friendlyName)
    {
        var decision = false;
        var modelLabel = string.IsNullOrWhiteSpace(friendlyName) ? "this model" : friendlyName.Trim();
        var modal = new Window
        {
            Width = 500,
            Height = 240,
            Title = "Remove Model",
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new Border
            {
                Padding = new Thickness(18),
                Background = new SolidColorBrush(Color.Parse("#0D1320")),
                Child = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"Remove {modelLabel} from managed models?",
                            Foreground = new SolidColorBrush(Color.Parse("#EEF4FF")),
                            FontWeight = Avalonia.Media.FontWeight.SemiBold,
                            FontSize = 16,
                        },
                        new TextBlock
                        {
                            Text = "This updates models.json only. Downloaded cache files can be reused if you install again later.",
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(Color.Parse("#9FC2E5")),
                        },
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            Spacing = 8,
                            Children =
                            {
                                new Button
                                {
                                    Content = "Cancel",
                                    MinWidth = 90,
                                },
                                new Button
                                {
                                    Content = "Remove",
                                    MinWidth = 90,
                                },
                            }
                        },
                    }
                }
            }
        };

        if (modal.Content is Border { Child: StackPanel panel }
            && panel.Children[^1] is StackPanel actions
            && actions.Children.Count == 2
            && actions.Children[0] is Button cancel
            && actions.Children[1] is Button confirm)
        {
            cancel.Click += (_, _) =>
            {
                decision = false;
                modal.Close();
            };
            confirm.Click += (_, _) =>
            {
                decision = true;
                modal.Close();
            };
        }

        await modal.ShowDialog(this);
        return decision;
    }
}
