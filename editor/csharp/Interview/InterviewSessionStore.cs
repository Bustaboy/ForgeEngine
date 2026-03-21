using System.Text.Json;

namespace GameForge.Editor.Interview;

public sealed class InterviewSchemaException(string message) : Exception(message);

public static class InterviewSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
    };

    private static readonly HashSet<string> TopLevelProperties =
    [
        "schema_version",
        "session_id",
        "created_at_utc",
        "updated_at_utc",
        "concept",
        "genre_weights",
        "mechanics",
        "narrative",
        "style",
        "constraints",
    ];

    public static async Task SaveAsync(string path, InterviewSession session, CancellationToken cancellationToken = default)
    {
        var normalized = session with { UpdatedAtUtc = DateTime.UtcNow, SchemaVersion = InterviewSchema.Version };
        ValidateSessionModel(normalized);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(normalized, JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    public static async Task<InterviewSession> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);

        using var doc = JsonDocument.Parse(json);
        ValidatePayloadDocument(doc.RootElement);

        var session = JsonSerializer.Deserialize<InterviewSession>(json, JsonOptions)
            ?? throw new InterviewSchemaException("Interview session payload could not be deserialized.");

        ValidateSessionModel(session);
        return session;
    }

    private static void ValidatePayloadDocument(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InterviewSchemaException("Interview session JSON root must be an object.");
        }

        RequireOnlyProperties(root, TopLevelProperties, "root");

        RequireProperty(root, "schema_version", JsonValueKind.Number);
        RequireProperty(root, "session_id", JsonValueKind.String);
        RequireProperty(root, "created_at_utc", JsonValueKind.String);
        RequireProperty(root, "updated_at_utc", JsonValueKind.String);
        RequireProperty(root, "concept", JsonValueKind.String);

        var genreWeights = RequireProperty(root, "genre_weights", JsonValueKind.Object);
        RequireOnlyProperties(genreWeights, ["rts_sim", "rpg"], "genre_weights");
        RequireProperty(genreWeights, "rts_sim", JsonValueKind.Number);
        RequireProperty(genreWeights, "rpg", JsonValueKind.Number);

        var mechanics = RequireProperty(root, "mechanics", JsonValueKind.Object);
        RequireOnlyProperties(mechanics, ["core_loop", "progression_systems", "failure_states", "simulation_depth_notes"], "mechanics");
        RequireProperty(mechanics, "core_loop", JsonValueKind.String);
        RequireStringArray(mechanics, "progression_systems", "mechanics");
        RequireStringArray(mechanics, "failure_states", "mechanics");
        RequireProperty(mechanics, "simulation_depth_notes", JsonValueKind.String);

        var narrative = RequireProperty(root, "narrative", JsonValueKind.Object);
        RequireOnlyProperties(narrative, ["premise", "player_fantasy", "tone", "world_notes", "quest_structure"], "narrative");
        RequireProperty(narrative, "premise", JsonValueKind.String);
        RequireProperty(narrative, "player_fantasy", JsonValueKind.String);
        RequireProperty(narrative, "tone", JsonValueKind.String);
        RequireProperty(narrative, "world_notes", JsonValueKind.String);
        RequireStringArray(narrative, "quest_structure", "narrative");

        var style = RequireProperty(root, "style", JsonValueKind.Object);
        RequireOnlyProperties(style, ["preset", "art_direction", "camera_direction", "ui_direction", "audio_direction"], "style");
        RequireProperty(style, "preset", JsonValueKind.String);
        RequireProperty(style, "art_direction", JsonValueKind.String);
        RequireProperty(style, "camera_direction", JsonValueKind.String);
        RequireProperty(style, "ui_direction", JsonValueKind.String);
        RequireProperty(style, "audio_direction", JsonValueKind.String);

        var constraints = RequireProperty(root, "constraints", JsonValueKind.Object);
        RequireOnlyProperties(constraints, ["target_platforms", "content_rating_target", "scope_constraints", "technical_constraints", "accessibility_constraints"], "constraints");
        RequireStringArray(constraints, "target_platforms", "constraints", minItems: 1);
        RequireProperty(constraints, "content_rating_target", JsonValueKind.String);
        RequireStringArray(constraints, "scope_constraints", "constraints");
        RequireStringArray(constraints, "technical_constraints", "constraints");
        RequireStringArray(constraints, "accessibility_constraints", "constraints");
    }

    private static void ValidateSessionModel(InterviewSession session)
    {
        EnsureSupportedSchemaVersion(session.SchemaVersion);

        if (string.IsNullOrWhiteSpace(session.SessionId))
        {
            throw new InterviewSchemaException("session_id must be a non-empty string.");
        }

        ValidateGenreWeight("genre_weights.rts_sim", session.GenreWeights.RtsSim);
        ValidateGenreWeight("genre_weights.rpg", session.GenreWeights.Rpg);

        if (session.Constraints.TargetPlatforms.Count == 0)
        {
            throw new InterviewSchemaException("constraints.target_platforms must include at least one platform.");
        }

        var allowedPlatforms = new HashSet<string>(StringComparer.Ordinal) { "windows", "ubuntu" };
        foreach (var platform in session.Constraints.TargetPlatforms)
        {
            if (!allowedPlatforms.Contains(platform))
            {
                throw new InterviewSchemaException(
                    $"Unsupported platform '{platform}' in constraints.target_platforms. Allowed values: windows, ubuntu.");
            }
        }
    }

    private static void ValidateGenreWeight(string fieldPath, double value)
    {
        if (value < 0 || value > 1)
        {
            throw new InterviewSchemaException($"{fieldPath} must be between 0 and 1 inclusive.");
        }
    }

    private static JsonElement RequireProperty(JsonElement parent, string propertyName, JsonValueKind expectedKind)
    {
        if (!parent.TryGetProperty(propertyName, out var value))
        {
            throw new InterviewSchemaException($"Missing required property: {propertyName}.");
        }

        if (value.ValueKind != expectedKind)
        {
            throw new InterviewSchemaException(
                $"Property '{propertyName}' must be of type '{expectedKind}', got '{value.ValueKind}'.");
        }

        return value;
    }

    private static void RequireOnlyProperties(JsonElement element, IEnumerable<string> allowedProperties, string context)
    {
        var allowed = new HashSet<string>(allowedProperties, StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                throw new InterviewSchemaException($"Unexpected property '{property.Name}' in {context}.");
            }
        }

        foreach (var expectedProperty in allowed)
        {
            if (!element.TryGetProperty(expectedProperty, out _))
            {
                throw new InterviewSchemaException($"Missing required property '{expectedProperty}' in {context}.");
            }
        }
    }

    private static void RequireStringArray(JsonElement parent, string propertyName, string context, int minItems = 0)
    {
        var array = RequireProperty(parent, propertyName, JsonValueKind.Array);

        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new InterviewSchemaException(
                    $"Property '{context}.{propertyName}' must contain only strings (bad index {index}).");
            }

            index++;
        }

        if (index < minItems)
        {
            throw new InterviewSchemaException(
                $"Property '{context}.{propertyName}' must contain at least {minItems} item(s).");
        }
    }

    private static void EnsureSupportedSchemaVersion(int schemaVersion)
    {
        if (schemaVersion != InterviewSchema.Version)
        {
            throw new InterviewSchemaException(
                $"Unsupported interview schema version '{schemaVersion}'. Expected '{InterviewSchema.Version}'.");
        }
    }
}
