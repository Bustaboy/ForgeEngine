using System.Text.Json.Nodes;

namespace GameForge.Editor.EditorShell.EditorSystems;

public sealed record RecipeRow(string Name, string InputsCsv, string OutputItem, int OutputQuantity);

public sealed class InventoryRecipesPanelState
{
    public IReadOnlyDictionary<string, int> PlayerInventory { get; private set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<RecipeRow> Recipes { get; private set; } = [];

    public static InventoryRecipesPanelState FromScene(JsonObject root)
    {
        var inventory = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (root["player_inventory"] is JsonObject playerInventory)
        {
            foreach (var kvp in playerInventory)
            {
                if (kvp.Value is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var amount) && amount > 0)
                {
                    inventory[kvp.Key] = amount;
                }
            }
        }

        var recipes = new List<RecipeRow>();
        if (root["recipes"] is JsonArray recipeNodes)
        {
            foreach (var node in recipeNodes.OfType<JsonObject>())
            {
                recipes.Add(ToRecipeRow(node));
            }
        }

        return new InventoryRecipesPanelState
        {
            PlayerInventory = inventory,
            Recipes = recipes,
        };
    }

    public static void UpsertRecipe(JsonObject root, RecipeRow row, int? index = null)
    {
        var recipes = root["recipes"] as JsonArray ?? new JsonArray();
        root["recipes"] = recipes;

        var payload = new JsonObject
        {
            ["name"] = row.Name,
            ["inputs"] = ParseInputs(row.InputsCsv),
            ["output_item"] = row.OutputItem,
            ["output_quantity"] = Math.Max(1, row.OutputQuantity),
        };

        if (index.HasValue && index.Value >= 0 && index.Value < recipes.Count)
        {
            recipes[index.Value] = payload;
            return;
        }

        recipes.Add(payload);
    }

    public static void RemoveRecipe(JsonObject root, int index)
    {
        if (root["recipes"] is JsonArray recipes && index >= 0 && index < recipes.Count)
        {
            recipes.RemoveAt(index);
        }
    }

    private static RecipeRow ToRecipeRow(JsonObject node)
    {
        var inputs = node["inputs"] as JsonObject;
        var pairs = new List<string>();
        if (inputs is not null)
        {
            foreach (var kvp in inputs)
            {
                if (kvp.Value is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var amount) && amount > 0)
                {
                    pairs.Add($"{kvp.Key}:{amount}");
                }
            }
        }

        return new RecipeRow(
            node["name"]?.GetValue<string>() ?? "Recipe",
            string.Join(",", pairs),
            node["output_item"]?.GetValue<string>() ?? "crafted_item",
            Math.Max(1, node["output_quantity"]?.GetValue<int>() ?? 1));
    }

    private static JsonObject ParseInputs(string csv)
    {
        var inputs = new JsonObject();
        if (string.IsNullOrWhiteSpace(csv))
        {
            return inputs;
        }

        var tokens = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var token in tokens)
        {
            var pair = token.Split(':', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2 || string.IsNullOrWhiteSpace(pair[0]))
            {
                continue;
            }

            if (int.TryParse(pair[1], out var amount) && amount > 0)
            {
                inputs[pair[0]] = amount;
            }
        }

        return inputs;
    }
}
