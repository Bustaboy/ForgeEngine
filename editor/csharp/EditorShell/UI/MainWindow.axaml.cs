using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
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
    private const string AssetDragFormat = "application/x-gameforge-asset-id";
    private const string HierarchyDragFormat = "application/x-gameforge-hierarchy-entity";
    private readonly MainWindowViewModel _viewModel = new();
    private bool _firstRunModalChecked;
    private bool _isSyncingEditorText;
    private Canvas? _viewportCanvas;
    private MainWindowViewModel.ViewportEntity? _draggingEntity;
    private Point _dragPointerStart;
    private float _dragEntityStartX;
    private float _dragEntityStartY;
    private bool _isMarqueeSelecting;
    private Point _marqueeStart;
    private Border? _marqueeVisual;

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
        _viewportCanvas.PointerPressed += OnViewportPointerPressed;
        _viewportCanvas.PointerReleased += OnViewportPointerReleased;
        _viewportCanvas.SizeChanged += (_, _) => RefreshViewportVisuals();
        DragDrop.SetAllowDrop(_viewportCanvas, true);
        _viewportCanvas.AddHandler(DragDrop.DragOverEvent, OnViewportDragOver);
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
            _viewportCanvas.RemoveHandler(DragDrop.DropEvent, OnViewportDrop);
        }
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
            Width = MarkerSize * Math.Max(0.7, entity.Scale),
            Height = MarkerSize * Math.Max(0.7, entity.Scale),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(ParseColor(entity.ColorHex, "#1A2C45")),
            BorderBrush = new SolidColorBrush(entity.IsSelected ? Color.Parse("#7FD1FF") : Color.Parse("#4AA3FF")),
            BorderThickness = entity.IsSelected ? new Thickness(2.4) : new Thickness(1.2),
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
        var markerSize = MarkerSize * Math.Max(0.7, entity.Scale);
        var left = centerX + (entity.X * ViewportScale) - (markerSize / 2.0);
        var top = centerY - (entity.Y * ViewportScale) - (markerSize / 2.0);
        Canvas.SetLeft(marker, left);
        Canvas.SetTop(marker, top);
    }

    private void OnViewportPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewportCanvas is null)
        {
            return;
        }

        if (e.Source is Control source && source.Tag is string)
        {
            return;
        }

        var modifiers = e.KeyModifiers;
        if (!modifiers.HasFlag(KeyModifiers.Control))
        {
            _viewModel.ClearSelection();
            RefreshViewportVisuals();
        }

        _isMarqueeSelecting = true;
        _marqueeStart = e.GetPosition(_viewportCanvas);
        EnsureMarqueeVisual();
        e.Pointer.Capture(_viewportCanvas);
        e.Handled = true;
    }

    private void OnEntityPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewportCanvas is null || sender is not Control marker || marker.Tag is not string entityId)
        {
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

        _viewModel.SelectSingleEntity(entityId);
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

    private void OnViewportPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_viewportCanvas is null)
        {
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
        if (_viewportCanvas is null)
        {
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
        var minWorldX = (float)((minScreenX - centerX) / ViewportScale);
        var maxWorldX = (float)((maxScreenX - centerX) / ViewportScale);
        var minWorldY = (float)((centerY - maxScreenY) / ViewportScale);
        var maxWorldY = (float)((centerY - minScreenY) / ViewportScale);

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
        if (e.Data.Contains(AssetDragFormat) || e.Data.Contains(DataFormats.Text))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        e.DragEffects = DragDropEffects.None;
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
            return;
        }

        var world = ScreenToWorld(e.GetPosition(_viewportCanvas));
        await _viewModel.PlaceImportedAssetInSceneAsync(assetId, world.X, world.Y);
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
        var worldX = (float)((point.X - centerX) / ViewportScale);
        var worldY = (float)((centerY - point.Y) / ViewportScale);
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

        var data = new DataObject();
        data.Set(AssetDragFormat, assetId);
        data.Set(DataFormats.Text, assetId);
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
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
                new FilePickerFileType("GameForge V1 Assets")
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
}
