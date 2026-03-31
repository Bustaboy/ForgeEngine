using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Controls.Shapes;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using GameForge.Editor.EditorDiagnostics;
using GameForge.Editor.EditorShell.EditorSystems;
using GameForge.Editor.EditorShell.ViewModels;
using System.ComponentModel;
using System.Collections.Specialized;

namespace GameForge.Editor.EditorShell.UI;

public partial class MainWindow : Window
{
    private const double MarkerSize = 34.0;
    private const string AssetDragFormat = "application/x-gameforge-asset-id";
    private const string HierarchyDragFormat = "application/x-gameforge-hierarchy-entity";
    private readonly MainWindowViewModel _viewModel = new();
    private bool _firstRunModalChecked;
    private bool _isSyncingEditorText;
    private Canvas? _viewportCanvas;
    private Border? _viewportFocusBorder;
    private MainWindowViewModel.ViewportEntity? _draggingEntity;
    private Point _dragPointerStart;
    private float _dragEntityStartX;
    private float _dragEntityStartY;
    private bool _isPanningViewport;
    private Point _panPointerStart;
    private double _viewportOriginWorldX;
    private double _viewportOriginWorldY;
    private double _viewportZoom = 38.0;
    private bool _isMarqueeSelecting;
    private Point _marqueeStart;
    private Border? _marqueeVisual;
    private Border? _startupOnboardingOverlay;
    private TextBlock? _startupOnboardingStatusText;
    private ProgressBar? _startupOnboardingProgressBar;
    private Grid? _rootLayoutGrid;
    private Border? _topRibbonBorder;
    private Grid? _workspaceGrid;
    private Border? _toolRailBorder;
    private Border? _leftDockBorder;
    private Border? _rightDockBorder;
    private Border? _timelineDockBorder;
    private Border? _activityDockBorder;
    private ContentControl? _assetsPanelLeftHost;
    private ContentControl? _assetsPanelRightHost;
    private ContentControl? _assetsPanelBottomHost;
    private Border? _assetsPanelHost;
    private ContentControl? _aiInterviewBottomHost;
    private ContentControl? _aiInterviewRightHost;
    private Grid? _aiInterviewPanelHost;
    private TabItem? _assetsDockTab;
    private TabItem? _aiInterviewDockTab;
    private Button? _focusModeExitButton;
    private GridSplitter? _leftCenterSplitter;
    private GridSplitter? _centerRightSplitter;
    private GridSplitter? _workspaceTimelineSplitter;
    private GridSplitter? _timelineActivitySplitter;
    private bool _isToolRailVisible = true;
    private bool _isLeftDockVisible = true;
    private bool _isRightDockVisible = true;
    private bool _isTimelineDockVisible = true;
    private bool _isActivityDockVisible = true;
    private bool _isTopRibbonVisible = true;
    private bool _isFocusModeEnabled;
    private GridLength _leftDockWidth = new(2.3, GridUnitType.Star);
    private GridLength _rightDockWidth = new(2.8, GridUnitType.Star);
    private GridLength _timelineDockHeight = new(240);
    private GridLength _activityDockHeight = new(160);
    private Window? _assetsFloatingWindow;
    private Window? _aiInterviewFloatingWindow;
    private bool _suppressAssetsFloatingWindowClose;
    private bool _suppressAiInterviewFloatingWindowClose;

    public MainWindow()
    {
        InitializeComponent();
        ConfigureCodeEditor();
        ConfigureViewportSurface();
        DataContext = _viewModel;
        ApplyWorkspaceModePreference(forceReset: true);
        SyncDetachedPanelPlacements();
        AddHandler(DragDrop.DragOverEvent, OnWindowAssetDragOver);
        AddHandler(DragDrop.DropEvent, OnWindowAssetDrop);
        AddHandler(DragDrop.DragLeaveEvent, OnWindowAssetDragLeave);
        _viewModel.ThemePreferenceChanged += ApplyThemePreference;
        ApplyThemePreference(_viewModel.ThemePreference);
        Opened += OnOpened;
        Closed += OnClosed;
        KeyDown += OnMainWindowKeyDown;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _startupOnboardingOverlay = this.FindControl<Border>("StartupOnboardingOverlay");
        _startupOnboardingStatusText = this.FindControl<TextBlock>("StartupOnboardingStatusText");
        _startupOnboardingProgressBar = this.FindControl<ProgressBar>("StartupOnboardingProgressBar");
        _rootLayoutGrid = this.FindControl<Grid>("RootLayoutGrid");
        _topRibbonBorder = this.FindControl<Border>("TopRibbonBorder");
        _workspaceGrid = this.FindControl<Grid>("WorkspaceGrid");
        _toolRailBorder = this.FindControl<Border>("ToolRailBorder");
        _leftDockBorder = this.FindControl<Border>("LeftDockBorder");
        _viewportFocusBorder = this.FindControl<Border>("ViewportFocusBorder");
        _rightDockBorder = this.FindControl<Border>("RightDockBorder");
        _timelineDockBorder = this.FindControl<Border>("TimelineDockBorder");
        _activityDockBorder = this.FindControl<Border>("ActivityDockBorder");
        _assetsPanelLeftHost = this.FindControl<ContentControl>("AssetsPanelLeftHost");
        _assetsPanelRightHost = this.FindControl<ContentControl>("AssetsPanelRightHost");
        _assetsPanelBottomHost = this.FindControl<ContentControl>("AssetsPanelBottomHost");
        _assetsPanelHost = this.FindControl<Border>("AssetsPanelHost");
        _aiInterviewBottomHost = this.FindControl<ContentControl>("AiInterviewBottomHost");
        _aiInterviewRightHost = this.FindControl<ContentControl>("AiInterviewRightHost");
        _aiInterviewPanelHost = this.FindControl<Grid>("AiInterviewPanelHost");
        _assetsDockTab = this.FindControl<TabItem>("AssetsDockTab");
        _aiInterviewDockTab = this.FindControl<TabItem>("AiInterviewDockTab");
        _focusModeExitButton = this.FindControl<Button>("FocusModeExitButton");
        _leftCenterSplitter = this.FindControl<GridSplitter>("LeftCenterSplitter");
        _centerRightSplitter = this.FindControl<GridSplitter>("CenterRightSplitter");
        _workspaceTimelineSplitter = this.FindControl<GridSplitter>("WorkspaceTimelineSplitter");
        _timelineActivitySplitter = this.FindControl<GridSplitter>("TimelineActivitySplitter");
        ApplyWorkspaceLayout();
    }

