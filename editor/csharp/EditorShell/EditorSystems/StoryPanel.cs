using System.Text.Json.Nodes;

namespace GameForge.Editor.EditorShell.EditorSystems;

public sealed record StoryBeatRow(string Id, string Title, string Summary, bool Completed, bool CutsceneTrigger)
{
    public string Label => $"{(Completed ? "✓" : "•")} {Title} ({Id}){(CutsceneTrigger ? " 🎬" : string.Empty)}";
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
                var summary = node["summary"]?.GetValue<string>() ?? string.Empty;
                var completed = node["completed"]?.GetValue<bool>() ?? false;
                var cutsceneTrigger = node["cutscene_trigger"]?.GetValue<bool>() ?? false;
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }
                beats.Add(new StoryBeatRow(
                    id,
                    string.IsNullOrWhiteSpace(title) ? id : title,
                    summary,
                    completed,
                    cutsceneTrigger));
            }
        }
        return new StoryPanelState { Beats = beats };
    }
}
