using System.Text.Json;
using GameForge.Editor.Interview;

namespace GameForge.Editor.Tests;

public sealed class InterviewLongSessionContinuityTests
{
    [Fact]
    public async Task ResumeAfterRestart_PreservesLongSessionContinuity_AndNextQuestionDerivation()
    {
        var fixture = LoadLongSessionFixture();
        var expectedQuestionBeforeSave = InterviewQuestionPlanner.BuildNextQuestion(fixture);

        var path = Path.Combine(Path.GetTempPath(), $"gameforge-at003-long-session-{Guid.NewGuid():N}.json");
        try
        {
            await InterviewSessionStore.SaveAsync(path, fixture);

            // Simulate app restart by loading from persisted state in a fresh reference.
            var resumed = await InterviewSessionStore.LoadAsync(path);
            var resumedQuestion = InterviewQuestionPlanner.BuildNextQuestion(resumed);

            Assert.Equal(fixture.SchemaVersion, resumed.SchemaVersion);
            Assert.Equal(fixture.SessionId, resumed.SessionId);
            Assert.Equal(fixture.CreatedAtUtc, resumed.CreatedAtUtc);
            Assert.Equal(fixture.Concept, resumed.Concept);
            Assert.Equal(fixture.GenreWeights.RtsSim, resumed.GenreWeights.RtsSim);
            Assert.Equal(fixture.GenreWeights.Rpg, resumed.GenreWeights.Rpg);
            Assert.Equal(fixture.Mechanics.CoreLoop, resumed.Mechanics.CoreLoop);
            Assert.Equal(fixture.Narrative.Premise, resumed.Narrative.Premise);
            Assert.Equal(fixture.Style.Preset, resumed.Style.Preset);
            Assert.Equal(fixture.Constraints.TargetPlatforms, resumed.Constraints.TargetPlatforms);
            Assert.True(resumed.UpdatedAtUtc >= resumed.CreatedAtUtc);

            Assert.Equal(fixture.UncertaintyDecisions.Count, resumed.UncertaintyDecisions.Count);
            Assert.Contains(
                resumed.UncertaintyDecisions,
                decision => decision.Topic == "concept" && decision.SourceInput == "think of something");

            for (var i = 0; i < fixture.UncertaintyDecisions.Count; i++)
            {
                var expected = fixture.UncertaintyDecisions[i];
                var actual = resumed.UncertaintyDecisions[i];

                Assert.Equal(expected.Topic, actual.Topic);
                Assert.Equal(expected.SourceInput, actual.SourceInput);
                Assert.Equal(expected.SelectedOptionId, actual.SelectedOptionId);
                Assert.Equal(3, actual.Options.Count);

                for (var optionIndex = 0; optionIndex < expected.Options.Count; optionIndex++)
                {
                    var expectedOption = expected.Options[optionIndex];
                    var actualOption = actual.Options[optionIndex];
                    Assert.Equal(expectedOption.OptionId, actualOption.OptionId);
                    Assert.Equal(expectedOption.Title, actualOption.Title);
                    Assert.Equal(expectedOption.Summary, actualOption.Summary);
                    Assert.Equal(expectedOption.Tradeoff, actualOption.Tradeoff);
                }
            }

            Assert.Equal(expectedQuestionBeforeSave, resumedQuestion);
            Assert.Contains("Contextual coaching", resumedQuestion);
            Assert.Contains("core mechanic", resumedQuestion);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static InterviewSession LoadLongSessionFixture()
    {
        var fixturePath = Path.Combine(ResolveProjectRoot(), "editor", "csharp", "tests", "Fixtures", "interview-long-session.fixture.json");
        var json = File.ReadAllText(fixturePath);

        return JsonSerializer.Deserialize<InterviewSession>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidOperationException("AT-003 long-session fixture could not be deserialized.");
    }

    private static string ResolveProjectRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "GAMEFORGE_ACCEPTANCE_TEST_MATRIX.md");
            if (File.Exists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not resolve GameForge repository root from '{AppContext.BaseDirectory}'.");
    }
}