    private void ConfigureCodeEditor()
    {
        var editor = this.FindControl<TextEditor>("CodeEditor");
        if (editor is null)
        {
            return;
        }

        editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C++");
        editor.Options.ConvertTabsToSpaces = true;
        editor.Options.IndentationSize = 4;
        editor.Text = _viewModel.MonacoEditorContent;
        editor.TextChanged += OnCodeEditorTextChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void ConfigureViewportSurface()
    {
        _viewportCanvas = this.FindControl<Canvas>("ViewportCanvas");
        if (_viewportCanvas is null)
        {
            return;
        }

        _viewportCanvas.PointerMoved += OnViewportPointerMoved;
        _viewportCanvas.PointerPressed += OnViewportPointerPressed;
        _viewportCanvas.PointerReleased += OnViewportPointerReleased;
        _viewportCanvas.PointerWheelChanged += OnViewportPointerWheelChanged;
        _viewportCanvas.GotFocus += (_, _) => UpdateViewportFocusVisual(true);
        _viewportCanvas.LostFocus += (_, _) => UpdateViewportFocusVisual(false);
        _viewportCanvas.SizeChanged += (_, _) => RefreshViewportVisuals();
        DragDrop.SetAllowDrop(_viewportCanvas, true);
        _viewportCanvas.AddHandler(DragDrop.DragOverEvent, OnViewportDragOver);
        _viewportCanvas.AddHandler(DragDrop.DragLeaveEvent, OnViewportDragLeave);
        _viewportCanvas.AddHandler(DragDrop.DropEvent, OnViewportDrop);
        _viewModel.ViewportEntities.CollectionChanged += OnViewportEntitiesChanged;
        RefreshViewportVisuals();
    }

    private void OnCodeEditorTextChanged(object? sender, EventArgs e)
    {
        if (_isSyncingEditorText)
        {
            return;
        }

        var editor = this.FindControl<TextEditor>("CodeEditor");
        if (editor is null)
        {
            return;
        }

        _viewModel.MonacoEditorContent = editor.Text ?? string.Empty;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsCreatorModeEnabled))
        {
            ApplyWorkspaceModePreference(forceReset: true);
            SyncDetachedPanelPlacements();
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.AssetsPanelPlacement))
        {
            SyncAssetsPanelPlacement();
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.AiInterviewPlacement))
        {
            SyncAiInterviewPlacement();
            return;
        }

        if (e.PropertyName != nameof(MainWindowViewModel.MonacoEditorContent))
        {
            return;
        }

        var editor = this.FindControl<TextEditor>("CodeEditor");
        if (editor is null)
        {
            return;
        }

        var nextText = _viewModel.MonacoEditorContent ?? string.Empty;
        if (string.Equals(editor.Text, nextText, StringComparison.Ordinal))
        {
            return;
        }

        _isSyncingEditorText = true;
        try
        {
            editor.Text = nextText;
        }
        finally
        {
            _isSyncingEditorText = false;
        }
    }

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
            var onboardingCompleted = await _viewModel.IsOnboardingCompletedAsync();
            if (!onboardingCompleted)
            {
                await ShowFirstLaunchQuickSetupDialogAsync();
            }
        }
        catch (Exception ex)
        {
            EditorDiagnosticsLog.LogException("Quick setup startup check failed.", ex);
            _viewModel.SetStatusMessage($"Quick setup startup check warning: {ex.Message}");
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

    private async Task ShowFirstLaunchQuickSetupDialogAsync()
    {
        var shouldRunQuickSetup = false;
        var shouldOpenModels = false;
        var modal = new Window
        {
            Title = "Welcome / Quick Setup",
            Width = 680,
            Height = 390,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#08111B")),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Border
            {
                Margin = new Thickness(14),
                Padding = new Thickness(18),
                Background = new SolidColorBrush(Color.Parse("#0D1320")),
                BorderBrush = new SolidColorBrush(Color.Parse("#355A87")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Welcome to Soul Loom",
                            FontSize = 24,
                            FontWeight = FontWeight.Bold,
                            Foreground = new SolidColorBrush(Color.Parse("#EEF4FF")),
                        },
                        new TextBlock
                        {
                            Text = "Quick Setup installs the first-boot path for V1: ForgeGuard, Free-Will, and Coding.",
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(Color.Parse("#CFE5FF")),
                        },
                        new TextBlock
                        {
                            Text = "Before the first download, add your Hugging Face token in Models & LLM. Soul Loom can open the sign-up, login, and access-token pages for you.",
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(Color.Parse("#9FC2E5")),
                        },
                        new TextBlock
                        {
                            Text = "Keep ForgeGuard installed: it powers local guardrails, critique passes, and lightweight decisions across workflows.",
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(Color.Parse("#9FC2E5")),
                        },
                        new TextBlock
                        {
                            Text = "If you prefer to review each model manually first, open Models & LLM instead of running the guided setup.",
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(Color.Parse("#9FC2E5")),
                        },
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            Spacing = 8,
                            Margin = new Thickness(0, 14, 0, 0),
                            Children =
                            {
                                new Button { Content = "Later", MinWidth = 90 },
                                new Button { Content = "Open Models & LLM", MinWidth = 150 },
                                new Button
                                {
                                    Content = "Run Quick Setup",
                                    MinWidth = 170,
                                    Background = new SolidColorBrush(Color.Parse("#1D6EE8")),
                                    Foreground = Brushes.White,
                                },
                            },
                        },
                    }
                },
            },
        };

        if (modal.Content is Border { Child: StackPanel root } && root.Children[^1] is StackPanel actions)
        {
            if (actions.Children[0] is Button laterButton)
            {
                laterButton.Click += (_, _) => modal.Close();
            }
            if (actions.Children[1] is Button settingsButton)
            {
                settingsButton.Click += (_, _) =>
                {
                    shouldOpenModels = true;
                    modal.Close();
                };
            }
            if (actions.Children[2] is Button quickButton)
            {
                quickButton.Click += (_, _) =>
                {
                    shouldRunQuickSetup = true;
                    modal.Close();
                };
            }
        }

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
        var summary = await _viewModel.BuildQuickSetupSummaryAsync();
        var shouldOpenModels = false;
        var shouldCreateFirstPrototype = false;
        var modal = new Window
        {
            Title = "Quick Setup Complete",
            Width = 620,
            Height = 380,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#08111B")),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Border
            {
                Margin = new Thickness(14),
                Padding = new Thickness(16),
                Background = new SolidColorBrush(Color.Parse("#0D1320")),
                BorderBrush = new SolidColorBrush(Color.Parse("#355A87")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Quick Setup complete ✅",
                            FontSize = 21,
                            FontWeight = FontWeight.Bold,
                            Foreground = new SolidColorBrush(Color.Parse("#EEF4FF")),
                        },
                        new TextBlock
                        {
                            Text = "Installed now: ForgeGuard + Free-Will + Coding.",
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(Color.Parse("#CFE5FF")),
                        },
                        new TextBlock
                        {
                            Text = "ForgeGuard should stay installed for local guardrails, critique, and lightweight decisions.",
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(Color.Parse("#9FC2E5")),
                        },
                        new TextBlock
                        {
                            Text = summary,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(Color.Parse("#AFC2DF")),
                        },
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            Spacing = 8,
                            Margin = new Thickness(0, 8, 0, 0),
                            Children =
                            {
                                new Button { Content = "Close", MinWidth = 90 },
                                new Button { Content = "Open Models & LLM", MinWidth = 150 },
                                new Button
                                {
                                    Content = "Create First Prototype",
                                    MinWidth = 180,
                                    Background = new SolidColorBrush(Color.Parse("#1D6EE8")),
                                    Foreground = Brushes.White,
                                },
                            },
                        },
                    }
                },
            },
        };

        if (modal.Content is Border { Child: StackPanel panel } && panel.Children[^1] is StackPanel actions)
        {
            if (actions.Children[0] is Button closeButton)
            {
                closeButton.Click += (_, _) => modal.Close();
            }
            if (actions.Children[1] is Button openModelsButton)
            {
                openModelsButton.Click += (_, _) =>
                {
                    shouldOpenModels = true;
                    modal.Close();
                };
            }
            if (actions.Children[2] is Button createFirstPrototypeButton)
            {
                createFirstPrototypeButton.Click += (_, _) =>
                {
                    shouldCreateFirstPrototype = true;
                    modal.Close();
                };
            }
        }

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

    private void ApplyWorkspaceLayout()
    {
        if (_rootLayoutGrid is null
            || _rootLayoutGrid.RowDefinitions.Count < 7
            || _workspaceGrid is null
            || _workspaceGrid.ColumnDefinitions.Count < 6)
        {
            return;
        }

        if (_topRibbonBorder is not null)
        {
            _topRibbonBorder.IsVisible = _isTopRibbonVisible;
        }

        if (_toolRailBorder is not null)
        {
            _toolRailBorder.IsVisible = _isToolRailVisible;
        }

        if (_leftDockBorder is not null)
        {
            _leftDockBorder.IsVisible = _isLeftDockVisible;
        }

        if (_rightDockBorder is not null)
        {
            _rightDockBorder.IsVisible = _isRightDockVisible;
        }

        if (_timelineDockBorder is not null)
        {
            _timelineDockBorder.IsVisible = _isTimelineDockVisible;
        }

        if (_activityDockBorder is not null)
        {
            _activityDockBorder.IsVisible = _isActivityDockVisible;
        }

        if (_focusModeExitButton is not null)
        {
            _focusModeExitButton.IsVisible = _isFocusModeEnabled;
        }

        if (_leftCenterSplitter is not null)
        {
            _leftCenterSplitter.IsVisible = _isLeftDockVisible;
        }

        if (_centerRightSplitter is not null)
        {
            _centerRightSplitter.IsVisible = _isRightDockVisible;
        }

        var hasBottomDock = _isTimelineDockVisible || _isActivityDockVisible;
        if (_workspaceTimelineSplitter is not null)
        {
            _workspaceTimelineSplitter.IsVisible = hasBottomDock;
        }

        if (_timelineActivitySplitter is not null)
        {
            _timelineActivitySplitter.IsVisible = _isTimelineDockVisible && _isActivityDockVisible;
        }

        _workspaceGrid.ColumnDefinitions[0].Width = _isToolRailVisible ? new GridLength(72) : new GridLength(0);
        _workspaceGrid.ColumnDefinitions[1].Width = _isLeftDockVisible ? _leftDockWidth : new GridLength(0);
        _workspaceGrid.ColumnDefinitions[2].Width = _isLeftDockVisible ? new GridLength(6) : new GridLength(0);
        _workspaceGrid.ColumnDefinitions[3].Width = new GridLength(1, GridUnitType.Star);
        _workspaceGrid.ColumnDefinitions[4].Width = _isRightDockVisible ? new GridLength(6) : new GridLength(0);
        _workspaceGrid.ColumnDefinitions[5].Width = _isRightDockVisible ? _rightDockWidth : new GridLength(0);

        _rootLayoutGrid.RowDefinitions[0].Height = _isTopRibbonVisible ? GridLength.Auto : new GridLength(0);
        _rootLayoutGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
        _rootLayoutGrid.RowDefinitions[2].Height = hasBottomDock ? new GridLength(6) : new GridLength(0);
        _rootLayoutGrid.RowDefinitions[3].Height = _isTimelineDockVisible ? _timelineDockHeight : new GridLength(0);
        _rootLayoutGrid.RowDefinitions[4].Height = _isTimelineDockVisible && _isActivityDockVisible ? new GridLength(6) : new GridLength(0);
        _rootLayoutGrid.RowDefinitions[5].Height = _isActivityDockVisible ? _activityDockHeight : new GridLength(0);
        _rootLayoutGrid.RowDefinitions[6].Height = GridLength.Auto;
    }

    private void RememberCurrentDockSizes()
    {
        if (_leftDockBorder is not null && _leftDockBorder.IsVisible && _leftDockBorder.Bounds.Width > 0)
        {
            _leftDockWidth = new GridLength(_leftDockBorder.Bounds.Width);
        }

        if (_rightDockBorder is not null && _rightDockBorder.IsVisible && _rightDockBorder.Bounds.Width > 0)
        {
            _rightDockWidth = new GridLength(_rightDockBorder.Bounds.Width);
        }

        if (_timelineDockBorder is not null && _timelineDockBorder.IsVisible && _timelineDockBorder.Bounds.Height > 0)
        {
            _timelineDockHeight = new GridLength(_timelineDockBorder.Bounds.Height);
        }

        if (_activityDockBorder is not null && _activityDockBorder.IsVisible && _activityDockBorder.Bounds.Height > 0)
        {
            _activityDockHeight = new GridLength(_activityDockBorder.Bounds.Height);
        }
    }

    private void ApplyWorkspaceModePreference(bool forceReset)
    {
        if (_isFocusModeEnabled && !forceReset)
        {
            return;
        }

        _isFocusModeEnabled = false;
        _isTopRibbonVisible = true;
        _isRightDockVisible = true;

        if (_viewModel.IsCreatorModeEnabled)
        {
            _isToolRailVisible = false;
            _isLeftDockVisible = false;
            _isTimelineDockVisible = false;
            _isActivityDockVisible = false;
            _viewModel.IsAdvancedInspectorEnabled = false;
            if (forceReset)
            {
                _rightDockWidth = new GridLength(420);
            }
        }
        else
        {
            _isToolRailVisible = true;
            _isLeftDockVisible = true;
            _isTimelineDockVisible = true;
            _isActivityDockVisible = true;
            _viewModel.IsAdvancedInspectorEnabled = true;
            if (forceReset)
            {
                _leftDockWidth = new GridLength(2.3, GridUnitType.Star);
                _rightDockWidth = new GridLength(2.8, GridUnitType.Star);
                _timelineDockHeight = new GridLength(240);
                _activityDockHeight = new GridLength(160);
            }
        }

        ApplyWorkspaceLayout();
    }

    private void OnToggleLeftPaneClick(object? sender, RoutedEventArgs e)
    {
        RememberCurrentDockSizes();
        _isFocusModeEnabled = false;
        _isLeftDockVisible = !_isLeftDockVisible;
        ApplyWorkspaceLayout();
    }

    private void OnToggleRightPaneClick(object? sender, RoutedEventArgs e)
    {
        RememberCurrentDockSizes();
        _isFocusModeEnabled = false;
        _isRightDockVisible = !_isRightDockVisible;
        ApplyWorkspaceLayout();
    }

    private void OnToggleBottomDockClick(object? sender, RoutedEventArgs e)
    {
        RememberCurrentDockSizes();
        _isFocusModeEnabled = false;
        var hasBottomDock = _isTimelineDockVisible || _isActivityDockVisible;
        if (hasBottomDock)
        {
            _isTimelineDockVisible = false;
            _isActivityDockVisible = false;
        }
        else
        {
            _isTimelineDockVisible = true;
            _isActivityDockVisible = true;
        }
        ApplyWorkspaceLayout();
    }

    private void OnToggleTimelineDockClick(object? sender, RoutedEventArgs e)
    {
        RememberCurrentDockSizes();
        _isFocusModeEnabled = false;
        _isTimelineDockVisible = !_isTimelineDockVisible;
        ApplyWorkspaceLayout();
    }

    private void OnToggleActivityDockClick(object? sender, RoutedEventArgs e)
    {
        RememberCurrentDockSizes();
        _isFocusModeEnabled = false;
        _isActivityDockVisible = !_isActivityDockVisible;
        ApplyWorkspaceLayout();
    }

    private void OnToggleFocusModeClick(object? sender, RoutedEventArgs e)
    {
        RememberCurrentDockSizes();
        _isFocusModeEnabled = !_isFocusModeEnabled;
        if (_isFocusModeEnabled)
        {
            _isTopRibbonVisible = false;
            _isToolRailVisible = false;
            _isLeftDockVisible = false;
            _isRightDockVisible = false;
            _isTimelineDockVisible = false;
            _isActivityDockVisible = false;
        }
        else
        {
            ApplyWorkspaceModePreference(forceReset: false);
            return;
        }

        ApplyWorkspaceLayout();
    }

    private async void OnToggleWorkspaceModeClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.SetCreatorModeEnabledAsync(!_viewModel.IsCreatorModeEnabled);
    }

    private void OnShowSimpleInspectorClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.IsAdvancedInspectorEnabled = false;
    }

    private void OnShowAdvancedInspectorClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.IsAdvancedInspectorEnabled = true;
        _isRightDockVisible = true;
        _isFocusModeEnabled = false;
        ApplyWorkspaceLayout();
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
    }

    private void OnViewportEntitiesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var entity in e.NewItems.OfType<MainWindowViewModel.ViewportEntity>())
            {
                entity.PropertyChanged += OnViewportEntityPropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (var entity in e.OldItems.OfType<MainWindowViewModel.ViewportEntity>())
            {
                entity.PropertyChanged -= OnViewportEntityPropertyChanged;
            }
        }

        RefreshViewportVisuals();
    }

    private void OnViewportEntityPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshViewportVisuals();
    }

    private void RefreshViewportVisuals()
    {
        if (_viewportCanvas is null)
        {
            return;
        }

        _viewportCanvas.Children.Clear();
        DrawViewportBackdrop();
        foreach (var entity in _viewModel.ViewportEntities)
        {
            var marker = BuildEntityMarker(entity);
            _viewportCanvas.Children.Add(marker);
            UpdateEntityMarkerPosition(marker, entity);
        }

        if (_viewModel.IsAssetDragGhostVisible)
        {
            var ghost = BuildAssetGhostMarker();
            _viewportCanvas.Children.Add(ghost);
            UpdateAssetGhostPosition(ghost);
        }
    }

    private void DrawViewportBackdrop()
    {
        if (_viewportCanvas is null)
        {
            return;
        }

        var width = _viewportCanvas.Bounds.Width;
        var height = _viewportCanvas.Bounds.Height;
        if (width <= 1 || height <= 1)
        {
            return;
        }

        const double gridWorldStep = 1.0;
        var gridPixelStep = _viewportZoom * gridWorldStep;
        if (gridPixelStep < 8)
        {
            return;
        }

        var centerX = width / 2.0;
        var centerY = height / 2.0;
        var verticalOffset = (centerX + (_viewportOriginWorldX * _viewportZoom)) % gridPixelStep;
        var horizontalOffset = (centerY - (_viewportOriginWorldY * _viewportZoom)) % gridPixelStep;
        var gridBrush = new SolidColorBrush(Color.Parse("#1B2740"));

        for (var x = verticalOffset; x <= width; x += gridPixelStep)
        {
            _viewportCanvas.Children.Add(new Line
            {
                StartPoint = new Point(x, 0),
                EndPoint = new Point(x, height),
                Stroke = gridBrush,
                StrokeThickness = 1,
                IsHitTestVisible = false,
            });
        }

        for (var y = horizontalOffset; y <= height; y += gridPixelStep)
        {
            _viewportCanvas.Children.Add(new Line
            {
                StartPoint = new Point(0, y),
                EndPoint = new Point(width, y),
                Stroke = gridBrush,
                StrokeThickness = 1,
                IsHitTestVisible = false,
            });
        }

        _viewportCanvas.Children.Add(new Line
        {
            StartPoint = new Point(centerX, 0),
            EndPoint = new Point(centerX, height),
            Stroke = new SolidColorBrush(Color.Parse("#375A89")),
            StrokeThickness = 1.4,
            IsHitTestVisible = false,
        });
        _viewportCanvas.Children.Add(new Line
        {
            StartPoint = new Point(0, centerY),
            EndPoint = new Point(width, centerY),
            Stroke = new SolidColorBrush(Color.Parse("#375A89")),
            StrokeThickness = 1.4,
            IsHitTestVisible = false,
        });
    }

    private Border BuildEntityMarker(MainWindowViewModel.ViewportEntity entity)
    {
        var markerWidth = Math.Max(16, entity.RenderWidth);
        var markerHeight = Math.Max(14, entity.RenderHeight);
        var markerContent = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
        };
        markerContent.Children.Add(new Border
        {
            Margin = new Thickness(3),
            BorderBrush = new SolidColorBrush(entity.IsSelected ? Color.Parse("#F2FBFF") : Color.Parse("#00000000")),
            BorderThickness = new Thickness(entity.IsSelected ? 1.1 : 0),
            CornerRadius = new CornerRadius(3),
            IsHitTestVisible = false,
        });

        var selectedLabel = new Border
        {
            Margin = new Thickness(3, 0, 3, 3),
            Padding = new Thickness(4, 1),
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.Parse("#7A0A1222")),
            IsVisible = entity.IsSelected,
            Child = new TextBlock
            {
                Text = entity.DisplayName,
                FontSize = 9,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
            }
        };
        Grid.SetRow(selectedLabel, 1);
        markerContent.Children.Add(selectedLabel);

        var marker = new Border
        {
            Width = markerWidth,
            Height = markerHeight,
            CornerRadius = new CornerRadius(4),
            Background = entity.RenderBrush,
            BorderBrush = new SolidColorBrush(entity.IsSelected ? Color.Parse("#B5DFFF") : Color.Parse("#2E3D54")),
            BorderThickness = entity.IsSelected ? new Thickness(2.4) : new Thickness(1.2),
            BoxShadow = entity.IsSelected
                ? new BoxShadows(new BoxShadow
                {
                    Blur = 18,
                    Spread = 1,
                    OffsetX = 0,
                    OffsetY = 0,
                    Color = Color.Parse("#805AB8FF"),
                })
                : default,
            Tag = entity.Id,
            Child = markerContent
        };

        ToolTip.SetTip(marker, $"{entity.DisplayName} ({entity.X:F2}, {entity.Y:F2}) • scale {entity.Scale:F2}");
        marker.PointerPressed += OnEntityPointerPressed;
        marker.ContextMenu = BuildEntityContextMenu(entity.Id);
        return marker;
    }

    private ContextMenu BuildEntityContextMenu(string entityId)
    {
        var editItem = new MenuItem { Header = "✏ Edit Properties", Tag = entityId };
        editItem.Click += OnViewportEditPropertiesClick;
        var deleteItem = new MenuItem { Header = "🗑 Delete Entity", Tag = entityId };
        deleteItem.Click += OnViewportDeleteEntityClick;
        return new ContextMenu
        {
            Items =
            {
                editItem,
                deleteItem,
            },
        };
    }

    private void UpdateViewportFocusVisual(bool isFocused)
    {
        if (_viewportFocusBorder is null)
        {
            return;
        }

        _viewportFocusBorder.BorderBrush = new SolidColorBrush(isFocused ? Color.Parse("#4AA3FF") : Color.Parse("#2B3446"));
        _viewportFocusBorder.BoxShadow = isFocused
            ? new BoxShadows(new BoxShadow
                {
                    Blur = 18,
                    Spread = 1,
                    OffsetX = 0,
                    OffsetY = 0,
                    Color = Color.Parse("#324AA3FF"),
                })
            : default;
    }

    private void UpdateEntityMarkerPosition(Control marker, MainWindowViewModel.ViewportEntity entity)
    {
        if (_viewportCanvas is null)
        {
            return;
        }

        var centerX = _viewportCanvas.Bounds.Width / 2.0;
        var centerY = _viewportCanvas.Bounds.Height / 2.0;
        var markerWidth = entity.RenderWidth;
        var markerHeight = entity.RenderHeight;
        var left = centerX + ((entity.X - _viewportOriginWorldX) * _viewportZoom) - (markerWidth / 2.0);
        var top = centerY - ((entity.Y - _viewportOriginWorldY) * _viewportZoom) - (markerHeight / 2.0);
        Canvas.SetLeft(marker, left);
        Canvas.SetTop(marker, top);
    }

    private void OnViewportPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewportCanvas is null)
        {
            return;
        }

        _viewportCanvas.Focus();
        if (e.Source is Control source && source.Tag is string)
        {
            return;
        }

        if (e.GetCurrentPoint(_viewportCanvas).Properties.IsRightButtonPressed)
        {
            return;
        }

        var modifiers = e.KeyModifiers;
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            _isMarqueeSelecting = true;
            _marqueeStart = e.GetPosition(_viewportCanvas);
            EnsureMarqueeVisual();
            e.Pointer.Capture(_viewportCanvas);
            e.Handled = true;
            return;
        }

        _isPanningViewport = true;
        _panPointerStart = e.GetPosition(_viewportCanvas);
        e.Pointer.Capture(_viewportCanvas);
        e.Handled = true;
    }

    private void OnEntityPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewportCanvas is null || sender is not Control marker || marker.Tag is not string entityId)
        {
            return;
        }

        if (e.GetCurrentPoint(marker).Properties.IsRightButtonPressed)
        {
            var changedSelection = _viewModel.BeginDirectPropertyEditForEntity(entityId);
            if (changedSelection)
            {
                RefreshViewportVisuals();
            }

            return;
        }

        var modifiers = e.KeyModifiers;
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            _viewModel.ToggleEntitySelection(entityId);
            RefreshViewportVisuals();
            e.Handled = true;
            return;
        }

        var selected = _viewModel.SelectSingleEntity(entityId);
        if (selected && e.ClickCount >= 2)
        {
            _viewModel.BeginDirectPropertyEditForEntity(entityId);
            FocusSceneEntityNameEditor();
            RefreshViewportVisuals();
            e.Handled = true;
            return;
        }

        if (!_viewModel.BeginDragForEntity(entityId))
        {
            return;
        }

        _draggingEntity = _viewModel.SelectedViewportEntity;
        if (_draggingEntity is null)
        {
            return;
        }

        _dragPointerStart = e.GetPosition(_viewportCanvas);
        _dragEntityStartX = _draggingEntity.X;
        _dragEntityStartY = _draggingEntity.Y;
        e.Pointer.Capture(_viewportCanvas);
        e.Handled = true;
    }

    private void OnViewportEditPropertiesClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string entityId } || string.IsNullOrWhiteSpace(entityId))
        {
            return;
        }

        if (_viewModel.BeginDirectPropertyEditForEntity(entityId))
        {
            FocusSceneEntityNameEditor();
            RefreshViewportVisuals();
        }
    }

    private void OnViewportDeleteEntityClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string entityId } || string.IsNullOrWhiteSpace(entityId))
        {
            return;
        }

        if (_viewModel.SelectSingleEntity(entityId))
        {
            _viewModel.DeleteSelectedEntityCommand.Execute(null);
        }
    }

    private async void OnHierarchyNodePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not string entityId || string.IsNullOrWhiteSpace(entityId))
        {
            return;
        }

        if (!_viewModel.SelectSingleEntity(entityId))
        {
            return;
        }

        if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var payload = new DataObject();
        payload.Set(HierarchyDragFormat, entityId);
        await DragDrop.DoDragDrop(e, payload, DragDropEffects.Move);
    }

    private void OnHierarchyNodeDragOver(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(HierarchyDragFormat))
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }

    private async void OnHierarchyNodeDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(HierarchyDragFormat) || sender is not Border border)
        {
            return;
        }

        var sourceEntityId = e.Data.Get(HierarchyDragFormat) as string;
        var targetEntityId = border.Tag as string;
        if (string.IsNullOrWhiteSpace(sourceEntityId))
        {
            return;
        }

        if (string.Equals(sourceEntityId, targetEntityId, StringComparison.Ordinal))
        {
            return;
        }

        await _viewModel.ReparentEntityAsync(sourceEntityId, targetEntityId);
        e.Handled = true;
    }

    private async void OnHierarchyRenameClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string entityId } || string.IsNullOrWhiteSpace(entityId))
        {
            return;
        }

        var current = _viewModel.ViewportEntities.FirstOrDefault(item => string.Equals(item.Id, entityId, StringComparison.Ordinal));
        if (current is null)
        {
            return;
        }

        var renamed = await ShowRenameHierarchyDialogAsync(current.DisplayName);
        if (string.IsNullOrWhiteSpace(renamed))
        {
            return;
        }

        await _viewModel.RenameHierarchyEntityAsync(entityId, renamed);
    }

    private async void OnHierarchyDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string entityId } || string.IsNullOrWhiteSpace(entityId))
        {
            return;
        }

        await _viewModel.DeleteHierarchyEntityAsync(entityId);
    }

    private async void OnHierarchyUngroupClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string entityId } || string.IsNullOrWhiteSpace(entityId))
        {
            return;
        }

        await _viewModel.UngroupHierarchyEntityAsync(entityId);
    }

    private async void OnHierarchyRootDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(HierarchyDragFormat))
        {
            return;
        }

        var sourceEntityId = e.Data.Get(HierarchyDragFormat) as string;
        if (string.IsNullOrWhiteSpace(sourceEntityId))
        {
            return;
        }

        await _viewModel.ReparentEntityAsync(sourceEntityId, targetEntityId: null);
        e.Handled = true;
    }

    private void OnViewportPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_viewportCanvas is null)
        {
            return;
        }

        if (_isPanningViewport && e.GetCurrentPoint(_viewportCanvas).Properties.IsLeftButtonPressed)
        {
            var current = e.GetPosition(_viewportCanvas);
            _viewportOriginWorldX -= (current.X - _panPointerStart.X) / _viewportZoom;
            _viewportOriginWorldY += (current.Y - _panPointerStart.Y) / _viewportZoom;
            _panPointerStart = current;
            RefreshViewportVisuals();
            e.Handled = true;
            return;
        }

        if (_isMarqueeSelecting && _marqueeVisual is not null && e.GetCurrentPoint(_viewportCanvas).Properties.IsLeftButtonPressed)
        {
            var marqueeCurrent = e.GetPosition(_viewportCanvas);
            DrawMarquee(_marqueeStart, marqueeCurrent);
            e.Handled = true;
            return;
        }

        if (_draggingEntity is null || !e.GetCurrentPoint(_viewportCanvas).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var dragCurrent = e.GetPosition(_viewportCanvas);
        var deltaX = (float)((dragCurrent.X - _dragPointerStart.X) / _viewportZoom);
        var deltaY = (float)((_dragPointerStart.Y - dragCurrent.Y) / _viewportZoom);
        var nextX = _dragEntityStartX + deltaX;
        var nextY = _dragEntityStartY + deltaY;
        if (_viewModel.PreviewDragPosition(_draggingEntity.Id, nextX, nextY))
        {
            RefreshViewportVisuals();
        }
    }

    private async void OnViewportPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_viewportCanvas is null)
        {
            return;
        }

        if (_isPanningViewport)
        {
            _isPanningViewport = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_isMarqueeSelecting)
        {
            var current = e.GetPosition(_viewportCanvas);
            CompleteMarqueeSelection(_marqueeStart, current, e.KeyModifiers.HasFlag(KeyModifiers.Control));
            _isMarqueeSelecting = false;
            HideMarquee();
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_draggingEntity is null)
        {
            return;
        }

        _draggingEntity = null;
        e.Pointer.Capture(null);
        await _viewModel.CommitDragAsync();
        RefreshViewportVisuals();
    }

    private void OnViewportPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_viewportCanvas is null)
        {
            return;
        }

        var before = ScreenToWorld(e.GetPosition(_viewportCanvas));
        var zoomFactor = e.Delta.Y > 0 ? 1.1 : 1 / 1.1;
        _viewportZoom = Math.Clamp(_viewportZoom * zoomFactor, 12d, 120d);
        var after = ScreenToWorld(e.GetPosition(_viewportCanvas));
        _viewportOriginWorldX += before.X - after.X;
        _viewportOriginWorldY += before.Y - after.Y;
        RefreshViewportVisuals();
        e.Handled = true;
    }

    private static Color ParseColor(string? candidate, string fallback)
    {
        try
        {
            return Color.Parse(string.IsNullOrWhiteSpace(candidate) ? fallback : candidate);
        }
        catch
        {
            return Color.Parse(fallback);
        }
    }

    private void EnsureMarqueeVisual()
    {
        if (_viewportCanvas is null)
        {
            return;
        }

        _marqueeVisual ??= new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#73B8FF")),
            BorderThickness = new Thickness(1.5),
            Background = new SolidColorBrush(Color.Parse("#334AA3FF")),
            IsVisible = false,
        };

        if (!_viewportCanvas.Children.Contains(_marqueeVisual))
        {
            _viewportCanvas.Children.Add(_marqueeVisual);
        }
    }

    private void DrawMarquee(Point start, Point current)
    {
        if (_viewportCanvas is null || _marqueeVisual is null)
        {
            return;
        }

        var left = Math.Min(start.X, current.X);
        var top = Math.Min(start.Y, current.Y);
        var width = Math.Abs(start.X - current.X);
        var height = Math.Abs(start.Y - current.Y);

        _marqueeVisual.IsVisible = true;
        _marqueeVisual.Width = width;
        _marqueeVisual.Height = height;
        Canvas.SetLeft(_marqueeVisual, left);
        Canvas.SetTop(_marqueeVisual, top);
    }

    private void CompleteMarqueeSelection(Point start, Point current, bool append)
    {
        if (_viewportCanvas is null)
        {
            return;
        }

        var minScreenX = Math.Min(start.X, current.X);
        var maxScreenX = Math.Max(start.X, current.X);
        var minScreenY = Math.Min(start.Y, current.Y);
        var maxScreenY = Math.Max(start.Y, current.Y);

        if (Math.Abs(maxScreenX - minScreenX) < 2 && Math.Abs(maxScreenY - minScreenY) < 2)
        {
            if (!append)
            {
                _viewModel.ClearSelection();
            }
            RefreshViewportVisuals();
            return;
        }

        var centerX = _viewportCanvas.Bounds.Width / 2.0;
        var centerY = _viewportCanvas.Bounds.Height / 2.0;
        var minWorldX = (float)(_viewportOriginWorldX + ((minScreenX - centerX) / _viewportZoom));
        var maxWorldX = (float)(_viewportOriginWorldX + ((maxScreenX - centerX) / _viewportZoom));
        var minWorldY = (float)(_viewportOriginWorldY + ((centerY - maxScreenY) / _viewportZoom));
        var maxWorldY = (float)(_viewportOriginWorldY + ((centerY - minScreenY) / _viewportZoom));

        _viewModel.SelectEntitiesByViewportRect(minWorldX, minWorldY, maxWorldX, maxWorldY, append);
        RefreshViewportVisuals();
    }

    private void HideMarquee()
    {
        if (_marqueeVisual is null)
        {
            return;
        }

        _marqueeVisual.IsVisible = false;
        _marqueeVisual.Width = 0;
        _marqueeVisual.Height = 0;
    }


    private void OnViewportDragOver(object? sender, DragEventArgs e)
    {
        if (_viewportCanvas is null)
        {
            return;
        }

        var assetId = e.Data.Get(AssetDragFormat) as string;
        if (string.IsNullOrWhiteSpace(assetId) && e.Data.Contains(DataFormats.Text))
        {
            assetId = e.Data.GetText();
        }

        if (!string.IsNullOrWhiteSpace(assetId))
        {
            var world = ScreenToWorld(e.GetPosition(_viewportCanvas));
            _viewModel.SetAssetDragGhost(assetId, world.X, world.Y);
            var isValidPlacement = _viewModel.HasActiveScenePath;
            _viewModel.SetAssetDragGhostPlacementValidity(isValidPlacement);
            RefreshViewportVisuals();
            e.DragEffects = isValidPlacement ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
            return;
        }

        _viewModel.ClearAssetDragGhost();
        RefreshViewportVisuals();
        e.DragEffects = DragDropEffects.None;
    }

    private void OnViewportDragLeave(object? sender, RoutedEventArgs e)
    {
        _viewModel.ClearAssetDragGhost();
        RefreshViewportVisuals();
    }

    private async void OnViewportDrop(object? sender, DragEventArgs e)
    {
        if (_viewportCanvas is null)
        {
            return;
        }

        var assetId = e.Data.Get(AssetDragFormat) as string;
        if (string.IsNullOrWhiteSpace(assetId) && e.Data.Contains(DataFormats.Text))
        {
            assetId = e.Data.GetText();
        }

        if (string.IsNullOrWhiteSpace(assetId))
        {
            _viewModel.ClearAssetDragGhost();
            RefreshViewportVisuals();
            return;
        }

        if (!_viewModel.HasActiveScenePath)
        {
            _viewModel.ClearAssetDragGhost();
            _viewModel.SetStatusMessage("Open or create a scene before placing assets.");
            RefreshViewportVisuals();
            return;
        }

        var world = ScreenToWorld(e.GetPosition(_viewportCanvas));
        await _viewModel.PlaceImportedAssetInSceneAsync(assetId, world.X, world.Y);
        _viewModel.ClearAssetDragGhost();
        RefreshViewportVisuals();
    }

    private (float X, float Y) ScreenToWorld(Point point)
    {
        if (_viewportCanvas is null)
        {
            return (0f, 0f);
        }

        var centerX = _viewportCanvas.Bounds.Width / 2.0;
        var centerY = _viewportCanvas.Bounds.Height / 2.0;
        var worldX = (float)(_viewportOriginWorldX + ((point.X - centerX) / _viewportZoom));
        var worldY = (float)(_viewportOriginWorldY + ((centerY - point.Y) / _viewportZoom));
        return (worldX, worldY);
    }

    private async void OnAssetChipPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || control.Tag is not string assetId || string.IsNullOrWhiteSpace(assetId))
        {
            return;
        }

        var selected = _viewModel.ImportedAssets.FirstOrDefault(asset => string.Equals(asset.Id, assetId, StringComparison.Ordinal));
        _viewModel.SelectedImportedAsset = selected;
        _viewModel.BeginAssetBrowserDragPreview(assetId);
        _viewModel.UpdateAssetBrowserDragPreviewPosition(e.GetPosition(this).X, e.GetPosition(this).Y);

        var data = new DataObject();
        data.Set(AssetDragFormat, assetId);
        data.Set(DataFormats.Text, assetId);

        await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
        _viewModel.EndAssetBrowserDragPreview();
        _viewModel.ClearAssetDragGhost();
        RefreshViewportVisuals();
    }

    private async void OnImportAssetClick(object? sender, RoutedEventArgs e)
    {
        if (StorageProvider is null)
        {
            _viewModel.SetStatusMessage("Storage provider unavailable for asset import.");
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Asset (PNG/OBJ)",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Soul Loom Assets")
                {
                    Patterns = ["*.png", "*.obj"],
                },
            ],
        });

        var picked = files.FirstOrDefault();
        if (picked is null)
        {
            return;
        }

        await _viewModel.ImportAssetAsync(picked.Path.LocalPath);
    }

    private async void OnRefreshAssetsClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshImportedAssetsAsync();
        await _viewModel.RefreshAssetLibraryAsync();
    }

    private async void OnRefreshGeneratedReviewQueueClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshGeneratedAssetReviewQueueAsync();
    }

    private async void OnApproveGeneratedAssetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string assetPath } || string.IsNullOrWhiteSpace(assetPath))
        {
            return;
        }

        await _viewModel.ReviewGeneratedAssetAsync(assetPath, "approve");
    }

    private async void OnRejectGeneratedAssetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string assetPath } || string.IsNullOrWhiteSpace(assetPath))
        {
            return;
        }

        await _viewModel.ReviewGeneratedAssetAsync(assetPath, "reject");
    }

    private async void OnRegenerateGeneratedAssetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string assetPath } || string.IsNullOrWhiteSpace(assetPath))
        {
            return;
        }

        await _viewModel.ReviewGeneratedAssetAsync(assetPath, "regenerate");
    }

    private async void OnGenerateAssetClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.GenerateAssetAsync();
    }

    private void OnApprovedAssetsTabClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.ActiveAssetLibraryTab = "Approved";
    }

    private void OnPendingAssetsTabClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.ActiveAssetLibraryTab = "Pending";
    }

    private void OnRejectedAssetsTabClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.ActiveAssetLibraryTab = "Rejected";
    }

    private void OnAssetLibrarySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: MainWindowViewModel.AssetLibraryItem item })
        {
            _viewModel.SelectedAssetLibraryItem = item;
        }
    }

    private async void OnApproveSelectedAssetClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.ApproveSelectedAssetAsync();
    }

    private async void OnRejectSelectedAssetClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.RejectSelectedAssetAsync();
    }

    private async void OnRegenerateSelectedAssetClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.RegenerateSelectedAssetAsync();
    }

    private async void OnDeleteSelectedAssetClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.DeleteSelectedAssetAsync();
    }

    private void OnAssetsPanelSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _viewModel.IsAssetPreviewStacked = e.NewSize.Width < 700;
    }

    private void OnDockAssetsLeftClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.AssetsPanelPlacement = MainWindowViewModel.PanelPlacementLeft;
    }

    private void OnDockAssetsRightClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.AssetsPanelPlacement = MainWindowViewModel.PanelPlacementRight;
    }

    private void OnDockAssetsBottomClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.AssetsPanelPlacement = MainWindowViewModel.PanelPlacementBottom;
    }

    private void OnFloatAssetsClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.AssetsPanelPlacement = MainWindowViewModel.PanelPlacementFloat;
    }

    private void OnHideAssetsClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.IsAssetsTabActive)
        {
            _viewModel.SetLeftPanelTabCommand.Execute("Hierarchy");
        }

        _viewModel.AssetsPanelPlacement = MainWindowViewModel.PanelPlacementHidden;
    }

    private void OnAssetGridViewClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.AssetLibraryViewMode = "Grid";
    }

    private void OnAssetListViewClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.AssetLibraryViewMode = "List";
    }

    private void OnShowAiInterviewSuggestionsClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.ShowAiInterviewSuggestions();
    }

    private void OnAiInterviewSuggestionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string suggestion })
        {
            _viewModel.ApplyAiInterviewSuggestion(suggestion);
        }
    }

    private void OnSendAiInterviewAnswerClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.SubmitAiInterviewAnswer();
    }

    private void OnDockAiInterviewRightClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.AiInterviewPlacement = MainWindowViewModel.PanelPlacementRight;
    }

    private void OnDockAiInterviewBottomClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.AiInterviewPlacement = MainWindowViewModel.PanelPlacementBottom;
    }

    private void OnFloatAiInterviewClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.AiInterviewPlacement = MainWindowViewModel.PanelPlacementFloat;
    }

    private void OnHideAiInterviewClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.AiInterviewPlacement = MainWindowViewModel.PanelPlacementHidden;
    }

    private void OnWindowAssetDragOver(object? sender, DragEventArgs e)
    {
        var assetId = e.Data.Get(AssetDragFormat) as string;
        if (string.IsNullOrWhiteSpace(assetId) && e.Data.Contains(DataFormats.Text))
        {
            assetId = e.Data.GetText();
        }

        if (string.IsNullOrWhiteSpace(assetId) || !_viewModel.IsAssetBrowserDragPreviewVisible)
        {
            return;
        }

        var point = e.GetPosition(this);
        _viewModel.UpdateAssetBrowserDragPreviewPosition(point.X, point.Y);
    }

    private void OnWindowAssetDrop(object? sender, DragEventArgs e)
    {
        _viewModel.EndAssetBrowserDragPreview();
    }

    private void OnWindowAssetDragLeave(object? sender, RoutedEventArgs e)
    {
        _viewModel.EndAssetBrowserDragPreview();
    }

    private Border BuildAssetGhostMarker()
    {
        var hasPreview = !string.IsNullOrWhiteSpace(_viewModel.AssetDragGhostPreviewPath)
            && File.Exists(_viewModel.AssetDragGhostPreviewPath);
        var isValidPlacement = _viewModel.IsAssetDragGhostPlacementValid;
        var fillColor = isValidPlacement ? Color.FromArgb(118, 68, 193, 120) : Color.FromArgb(118, 215, 84, 84);
        var borderColor = isValidPlacement ? Color.Parse("#7FE6A2") : Color.Parse("#FF9393");
        var marker = new Border
        {
            Width = MarkerSize * 1.15,
            Height = MarkerSize * 1.15,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(fillColor),
            BorderBrush = new SolidColorBrush(borderColor),
            BorderThickness = new Thickness(1.8),
            Opacity = 0.72,
            Child = hasPreview
                ? new Image
                {
                    Source = new Avalonia.Media.Imaging.Bitmap(_viewModel.AssetDragGhostPreviewPath),
                    Stretch = Stretch.UniformToFill,
                }
                : new TextBlock
                {
                    Text = "🧊",
                    FontSize = 18,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                },
        };

        ToolTip.SetTip(marker, isValidPlacement
            ? $"Place: {_viewModel.AssetDragGhostTitle}"
            : "Open or create a scene before placing this asset.");
        return marker;
    }

    private void SyncDetachedPanelPlacements()
    {
        SyncAssetsPanelPlacement();
        SyncAiInterviewPlacement();
    }

    private void SyncAssetsPanelPlacement()
    {
        if (_assetsPanelHost is null)
        {
            return;
        }

        if (_assetsDockTab is not null)
        {
            _assetsDockTab.IsVisible = _viewModel.IsAssetsPanelInBottom;
        }

        switch (_viewModel.AssetsPanelPlacement)
        {
            case MainWindowViewModel.PanelPlacementLeft:
                CloseAssetsFloatingWindow(keepPlacement: true);
                AttachContentToHost(_assetsPanelHost, _assetsPanelLeftHost);
                _viewModel.SetLeftPanelTabCommand.Execute("Assets");
                _isLeftDockVisible = true;
                _isFocusModeEnabled = false;
                break;
            case MainWindowViewModel.PanelPlacementRight:
                CloseAssetsFloatingWindow(keepPlacement: true);
                AttachContentToHost(_assetsPanelHost, _assetsPanelRightHost);
                _isRightDockVisible = true;
                _isFocusModeEnabled = false;
                break;
            case MainWindowViewModel.PanelPlacementBottom:
                CloseAssetsFloatingWindow(keepPlacement: true);
                AttachContentToHost(_assetsPanelHost, _assetsPanelBottomHost);
                if (_assetsDockTab is not null)
                {
                    _assetsDockTab.IsVisible = true;
                    _assetsDockTab.IsSelected = true;
                }

                _isActivityDockVisible = true;
                _isFocusModeEnabled = false;
                break;
            case MainWindowViewModel.PanelPlacementFloat:
                ShowAssetsFloatingWindow();
                break;
            default:
                CloseAssetsFloatingWindow(keepPlacement: true);
                DetachFromParent(_assetsPanelHost);
                break;
        }

        ApplyWorkspaceLayout();
    }

    private void SyncAiInterviewPlacement()
    {
        if (_aiInterviewPanelHost is null)
        {
            return;
        }

        if (_aiInterviewDockTab is not null)
        {
            _aiInterviewDockTab.IsVisible = _viewModel.IsAiInterviewInBottom;
        }

        switch (_viewModel.AiInterviewPlacement)
        {
            case MainWindowViewModel.PanelPlacementRight:
                CloseAiInterviewFloatingWindow(keepPlacement: true);
                AttachContentToHost(_aiInterviewPanelHost, _aiInterviewRightHost);
                _isRightDockVisible = true;
                _isFocusModeEnabled = false;
                break;
            case MainWindowViewModel.PanelPlacementBottom:
                CloseAiInterviewFloatingWindow(keepPlacement: true);
                AttachContentToHost(_aiInterviewPanelHost, _aiInterviewBottomHost);
                if (_aiInterviewDockTab is not null)
                {
                    _aiInterviewDockTab.IsVisible = true;
                    _aiInterviewDockTab.IsSelected = true;
                }

                _isActivityDockVisible = true;
                _isFocusModeEnabled = false;
                break;
            case MainWindowViewModel.PanelPlacementFloat:
                ShowAiInterviewFloatingWindow();
                break;
            default:
                CloseAiInterviewFloatingWindow(keepPlacement: true);
                DetachFromParent(_aiInterviewPanelHost);
                break;
        }

        ApplyWorkspaceLayout();
    }

    private void ShowAssetsFloatingWindow()
    {
        if (_assetsPanelHost is null)
        {
            return;
        }

        DetachFromParent(_assetsPanelHost);
        if (_assetsFloatingWindow is null)
        {
            _assetsFloatingWindow = BuildToolWindow("Assets", 860, 700);
            _assetsFloatingWindow.Closed += OnAssetsFloatingWindowClosed;
        }

        _assetsFloatingWindow.DataContext = _viewModel;
        _assetsFloatingWindow.Content = _assetsPanelHost;
        if (!_assetsFloatingWindow.IsVisible)
        {
            _assetsFloatingWindow.Show(this);
        }

        _assetsFloatingWindow.Activate();
    }

    private void ShowAiInterviewFloatingWindow()
    {
        if (_aiInterviewPanelHost is null)
        {
            return;
        }

        DetachFromParent(_aiInterviewPanelHost);
        if (_aiInterviewFloatingWindow is null)
        {
            _aiInterviewFloatingWindow = BuildToolWindow("AI Interview", 760, 620);
            _aiInterviewFloatingWindow.Closed += OnAiInterviewFloatingWindowClosed;
        }

        _aiInterviewFloatingWindow.DataContext = _viewModel;
        _aiInterviewFloatingWindow.Content = _aiInterviewPanelHost;
        if (!_aiInterviewFloatingWindow.IsVisible)
        {
            _aiInterviewFloatingWindow.Show(this);
        }

        _aiInterviewFloatingWindow.Activate();
    }

    private Window BuildToolWindow(string title, double width, double height)
    {
        return new Window
        {
            Title = $"ForgeEngine Editor - {title}",
            Width = width,
            Height = height,
            MinWidth = Math.Min(width, 520),
            MinHeight = Math.Min(height, 420),
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Background,
        };
    }

    private void OnAssetsFloatingWindowClosed(object? sender, EventArgs e)
    {
        if (_assetsFloatingWindow is not null && ReferenceEquals(_assetsFloatingWindow.Content, _assetsPanelHost))
        {
            _assetsFloatingWindow.Content = null;
        }

        _assetsFloatingWindow = null;
        if (_suppressAssetsFloatingWindowClose)
        {
            return;
        }

        if (_viewModel.IsAssetsTabActive)
        {
            _viewModel.SetLeftPanelTabCommand.Execute("Hierarchy");
        }

        _viewModel.AssetsPanelPlacement = MainWindowViewModel.PanelPlacementHidden;
    }

    private void OnAiInterviewFloatingWindowClosed(object? sender, EventArgs e)
    {
        if (_aiInterviewFloatingWindow is not null && ReferenceEquals(_aiInterviewFloatingWindow.Content, _aiInterviewPanelHost))
        {
            _aiInterviewFloatingWindow.Content = null;
        }

        _aiInterviewFloatingWindow = null;
        if (_suppressAiInterviewFloatingWindowClose)
        {
            return;
        }

        _viewModel.AiInterviewPlacement = MainWindowViewModel.PanelPlacementHidden;
    }

    private void CloseAssetsFloatingWindow(bool keepPlacement)
    {
        if (_assetsFloatingWindow is null)
        {
            return;
        }

        _suppressAssetsFloatingWindowClose = keepPlacement;
        if (ReferenceEquals(_assetsFloatingWindow.Content, _assetsPanelHost))
        {
            _assetsFloatingWindow.Content = null;
        }

        _assetsFloatingWindow.Close();
        _assetsFloatingWindow = null;
        _suppressAssetsFloatingWindowClose = false;
    }

    private void CloseAiInterviewFloatingWindow(bool keepPlacement)
    {
        if (_aiInterviewFloatingWindow is null)
        {
            return;
        }

        _suppressAiInterviewFloatingWindowClose = keepPlacement;
        if (ReferenceEquals(_aiInterviewFloatingWindow.Content, _aiInterviewPanelHost))
        {
            _aiInterviewFloatingWindow.Content = null;
        }

        _aiInterviewFloatingWindow.Close();
        _aiInterviewFloatingWindow = null;
        _suppressAiInterviewFloatingWindowClose = false;
    }

    private static void AttachContentToHost(Control content, ContentControl? host)
    {
        if (host is null)
        {
            return;
        }

        if (ReferenceEquals(host.Content, content))
        {
            return;
        }

        DetachFromParent(content);
        host.Content = content;
    }

    private static void DetachFromParent(Control content)
    {
        switch (content.Parent)
        {
            case ContentControl contentControl when ReferenceEquals(contentControl.Content, content):
                contentControl.Content = null;
                break;
            case Border border when ReferenceEquals(border.Child, content):
                border.Child = null;
                break;
            case Panel panel:
                panel.Children.Remove(content);
                break;
        }
    }

    private void UpdateAssetGhostPosition(Control marker)
    {
        if (_viewportCanvas is null)
        {
            return;
        }

        var centerX = _viewportCanvas.Bounds.Width / 2.0;
        var centerY = _viewportCanvas.Bounds.Height / 2.0;
        var markerSize = marker.Width;
        var left = centerX + ((_viewModel.AssetDragGhostWorldX - _viewportOriginWorldX) * _viewportZoom) - (markerSize / 2.0);
        var top = centerY - ((_viewModel.AssetDragGhostWorldY - _viewportOriginWorldY) * _viewportZoom) - (markerSize / 2.0);
        Canvas.SetLeft(marker, left);
        Canvas.SetTop(marker, top);
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


    private void OnSystemTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control && control.Tag is string tab)
        {
            RememberCurrentDockSizes();
            _isFocusModeEnabled = false;
            _isRightDockVisible = true;
            _isTopRibbonVisible = true;
            _viewModel.IsAdvancedInspectorEnabled = !string.Equals(tab, "Settings", StringComparison.Ordinal)
                && !string.Equals(tab, "Models", StringComparison.Ordinal);
            if (string.Equals(tab, "Models", StringComparison.Ordinal))
            {
                _isTimelineDockVisible = false;
                _isActivityDockVisible = false;
                _viewModel.IsCodeMode = false;
            }
            _viewModel.SetSystemTab(tab);
            ApplyWorkspaceLayout();
        }
    }

    private async void OnApplyDayNightClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.ApplyDayNightAsync();
    }

    private async void OnApplyWeatherClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.ApplyWeatherAsync();
    }

    private async void OnSaveLivingNpcsSettingsClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.SaveLivingNpcsSettingsAsync();
    }

    private async void OnReseedLivingNpcDefaultsClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.ReseedLivingNpcDefaultsAsync();
    }

    private async void OnAssignScriptedBehaviorClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.AssignScriptedBehaviorToSelectionAsync();
    }

    private async void OnRefreshScriptedBehaviorsClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshScriptedBehaviorCatalogAsync();
    }

    private async void OnApplyBuildableClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.ApplyBuildableSelectionAsync();
    }

    private async void OnAddRecipeClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.UpsertRecipeAsync();
    }

    private async void OnApplyDialogDraftClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.ApplyDialogDraftAsync();
    }

    private async void OnGenerateNpcClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.RunAiHookAsync("add-npc", "Generated NPC", "villager");
    }

    private async void OnAdd3HousesClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.AiPromptEditor = "Add 3 Houses";
        await _viewModel.RunAiHookAsync("modify-scene");
    }

    private async void OnModifyScenePromptClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.RunAiHookAsync("modify-scene");
    }

    private async void OnGenerateKitBashClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.RunAiHookAsync("kit-bash-scene");
    }

    private async void OnGenerateLootClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.RunAiHookAsync("generate-loot");
    }

    private async void OnLiveEditSceneClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.RunAiHookAsync("edit-scene");
    }

    private async void OnDownloadManagedModelClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string friendlyName)
        {
            return;
        }

        await _viewModel.DownloadManagedModelAsync(friendlyName);
    }

    private async void OnRetryManagedModelClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string friendlyName)
        {
            return;
        }

        await _viewModel.DownloadManagedModelAsync(friendlyName);
    }

    private async void OnRunModelOnboardingClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.RunModelOnboardingAsync();
    }

    private async void OnQuickStartModelsClick(object? sender, RoutedEventArgs e)
    {
        var completed = await _viewModel.RunQuickStartSetupAsync();
        if (completed)
        {
            await ShowQuickSetupSummaryDialogAsync();
        }
    }

    private async void OnSetupRecommendedModelsClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.SetupRecommendedModelsAsync();
    }

    private async void OnSetupFreeWillModelClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.SetupFreeWillModelAsync();
    }

    private async void OnRemoveManagedModelClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string friendlyName || string.IsNullOrWhiteSpace(friendlyName))
        {
            return;
        }

        var confirmed = await ShowModelRemovalConfirmationAsync(friendlyName);
        if (!confirmed)
        {
            _viewModel.SetStatusMessage($"Canceled removal for {friendlyName}.");
            return;
        }

        await _viewModel.RemoveManagedModelAsync(friendlyName);
    }

    private async void OnRefreshModelsClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshModelManagerAsync();
    }

    private void OnCancelModelDownloadClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.CancelActiveModelOperation();
    }

    private async void OnRetryModelOperationClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.RetryLastModelOperationAsync();
    }

    private void OnDismissModelErrorClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.DismissModelErrorDialog();
    }

    private async void OnRunOptimizationCheckClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.RunOptimizationCheckAsync();
    }

    private async void OnOptimizeProjectOneClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.OptimizeProjectOneClickAsync();
    }

    private async void OnSwitchLightweightModeClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.SwitchToLightweightModeAsync();
    }

    private void OnPreviewOptimizationSuggestionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string suggestionId)
        {
            _viewModel.PreviewOptimizationSuggestion(suggestionId);
        }
    }

    private async void OnApplyOptimizationSuggestionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string suggestionId)
        {
            await _viewModel.ApplyOptimizationSuggestionAsync(suggestionId);
        }
    }

    private void OnIgnoreOptimizationSuggestionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string suggestionId)
        {
            _viewModel.IgnoreOptimizationSuggestion(suggestionId);
        }
    }

    private async void OnRender2DClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.SetRenderModeAsync("2D");
    }

    private async void OnRender3DClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.SetRenderModeAsync("3D");
    }

    private async void OnSaveCoCreatorContextClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.SaveCoCreatorSettingsAsync();
    }

    private async void OnRefreshCoCreatorClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshCoCreatorSuggestionsAsync();
    }


    private async void OnRebuildNavmeshClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.RebuildNavmeshAsync();
    }

    private async void OnEvolveSelectedNpcDialogClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.EvolveSelectedNpcDialogAsync();
    }

    private void OnToggleCoCreatorLiveClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.SetCoCreatorLive(!_viewModel.CoCreatorLiveEnabled);
    }

    private async void OnApplyRelationshipClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.ApplyRelationshipEditsAsync();
    }

    private async void OnAcceptCoCreatorSuggestionClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.AcceptCoCreatorSuggestionAsync();
    }

    private void OnRejectCoCreatorSuggestionClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.RejectCoCreatorSuggestion();
    }

    private async void OnFactionRepIncreaseClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.AdjustFactionReputationAsync(1f);
    }

    private async void OnFactionRepDecreaseClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.AdjustFactionReputationAsync(-1f);
    }

    private async void OnToggleCombatModeClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.ToggleCombatModeAsync();
    }

    private async void OnToggleRealtimeCombatSelectionClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.ToggleRealtimeCombatForSelectionAsync();
    }

    private async void OnSaveStoryBibleClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.SaveStoryBibleAsync();
    }

    private async void OnSaveNarratorSettingsClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.SaveNarratorSettingsAsync();
    }

    private async void OnSaveSelectedNpcVoiceClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.SaveSelectedCharacterVoiceProfileAsync();
    }

    private async void OnUpsertStoryBeatClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.UpsertStoryBeatAsync();
    }

    private async void OnQueueStoryEventClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.QueueStoryEventAsync();
    }

    private void OnStoryBeatSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0)
        {
            return;
        }

        if (e.AddedItems[0] is StoryBeatRow beat)
        {
            _viewModel.SelectStoryBeatForEditing(beat);
        }
    }

    private void OnEditStorySuggestionClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.StageSelectedStorySuggestionForEdit();
    }

    private async void OnAcceptStorySuggestionClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.AcceptStoryBeatSuggestionAsync();
    }

    private void OnRejectStorySuggestionClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.RejectStoryBeatSuggestion();
    }

    private async void OnNewProjectClick(object? sender, RoutedEventArgs e)
    {
        var wizard = new NewProjectWizardWindow(_viewModel.EditorDefaultTemplateId);
        await wizard.ShowDialog(this);
        if (wizard.Result is null)
        {
            return;
        }

        await _viewModel.CreateProjectFromTemplateAsync(
            wizard.Result.Template,
            wizard.Result.ProjectName,
            wizard.Result.ConceptNotes);
    }

    private async void OnNewSceneClick(object? sender, RoutedEventArgs e)
    {
        var projectRoot = _viewModel.HasProjectRootPath ? _viewModel.ProjectRootPath : string.Empty;
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            if (StorageProvider is null)
            {
                _viewModel.SetStatusMessage("Storage provider unavailable for scene creation.");
                return;
            }

            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Project Root",
                AllowMultiple = false,
            });
            projectRoot = folders.FirstOrDefault()?.Path.LocalPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return;
            }
        }

        await _viewModel.CreateNewSceneAsync(projectRoot);
        RefreshViewportVisuals();
    }

    private async void OnOpenSceneClick(object? sender, RoutedEventArgs e)
    {
        if (StorageProvider is null)
        {
            _viewModel.SetStatusMessage("Storage provider unavailable for scene open.");
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Scene",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("ForgeEngine Scene")
                {
                    Patterns = ["*.scene.json", "scene_scaffold.json"],
                },
            ],
        });

        var picked = files.FirstOrDefault();
        if (picked is null)
        {
            return;
        }

        await _viewModel.OpenSceneAsync(picked.Path.LocalPath);
        RefreshViewportVisuals();
    }

    private async void OnSaveSceneClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.SaveSceneAsync();
    }

    private async void OnSaveSceneAsClick(object? sender, RoutedEventArgs e)
    {
        if (StorageProvider is null)
        {
            _viewModel.SetStatusMessage("Storage provider unavailable for scene save.");
            return;
        }

        var target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Scene As",
            SuggestedFileName = _viewModel.HasActiveScenePath ? System.IO.Path.GetFileName(_viewModel.ActiveScenePath) : "Untitled Scene.scene.json",
            FileTypeChoices =
            [
                new FilePickerFileType("ForgeEngine Scene")
                {
                    Patterns = ["*.scene.json"],
                },
            ],
            ShowOverwritePrompt = true,
        });

        if (target is null)
        {
            return;
        }

        await _viewModel.SaveSceneAsAsync(target.Path.LocalPath);
    }

    private void OnOpenProjectFolderClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.OpenProjectFolder();
    }

    private async void OnExitClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.IsSceneDirty)
        {
            var decision = await ShowSaveChangesPromptAsync();
            if (decision == "Cancel")
            {
                return;
            }

            if (decision == "Save")
            {
                await _viewModel.SaveSceneAsync();
            }
        }

        Close();
    }

    private async void OnOpenProjectClick(object? sender, RoutedEventArgs e)
    {
        if (StorageProvider is null)
        {
            _viewModel.SetStatusMessage("Storage provider unavailable for project open.");
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Soul Loom Project",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Soul Loom Project")
                {
                    Patterns = ["*.gfproj.json", "*.json"],
                },
            ],
        });

        var picked = files.FirstOrDefault();
        if (picked is null)
        {
            return;
        }

        await _viewModel.OpenProjectStateAsync(picked.Path.LocalPath);
        RefreshViewportVisuals();
    }

    private async void OnSaveProjectClick(object? sender, RoutedEventArgs e)
    {
        if (StorageProvider is null)
        {
            _viewModel.SetStatusMessage("Storage provider unavailable for project save.");
            return;
        }

        if (_viewModel.HasActiveProjectFilePath)
        {
            await _viewModel.SaveProjectStateToActivePathAsync();
            return;
        }

        var target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Soul Loom Project",
            SuggestedFileName = "soul-loom-project.gfproj.json",
            FileTypeChoices =
            [
                new FilePickerFileType("Soul Loom Project")
                {
                    Patterns = ["*.gfproj.json"],
                },
            ],
            ShowOverwritePrompt = true,
        });

        if (target is null)
        {
            return;
        }

        await _viewModel.SaveProjectStateAsync(target.Path.LocalPath);
    }

    private async void OnGenerateFromBriefClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.GenerateFromBriefAsync(launchRuntime: false);
    }

    private async void OnPlayRuntimeClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.PlayRuntimeAsync();
    }

    private async void OnGenerateAndPlayClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.GenerateFromBriefAsync(launchRuntime: true);
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
                    _viewModel.ForgeGuardKeepInstalledMessage,
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
            settingsWindow.DownloadForgeGuardRequested += async () =>
            {
                await _viewModel.DownloadManagedModelAsync("forgeguard");
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
                _viewModel.ForgeGuardKeepInstalledMessage,
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

    private void OnOpenExportChecklistClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.OpenExportChecklist();
    }

    private async void OnRunExportChecklistClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.RunExportChecklistAsync();
    }

    private void OnOpenExportFolderClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.OpenExportFolderPath();
    }

    private void OnOpenExportZipClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.OpenExportPackagePath();
    }

    private async void OnPublishToSteamClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.RunPublishToSteamDryRunAsync(userConfirmed: false);
        var confirmed = await ShowPublishDryRunConfirmationAsync();
        if (!confirmed)
        {
            return;
        }

        await _viewModel.RunPublishToSteamDryRunAsync(userConfirmed: true);
    }

    private async void OnUploadToSteamStubClick(object? sender, RoutedEventArgs e)
    {
        var confirmed = await ShowUploadToSteamStubConfirmationAsync();
        if (!confirmed)
        {
            return;
        }

        await _viewModel.RunSteamUploadStubAsync();
    }

    private async void OnBuildInstallerClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.RunInstallerBuildAsync();
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

    private void OnOpenInstallerOutputClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.OpenInstallerOutputPath();
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
