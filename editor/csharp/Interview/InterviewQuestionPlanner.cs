namespace GameForge.Editor.Interview;

public static class InterviewQuestionPlanner
{
    public static string BuildNextQuestion(InterviewSession session)
    {
        var latestDecision = session.UncertaintyDecisions.LastOrDefault();
        if (latestDecision is null)
        {
            return "What kind of core gameplay loop do you want to start with?";
        }

        var chosen = latestDecision.Options.FirstOrDefault(option => option.OptionId == latestDecision.SelectedOptionId);
        if (chosen is null)
        {
            return "Could you pick one direction so I can tailor the next gameplay question?";
        }

        return $"You selected '{chosen.Title}'. Which core mechanic should we detail first for this direction?";
    }
}
