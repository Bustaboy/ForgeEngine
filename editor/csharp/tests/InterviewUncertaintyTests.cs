using System.Text.Json;
using GameForge.Editor.Interview;

namespace GameForge.Editor.Tests;

public sealed class InterviewUncertaintyTests
{
    [Fact]
    public async Task SaveAsync_RejectsDecisionWithoutExactlyThreeOptions()
    {
        var session = new InterviewSession
        {
            UncertaintyDecisions =
            [
                new UncertaintyDecision
                {
                    Topic = "genre",
                    SourceInput = "idk",
                    Options =
                    [
                        new UncertaintyOption { OptionId = "a", Title = "A", Summary = "A", Tradeoff = "A" },
                        new UncertaintyOption { OptionId = "b", Title = "B", Summary = "B", Tradeoff = "B" },
                    ],
                    SelectedOptionId = "a",
                },
            ],
        };

        var path = Path.Combine(Path.GetTempPath(), $"gameforge-invalid-{Guid.NewGuid():N}.json");
        try
        {
            var ex = await Assert.ThrowsAsync<InterviewSchemaException>(() => InterviewSessionStore.SaveAsync(path, session));
            Assert.Contains("exactly 3 options", ex.Message);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsSelectedOptionId()
    {
        var options = new List<UncertaintyOption>
        {
            new() { OptionId = "genre-balanced", Title = "Balanced", Summary = "Mix", Tradeoff = "Moderate identity" },
            new() { OptionId = "genre-systems", Title = "Systems", Summary = "Simulation", Tradeoff = "Story later" },
            new() { OptionId = "genre-story", Title = "Story", Summary = "Narrative", Tradeoff = "Lower systemic depth" },
        };

        var session = new InterviewSession
        {
            UncertaintyDecisions =
            [
                new UncertaintyDecision
                {
                    Topic = "genre",
                    SourceInput = "I don't know",
                    Options = options,
                    SelectedOptionId = "genre-systems",
                },
            ],
        };

        var path = Path.Combine(Path.GetTempPath(), $"gameforge-valid-{Guid.NewGuid():N}.json");
        try
        {
            await InterviewSessionStore.SaveAsync(path, session);
            var loaded = await InterviewSessionStore.LoadAsync(path);

            var persisted = Assert.Single(loaded.UncertaintyDecisions);
            Assert.Equal(3, persisted.Options.Count);
            Assert.Equal("genre-systems", persisted.SelectedOptionId);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }


    [Fact]
    public void SuggestionEnvelope_MapsSnakeCaseSourceInput()
    {
        var payload = """
        {
          "topic": "genre",
          "source_input": "I do not know",
          "ambiguous": true,
          "options": []
        }
        """;

        var envelope = JsonSerializer.Deserialize<SuggestionResponseEnvelope>(payload, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });

        Assert.NotNull(envelope);
        Assert.Equal("I do not know", envelope!.SourceInput);
    }

    [Fact]
    public void BuildNextQuestion_UsesSelectedOptionTitle()
    {
        var session = new InterviewSession
        {
            UncertaintyDecisions =
            [
                new UncertaintyDecision
                {
                    Topic = "style",
                    SourceInput = "unsure",
                    Options =
                    [
                        new() { OptionId = "style-a", Title = "Cozy", Summary = "Warm", Tradeoff = "Lower contrast" },
                        new() { OptionId = "style-b", Title = "Dramatic", Summary = "Bold", Tradeoff = "Harder UI readability" },
                        new() { OptionId = "style-c", Title = "Neutral", Summary = "Clear", Tradeoff = "Less distinct" },
                    ],
                    SelectedOptionId = "style-b",
                },
            ],
        };

        var nextQuestion = InterviewQuestionPlanner.BuildNextQuestion(session);

        Assert.Contains("Dramatic", nextQuestion);
        Assert.Contains("core mechanic", nextQuestion);
    }

    [Fact]
    public void ThinkForMeEnvelope_MapsConfirmationGateAndThreeProposals()
    {
        var payload = """
        {
          "mode": "think-for-me",
          "topic": "concept",
          "source_input": "think of something",
          "triggered": true,
          "confirmation_required": true,
          "proposals": [
            {
              "direction_id": "concept-a",
              "title": "A",
              "elevator_pitch": "Pitch A",
              "gameplay_pillars": ["one"],
              "prototype_seed": {"rendering": "vulkan-first"},
              "tradeoff": "Tradeoff A"
            },
            {
              "direction_id": "concept-b",
              "title": "B",
              "elevator_pitch": "Pitch B",
              "gameplay_pillars": ["two"],
              "prototype_seed": {"rendering": "vulkan-first"},
              "tradeoff": "Tradeoff B"
            },
            {
              "direction_id": "concept-c",
              "title": "C",
              "elevator_pitch": "Pitch C",
              "gameplay_pillars": ["three"],
              "prototype_seed": {"rendering": "vulkan-first"},
              "tradeoff": "Tradeoff C"
            }
          ],
          "human_summary_markdown": "## Proposals"
        }
        """;

        var envelope = JsonSerializer.Deserialize<ThinkForMeResponseEnvelope>(payload, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });

        Assert.NotNull(envelope);
        Assert.True(envelope!.ConfirmationRequired);
        Assert.Equal(3, envelope.Proposals.Count);
        Assert.Equal("think-for-me", envelope.Mode);
    }
}
