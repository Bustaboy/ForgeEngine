using System.Text.Json;
using System.Text.Json.Nodes;

namespace GameForge.Editor.EditorShell.EditorSystems;

public sealed record CoCreatorSuggestion(
    string Id,
    string Title,
    string WhyThisFits,
    JsonObject Mutation)
{
    public string Label => string.IsNullOrWhiteSpace(Title) ? Id : Title;

    public static IReadOnlyList<CoCreatorSuggestion> ParseSuggestions(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        var parsed = JsonNode.Parse(payload) as JsonArray;
        if (parsed is null)
        {
            return [];
        }

        var suggestions = new List<CoCreatorSuggestion>(parsed.Count);
        foreach (var node in parsed)
        {
            if (node is not JsonObject suggestionObject)
            {
                continue;
            }

            var id = suggestionObject["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("n");
            var title = suggestionObject["title"]?.GetValue<string>() ?? "Untitled suggestion";
            var why = suggestionObject["why_this_fits"]?.GetValue<string>() ?? "No rationale provided.";
            var mutation = suggestionObject["mutation"] as JsonObject;
            if (mutation is null)
            {
                continue;
            }

            suggestions.Add(new CoCreatorSuggestion(id, title, why, mutation));
        }

        return suggestions;
    }
}
