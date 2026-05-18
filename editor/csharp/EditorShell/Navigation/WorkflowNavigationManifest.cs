using System.Text.Json;

namespace Soul.Editor.EditorShell.Navigation;

public sealed record WorkflowNavigationManifestEntry(
    string Section,
    string NavLabel,
    string ShellTitle,
    string PrimaryCta,
    IReadOnlyList<string> RequiredControls);

public sealed record WorkflowNavigationManifest(IReadOnlyList<WorkflowNavigationManifestEntry> Sections)
{
    public static WorkflowNavigationManifest LoadFromRepository(string repositoryRoot)
    {
        var path = Path.Combine(repositoryRoot, "editor", "csharp", "EditorShell", "Navigation", "workflow_navigation_manifest.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var sections = document.RootElement.GetProperty("sections");
        var entries = new List<WorkflowNavigationManifestEntry>();
        foreach (var section in sections.EnumerateArray())
        {
            var controls = section.GetProperty("required_controls").EnumerateArray()
                .Select(node => node.GetString() ?? string.Empty)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();
            entries.Add(new WorkflowNavigationManifestEntry(
                section.GetProperty("section").GetString() ?? string.Empty,
                section.GetProperty("nav_label").GetString() ?? string.Empty,
                section.GetProperty("shell_title").GetString() ?? string.Empty,
                section.GetProperty("primary_cta").GetString() ?? string.Empty,
                controls));
        }

        return new WorkflowNavigationManifest(entries);
    }
}
