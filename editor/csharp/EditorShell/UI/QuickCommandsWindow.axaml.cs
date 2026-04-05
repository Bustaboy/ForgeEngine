using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GameForge.Editor.EditorShell.UI;

public sealed class QuickCommandItem
{
    public QuickCommandItem(string title, string subtitle, Action execute)
    {
        Title = title;
        Subtitle = subtitle;
        Execute = execute;
    }

    public string Title { get; }
    public string Subtitle { get; }
    public Action Execute { get; }
}

public partial class QuickCommandsWindow : Window
{
    private readonly List<QuickCommandItem> _all;

    public QuickCommandsWindow(IReadOnlyList<QuickCommandItem> commands)
    {
        InitializeComponent();
        _all = commands.ToList();
        CommandsListBox.ItemsSource = _all;
        FilterTextBox.TextChanged += OnFilterTextChanged;
        FilterTextBox.KeyDown += OnFilterKeyDown;
        CommandsListBox.DoubleTapped += OnCommandsDoubleTapped;
        KeyDown += OnWindowKeyDown;
        Opened += OnOpened;
    }

    private void OnFilterKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            RunSelected();
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnOpened(object? sender, EventArgs e)
    {
        FilterTextBox.Focus();
        SelectFirstIfAny();
    }

    private void SelectFirstIfAny()
    {
        if (CommandsListBox.Items.Count > 0)
        {
            CommandsListBox.SelectedIndex = 0;
        }
    }

    private void OnFilterTextChanged(object? sender, TextChangedEventArgs e) =>
        ApplyFilter(FilterTextBox.Text ?? string.Empty);

    private void ApplyFilter(string query)
    {
        var q = query.Trim();
        if (string.IsNullOrEmpty(q))
        {
            CommandsListBox.ItemsSource = _all;
            SelectFirstIfAny();
            return;
        }

        static bool Match(QuickCommandItem c, string needle)
        {
            return c.Title.Contains(needle, StringComparison.OrdinalIgnoreCase)
                   || c.Subtitle.Contains(needle, StringComparison.OrdinalIgnoreCase);
        }

        CommandsListBox.ItemsSource = _all.Where(c => Match(c, q)).ToList();
        SelectFirstIfAny();
    }

    private void OnCommandsDoubleTapped(object? sender, RoutedEventArgs e) => RunSelected();

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        RunSelected();
        e.Handled = true;
    }

    private void RunSelected()
    {
        if (CommandsListBox.SelectedItem is not QuickCommandItem item)
        {
            return;
        }

        try
        {
            item.Execute();
        }
        finally
        {
            Close();
        }
    }
}
