using System.Text.Json;

namespace GameForge.Editor.EditorShell.ViewModels;

public sealed class EditorPreferences
{
    public GeneralPreferences General { get; init; } = new();

    public RuntimePreferences Runtime { get; init; } = new();

    public EditorPanePreferences Editor { get; init; } = new();

    public static EditorPreferences CreateDefault() => new();

    public EditorPreferences Clone()
    {
        return new EditorPreferences
        {
            General = new GeneralPreferences
            {
                Theme = General.Theme,
                AutosaveEnabled = General.AutosaveEnabled,
            },
            Runtime = new RuntimePreferences
            {
                VulkanResolution = Runtime.VulkanResolution,
                FpsLimit = Runtime.FpsLimit,
            },
            Editor = new EditorPanePreferences
            {
                IconSize = Editor.IconSize,
                HistoryLength = Editor.HistoryLength,
                DefaultTemplateId = Editor.DefaultTemplateId,
            },
        };
    }

    public EditorPreferences Sanitize()
    {
        var theme = string.IsNullOrWhiteSpace(General.Theme) ? "Dark" : General.Theme.Trim();
        var normalizedTheme = theme.Equals("Light", StringComparison.OrdinalIgnoreCase)
            ? "Light"
            : theme.Equals("System", StringComparison.OrdinalIgnoreCase)
                ? "System"
                : "Dark";

        var normalizedResolution = string.IsNullOrWhiteSpace(Runtime.VulkanResolution)
            ? "1920x1080"
            : Runtime.VulkanResolution.Trim();

        var normalizedTemplate = string.IsNullOrWhiteSpace(Editor.DefaultTemplateId)
            ? "cozy-colony"
            : Editor.DefaultTemplateId.Trim();

        return new EditorPreferences
        {
            General = new GeneralPreferences
            {
                Theme = normalizedTheme,
                AutosaveEnabled = General.AutosaveEnabled,
            },
            Runtime = new RuntimePreferences
            {
                VulkanResolution = normalizedResolution,
                FpsLimit = Math.Clamp(Runtime.FpsLimit, 30, 240),
            },
            Editor = new EditorPanePreferences
            {
                IconSize = Math.Clamp(Editor.IconSize, 40, 84),
                HistoryLength = Math.Clamp(Editor.HistoryLength, 10, 300),
                DefaultTemplateId = normalizedTemplate,
            },
        };
    }

    public static EditorPreferences LoadOrDefault(string settingsPath)
    {
        if (!File.Exists(settingsPath))
        {
            return CreateDefault();
        }

        try
        {
            var payload = File.ReadAllText(settingsPath);
            var loaded = JsonSerializer.Deserialize<EditorPreferences>(payload);
            return (loaded ?? CreateDefault()).Sanitize();
        }
        catch
        {
            return CreateDefault();
        }
    }

    public async Task SaveAsync(string settingsPath, CancellationToken cancellationToken = default)
    {
        var rootDirectory = Path.GetDirectoryName(settingsPath) ?? ".";
        Directory.CreateDirectory(rootDirectory);
        var payload = JsonSerializer.Serialize(Sanitize(), new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(settingsPath, payload, cancellationToken);
    }

    public sealed class GeneralPreferences
    {
        public string Theme { get; init; } = "Dark";

        public bool AutosaveEnabled { get; init; } = true;
    }

    public sealed class RuntimePreferences
    {
        public string VulkanResolution { get; init; } = "1920x1080";

        public int FpsLimit { get; init; } = 60;
    }

    public sealed class EditorPanePreferences
    {
        public int IconSize { get; init; } = 58;

        public int HistoryLength { get; init; } = 120;

        public string DefaultTemplateId { get; init; } = "cozy-colony";
    }
}
