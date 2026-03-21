using System.Text.Json.Serialization;

namespace GameForge.Editor.Interview;

public static class InterviewSchema
{
    public const int Version = 1;
}

public sealed record GenreWeights
{
    [JsonPropertyName("rts_sim")]
    public double RtsSim { get; init; } = 0.5;

    [JsonPropertyName("rpg")]
    public double Rpg { get; init; } = 0.5;
}

public sealed record MechanicsSection
{
    [JsonPropertyName("core_loop")]
    public string CoreLoop { get; init; } = string.Empty;

    [JsonPropertyName("progression_systems")]
    public List<string> ProgressionSystems { get; init; } = [];

    [JsonPropertyName("failure_states")]
    public List<string> FailureStates { get; init; } = [];

    [JsonPropertyName("simulation_depth_notes")]
    public string SimulationDepthNotes { get; init; } = string.Empty;
}

public sealed record NarrativeSection
{
    [JsonPropertyName("premise")]
    public string Premise { get; init; } = string.Empty;

    [JsonPropertyName("player_fantasy")]
    public string PlayerFantasy { get; init; } = string.Empty;

    [JsonPropertyName("tone")]
    public string Tone { get; init; } = string.Empty;

    [JsonPropertyName("world_notes")]
    public string WorldNotes { get; init; } = string.Empty;

    [JsonPropertyName("quest_structure")]
    public List<string> QuestStructure { get; init; } = [];
}

public sealed record StyleSection
{
    [JsonPropertyName("preset")]
    public string Preset { get; init; } = string.Empty;

    [JsonPropertyName("art_direction")]
    public string ArtDirection { get; init; } = string.Empty;

    [JsonPropertyName("camera_direction")]
    public string CameraDirection { get; init; } = string.Empty;

    [JsonPropertyName("ui_direction")]
    public string UiDirection { get; init; } = string.Empty;

    [JsonPropertyName("audio_direction")]
    public string AudioDirection { get; init; } = string.Empty;
}

public sealed record ConstraintsSection
{
    [JsonPropertyName("target_platforms")]
    public List<string> TargetPlatforms { get; init; } = ["windows", "ubuntu"];

    [JsonPropertyName("content_rating_target")]
    public string ContentRatingTarget { get; init; } = string.Empty;

    [JsonPropertyName("scope_constraints")]
    public List<string> ScopeConstraints { get; init; } = [];

    [JsonPropertyName("technical_constraints")]
    public List<string> TechnicalConstraints { get; init; } = [];

    [JsonPropertyName("accessibility_constraints")]
    public List<string> AccessibilityConstraints { get; init; } = [];
}

public sealed record UncertaintyOption
{
    [JsonPropertyName("option_id")]
    public string OptionId { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;

    [JsonPropertyName("tradeoff")]
    public string Tradeoff { get; init; } = string.Empty;
}

public sealed record UncertaintyDecision
{
    [JsonPropertyName("topic")]
    public string Topic { get; init; } = string.Empty;

    [JsonPropertyName("source_input")]
    public string SourceInput { get; init; } = string.Empty;

    [JsonPropertyName("options")]
    public List<UncertaintyOption> Options { get; init; } = [];

    [JsonPropertyName("selected_option_id")]
    public string SelectedOptionId { get; init; } = string.Empty;
}

public sealed record InterviewSession
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = InterviewSchema.Version;

    [JsonPropertyName("session_id")]
    public string SessionId { get; init; } = Guid.NewGuid().ToString();

    [JsonPropertyName("created_at_utc")]
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    [JsonPropertyName("updated_at_utc")]
    public DateTime UpdatedAtUtc { get; init; } = DateTime.UtcNow;

    [JsonPropertyName("concept")]
    public string Concept { get; init; } = string.Empty;

    [JsonPropertyName("genre_weights")]
    public GenreWeights GenreWeights { get; init; } = new();

    [JsonPropertyName("mechanics")]
    public MechanicsSection Mechanics { get; init; } = new();

    [JsonPropertyName("narrative")]
    public NarrativeSection Narrative { get; init; } = new();

    [JsonPropertyName("style")]
    public StyleSection Style { get; init; } = new();

    [JsonPropertyName("constraints")]
    public ConstraintsSection Constraints { get; init; } = new();

    [JsonPropertyName("uncertainty_decisions")]
    public List<UncertaintyDecision> UncertaintyDecisions { get; init; } = [];
}
