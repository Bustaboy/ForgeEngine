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

    public static async Task SaveAsync(string path, InterviewSession session, CancellationToken cancellationToken = default)
    {
        var normalized = session with { UpdatedAtUtc = DateTime.UtcNow, SchemaVersion = InterviewSchema.Version };
        EnsureSupportedSchemaVersion(normalized.SchemaVersion);

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
        var session = JsonSerializer.Deserialize<InterviewSession>(json, JsonOptions)
            ?? throw new InterviewSchemaException("Interview session payload could not be deserialized.");

        EnsureSupportedSchemaVersion(session.SchemaVersion);
        return session;
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
