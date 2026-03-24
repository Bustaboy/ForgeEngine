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
        var gridPixelStep = ViewportScale * gridWorldStep;
        if (gridPixelStep < 8)
        {
            return;
        }

        var centerX = width / 2.0;
        var centerY = height / 2.0;
        var verticalOffset = centerX % gridPixelStep;
        var horizontalOffset = centerY % gridPixelStep;
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
        var left = centerX + (entity.X * ViewportScale) - (markerWidth / 2.0);
        var top = centerY - (entity.Y * ViewportScale) - (markerHeight / 2.0);
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
            RefreshViewportVisuals();
            e.DragEffects = DragDropEffects.Copy;
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

    private async void OnRefreshAssetsClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshImportedAssetsAsync();
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
        var marker = new Border
        {
            Width = MarkerSize * 1.15,
            Height = MarkerSize * 1.15,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(150, 74, 163, 255)),
            BorderBrush = new SolidColorBrush(Color.Parse("#BFE1FF")),
            BorderThickness = new Thickness(1.8),
            Opacity = 0.82,
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

        ToolTip.SetTip(marker, $"Drop: {_viewModel.AssetDragGhostTitle}");
        return marker;
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
        var left = centerX + (_viewModel.AssetDragGhostWorldX * ViewportScale) - (markerSize / 2.0);
        var top = centerY - (_viewModel.AssetDragGhostWorldY * ViewportScale) - (markerSize / 2.0);
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
            _viewModel.SetSystemTab(tab);
        }
    }

    private async void OnApplyDayNightClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.ApplyDayNightAsync();
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

    private async void OnSaveCoCreatorContextClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.SaveCoCreatorSettingsAsync();
    }

    private async void OnRefreshCoCreatorClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshCoCreatorSuggestionsAsync();
    }

    private void OnToggleCoCreatorLiveClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.SetCoCreatorLive(!_viewModel.CoCreatorLiveEnabled);
    }

    private async void OnAcceptCoCreatorSuggestionClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.AcceptCoCreatorSuggestionAsync();
    }

    private void OnRejectCoCreatorSuggestionClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.RejectCoCreatorSuggestion();
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

    private async void OnOpenProjectClick(object? sender, RoutedEventArgs e)
    {
        if (StorageProvider is null)
        {
            _viewModel.SetStatusMessage("Storage provider unavailable for project open.");
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open GameForge Project",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("GameForge Project")
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
            Title = "Save GameForge Project",
            SuggestedFileName = "gameforge-project.gfproj.json",
            FileTypeChoices =
            [
                new FilePickerFileType("GameForge Project")
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

    private async void OnOpenSettingsClick(object? sender, RoutedEventArgs e)
    {
        var originalPreferences = _viewModel.GetPreferencesSnapshot();
        var settingsWindow = new SettingsWindow(originalPreferences);
        settingsWindow.PreferencesPreviewChanged += preview => _viewModel.ApplyPreferencesPreview(preview);
        await settingsWindow.ShowDialog(this);
        if (settingsWindow.Result is null)
        {
            _viewModel.ApplyPreferencesPreview(originalPreferences);
            return;
        }

        await _viewModel.ApplyAndSavePreferencesAsync(settingsWindow.Result);
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
        if (e.Key == Key.Delete && !IsTextEntryFocused())
        {
            await _viewModel.HandleDeleteShortcutAsync();
            e.Handled = true;
            return;
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        if (e.Key == Key.N)
        {
            OnNewProjectClick(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            OnOpenSettingsClick(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.S)
        {
            OnSaveProjectClick(this, new RoutedEventArgs());
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
}
