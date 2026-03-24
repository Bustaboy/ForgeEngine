using System.Text.Json.Nodes;

namespace GameForge.Editor.EditorShell.EditorSystems;

public sealed class WeatherPanelState
{
    public string CurrentWeather { get; set; } = "sunny";
    public string TargetWeather { get; set; } = "sunny";
    public float Intensity { get; set; } = 0.25f;
    public float TransitionSeconds { get; set; } = 14f;
    public float NextTransitionSeconds { get; set; } = 75f;

    public static WeatherPanelState FromScene(JsonObject root)
    {
        var weather = root["weather"] as JsonObject;
        if (weather is null)
        {
            return new WeatherPanelState();
        }

        return new WeatherPanelState
        {
            CurrentWeather = weather["current_weather"]?.GetValue<string>() ?? "sunny",
            TargetWeather = weather["target_weather"]?.GetValue<string>() ?? weather["current_weather"]?.GetValue<string>() ?? "sunny",
            Intensity = Math.Clamp(ReadSingle(weather["intensity"], 0.25f), 0f, 1f),
            TransitionSeconds = Math.Max(2f, ReadSingle(weather["transition_duration_seconds"], 14f)),
            NextTransitionSeconds = Math.Max(5f, ReadSingle(weather["seconds_until_next_transition"], 75f)),
        };
    }

    public void ApplyToScene(JsonObject root)
    {
        var weather = root["weather"] as JsonObject ?? new JsonObject();
        root["weather"] = weather;
        weather["current_weather"] = string.IsNullOrWhiteSpace(CurrentWeather) ? "sunny" : CurrentWeather.Trim();
        weather["target_weather"] = string.IsNullOrWhiteSpace(TargetWeather) ? weather["current_weather"]?.GetValue<string>() ?? "sunny" : TargetWeather.Trim();
        weather["intensity"] = Math.Clamp(Intensity, 0f, 1f);
        weather["transition_duration_seconds"] = Math.Max(2f, TransitionSeconds);
        weather["seconds_until_next_transition"] = Math.Max(5f, NextTransitionSeconds);
    }

    private static float ReadSingle(JsonNode? value, float fallback)
    {
        if (value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<float>(out var floatValue))
            {
                return floatValue;
            }

            if (jsonValue.TryGetValue<double>(out var doubleValue))
            {
                return (float)doubleValue;
            }
        }

        return fallback;
    }
}
