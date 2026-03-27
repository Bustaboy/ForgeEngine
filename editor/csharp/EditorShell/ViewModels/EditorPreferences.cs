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
                Audio = new RuntimePreferences.AudioPreferences
                {
                    MusicTrack = Runtime.Audio.MusicTrack,
                    AmbientTrack = Runtime.Audio.AmbientTrack,
                    CombatMusicOverride = Runtime.Audio.CombatMusicOverride,
                    MasterVolume = Runtime.Audio.MasterVolume,
                    MusicVolume = Runtime.Audio.MusicVolume,
                    AmbientVolume = Runtime.Audio.AmbientVolume,
                    UiVolume = Runtime.Audio.UiVolume,
                    SfxVolume = Runtime.Audio.SfxVolume,
                    SpatialVoiceLimit = Runtime.Audio.SpatialVoiceLimit,
                    CombatDuckingStrength = Runtime.Audio.CombatDuckingStrength,
                    UiDuckingStrength = Runtime.Audio.UiDuckingStrength,
                    ReverbZonePreset = Runtime.Audio.ReverbZonePreset,
                    ProceduralIntensity = Runtime.Audio.ProceduralIntensity,
                },
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
        var normalizedMusicTrack = string.IsNullOrWhiteSpace(Runtime.Audio.MusicTrack)
            ? "music_exploration"
            : Runtime.Audio.MusicTrack.Trim();
        var normalizedAmbientTrack = string.IsNullOrWhiteSpace(Runtime.Audio.AmbientTrack)
            ? "ambient_exploration_loop"
            : Runtime.Audio.AmbientTrack.Trim();
        var normalizedReverbZone = string.IsNullOrWhiteSpace(Runtime.Audio.ReverbZonePreset)
            ? "outdoor"
            : Runtime.Audio.ReverbZonePreset.Trim().ToLowerInvariant();
        if (normalizedReverbZone is not ("outdoor" or "indoor" or "cave" or "workshop"))
        {
            normalizedReverbZone = "outdoor";
        }

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
                Audio = new RuntimePreferences.AudioPreferences
                {
                    MusicTrack = normalizedMusicTrack,
                    AmbientTrack = normalizedAmbientTrack,
                    CombatMusicOverride = Runtime.Audio.CombatMusicOverride,
                    MasterVolume = Math.Clamp(Runtime.Audio.MasterVolume, 0, 100),
                    MusicVolume = Math.Clamp(Runtime.Audio.MusicVolume, 0, 100),
                    AmbientVolume = Math.Clamp(Runtime.Audio.AmbientVolume, 0, 100),
                    UiVolume = Math.Clamp(Runtime.Audio.UiVolume, 0, 100),
                    SfxVolume = Math.Clamp(Runtime.Audio.SfxVolume, 0, 100),
                    SpatialVoiceLimit = Math.Clamp(Runtime.Audio.SpatialVoiceLimit, 4, 64),
                    CombatDuckingStrength = Math.Clamp(Runtime.Audio.CombatDuckingStrength, 0, 100),
                    UiDuckingStrength = Math.Clamp(Runtime.Audio.UiDuckingStrength, 0, 100),
                    ReverbZonePreset = normalizedReverbZone,
                    ProceduralIntensity = Math.Clamp(Runtime.Audio.ProceduralIntensity, 0, 100),
                },
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

        public AudioPreferences Audio { get; init; } = new();

        public sealed class AudioPreferences
        {
            public string MusicTrack { get; init; } = "music_exploration";

            public string AmbientTrack { get; init; } = "ambient_exploration_loop";

            public bool CombatMusicOverride { get; init; } = true;

            public int MasterVolume { get; init; } = 85;

            public int MusicVolume { get; init; } = 75;

            public int AmbientVolume { get; init; } = 60;

            public int UiVolume { get; init; } = 80;

            public int SfxVolume { get; init; } = 80;

            public int SpatialVoiceLimit { get; init; } = 24;

            public int CombatDuckingStrength { get; init; } = 35;

            public int UiDuckingStrength { get; init; } = 15;

            public string ReverbZonePreset { get; init; } = "outdoor";

            public int ProceduralIntensity { get; init; } = 55;
        }
    }

    public sealed class EditorPanePreferences
    {
        public int IconSize { get; init; } = 58;

        public int HistoryLength { get; init; } = 120;

        public string DefaultTemplateId { get; init; } = "cozy-colony";
    }
}
