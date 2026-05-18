using Soul.Editor.Interview;

namespace Soul.Editor.Tests;

public sealed class ThinkForMeIntegrationTests
{
    private static bool ShouldSkipPythonBridge =>
        string.Equals(Environment.GetEnvironmentVariable("SOUL_LOOM_SKIP_PYTHON_BRIDGE"), "1", StringComparison.Ordinal);

    [Fact]
    public async Task GenerateThinkForMeDirectionsAsync_ReturnsThreeProposalsWithConfirmationGate()
    {
        if (ShouldSkipPythonBridge)
        {
            return;
        }

        var response = await UncertaintyOptionBridge.GenerateThinkForMeDirectionsAsync("think for me", "concept");
        Assert.True(response.Triggered);
        Assert.True(response.ConfirmationRequired);
        Assert.Equal(3, response.Proposals.Count);
        Assert.Contains("confirm", response.HumanSummaryMarkdown, StringComparison.OrdinalIgnoreCase);
        Assert.All(response.Proposals, proposal =>
        {
            Assert.False(string.IsNullOrWhiteSpace(proposal.Title));
            Assert.False(string.IsNullOrWhiteSpace(proposal.ElevatorPitch));
            Assert.NotEmpty(proposal.GameplayPillars);
        });
    }
}
