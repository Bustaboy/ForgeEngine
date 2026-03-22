using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using GameForge.Editor.EditorShell.ViewModels;
using System.ComponentModel;
using System.Collections.Specialized;

namespace GameForge.Editor.EditorShell.UI;

public partial class MainWindow : Window
{
    private const double ViewportScale = 38.0;
    private const double MarkerSize = 34.0;
    private readonly MainWindowViewModel _viewModel = new();
    private bool _firstRunModalChecked;
    private bool _isSyncingEditorText;
    private Canvas? _viewportCanvas;
    private MainWindowViewModel.ViewportEntity? _draggingEntity;
    private Point _dragPointerStart;
    private float _dragEntityStartX;
    private float _dragEntityStartY;

    public MainWindow()
    {
        InitializeComponent();
        ConfigureCodeEditor();
        ConfigureViewportSurface();
        DataContext = _viewModel;
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
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
        _viewportCanvas.PointerReleased += OnViewportPointerReleased;
        _viewportCanvas.SizeChanged += (_, _) => RefreshViewportVisuals();
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
            var benchmark = await FirstRunBenchmarkExample.RunAsync();
            if (!benchmark.IsFirstRun)
            {
                return;
            }

            await ShowBenchmarkModalAsync(benchmark);
        }
        catch (Exception ex)
        {
            _viewModel.SetStatusMessage($"Benchmark warning: {ex.Message}");
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        var editor = this.FindControl<TextEditor>("CodeEditor");
        if (editor is not null)
        {
            editor.TextChanged -= OnCodeEditorTextChanged;
        }

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.ViewportEntities.CollectionChanged -= OnViewportEntitiesChanged;
        if (_viewportCanvas is not null)
        {
            _viewportCanvas.PointerMoved -= OnViewportPointerMoved;
            _viewportCanvas.PointerReleased -= OnViewportPointerReleased;
        }
    }

    private void OnViewportEntitiesChanged(object? sender, NotifyCollectionChangedEventArgs e)
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
        foreach (var entity in _viewModel.ViewportEntities)
        {
            var marker = BuildEntityMarker(entity);
            _viewportCanvas.Children.Add(marker);
            UpdateEntityMarkerPosition(marker, entity);
        }
    }

    private Border BuildEntityMarker(MainWindowViewModel.ViewportEntity entity)
    {
        var icon = entity.Type switch
        {
            "player" => "👤",
            "npc" => "🧍",
            _ => "📦",
        };

        var marker = new Border
        {
            Width = MarkerSize,
            Height = MarkerSize,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.Parse("#1A2C45")),
            BorderBrush = new SolidColorBrush(Color.Parse("#4AA3FF")),
            BorderThickness = new Thickness(1.2),
            Tag = entity.Id,
            Child = new TextBlock
            {
                Text = icon,
                FontSize = 15,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            }
        };

        ToolTip.SetTip(marker, $"{entity.DisplayName} ({entity.X:F2}, {entity.Y:F2})");
        marker.PointerPressed += OnEntityPointerPressed;
        return marker;
    }

    private void UpdateEntityMarkerPosition(Control marker, MainWindowViewModel.ViewportEntity entity)
    {
        if (_viewportCanvas is null)
        {
            return;
        }

        var centerX = _viewportCanvas.Bounds.Width / 2.0;
        var centerY = _viewportCanvas.Bounds.Height / 2.0;
        var left = centerX + (entity.X * ViewportScale) - (MarkerSize / 2.0);
        var top = centerY - (entity.Y * ViewportScale) - (MarkerSize / 2.0);
        Canvas.SetLeft(marker, left);
        Canvas.SetTop(marker, top);
    }

    private void OnEntityPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewportCanvas is null || sender is not Control marker || marker.Tag is not string entityId)
        {
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

    private void OnViewportPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_viewportCanvas is null || _draggingEntity is null || !e.GetCurrentPoint(_viewportCanvas).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var current = e.GetPosition(_viewportCanvas);
        var deltaX = (float)((current.X - _dragPointerStart.X) / ViewportScale);
        var deltaY = (float)((_dragPointerStart.Y - current.Y) / ViewportScale);
        var nextX = _dragEntityStartX + deltaX;
        var nextY = _dragEntityStartY + deltaY;
        if (_viewModel.PreviewDragPosition(_draggingEntity.Id, nextX, nextY))
        {
            RefreshViewportVisuals();
        }
    }

    private async void OnViewportPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggingEntity is null || _viewportCanvas is null)
        {
            return;
        }

        _draggingEntity = null;
        e.Pointer.Capture(null);
        await _viewModel.CommitDragAsync();
        RefreshViewportVisuals();
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

    private async void OnNewPrototypeClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.NewPrototypeAsync();
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

    private async void OnSaveCodeClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.SaveCodeEditsAsync();
    }
}
