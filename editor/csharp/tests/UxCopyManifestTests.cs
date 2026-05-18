using System.Text.Json;

namespace Soul.Editor.Tests;

public sealed class UxCopyManifestTests
{
    [Fact]
    public void BlockingCopyManifestLabels_AppearInEditorUiSources()
    {
        var root = ResolveProjectRoot();
        var manifestPath = Path.Combine(root, "docs", "release", "fixtures", "ux_copy_manifest_v1.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));

        var uiRoots = Directory.EnumerateFiles(
                Path.Combine(root, "editor", "csharp", "EditorShell", "UI"),
                "*.*",
                SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var combined = string.Join('\n', uiRoots.Select(File.ReadAllText));

        foreach (var entry in document.RootElement.GetProperty("entries").EnumerateArray())
        {
            if (!string.Equals(entry.GetProperty("severity").GetString(), "blocking", StringComparison.Ordinal))
            {
                continue;
            }

            var label = entry.GetProperty("canonical_label").GetString()
                ?? throw new InvalidOperationException("canonical_label missing");
            Assert.Contains(label, combined, StringComparison.Ordinal);
        }
    }

    private static string ResolveProjectRoot()
    {
        var current = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.GetFullPath(Path.Combine(current, string.Join(Path.DirectorySeparatorChar, Enumerable.Repeat("..", i))));
            if (File.Exists(Path.Combine(candidate, "SOUL_LOOM_V1_BLUEPRINT.md")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Unable to resolve repository root from test base directory.");
    }
}
