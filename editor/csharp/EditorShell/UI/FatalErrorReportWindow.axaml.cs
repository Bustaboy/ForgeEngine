using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace GameForge.Editor.EditorShell.UI;

public partial class FatalErrorReportWindow : Window
{
    public FatalErrorReportWindow(string summary, string detail, string logPath)
    {
        InitializeComponent();
        SummaryTextBlock.Text = summary;
        DetailTextBox.Text = detail;
        LogPathTextBlock.Text = $"Diagnostics log: {logPath}";
        ExitButton.Click += OnExitClick;
    }

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
