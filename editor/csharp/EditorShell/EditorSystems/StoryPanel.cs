using System.Text.Json.Nodes;

namespace GameForge.Editor.EditorShell.EditorSystems;

public sealed record StoryBeatRow(string Id, string Title, bool Completed)
{
    public string Label => $"{(Completed ? "✓" : "•")} {Title} ({Id})";
}

public sealed class StoryPanelState
{
    public IReadOnlyList<StoryBeatRow> Beats { get; init; } = [];

    public static StoryPanelState FromScene(JsonObject root)
    {
        var beats = new List<StoryBeatRow>();
        var story = root["story"] as JsonObject;
        if (story?["campaign_beats"] is JsonArray campaignBeats)
        {
            foreach (var node in campaignBeats.OfType<JsonObject>())
            {
                var id = node["id"]?.GetValue<string>() ?? string.Empty;
                var title = node["title"]?.GetValue<string>() ?? id;
                var completed = node["completed"]?.GetValue<bool>() ?? false;
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }
                beats.Add(new StoryBeatRow(id, string.IsNullOrWhiteSpace(title) ? id : title, completed));
            }
        }
        return new StoryPanelState { Beats = beats };
    }
}
