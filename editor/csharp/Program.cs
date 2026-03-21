using GameForge.Editor.Interview;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("GameForge V1 editor launcher (C# app entrypoint)");
        Console.WriteLine("Mode: local-first, single-player, no-code-first");
        Console.WriteLine("Target OS: Windows + Ubuntu");

        if (args.Length > 0 && args[0] == "--interview-persistence-smoke")
        {
            var smokePath = args.Length > 1 ? args[1] : Path.Combine("build", "interview", "smoke-session.json");
            await RunInterviewPersistenceSmokeAsync(smokePath);
            return 0;
        }

        if (args.Length > 0 && args[0] == "--interview-validate-file")
        {
            var payloadPath = args.Length > 1 ? args[1] : throw new ArgumentException("Missing payload path");
            try
            {
                await InterviewSessionStore.LoadAsync(payloadPath);
                Console.WriteLine("Interview payload validation passed.");
                return 0;
            }
            catch (InterviewSchemaException ex)
            {
                Console.WriteLine($"Interview payload validation failed: {ex.Message}");
                return 3;
            }
        }

        if (args.Length > 0 && args[0] == "--interview-uncertainty")
        {
            if (args.Length < 4)
            {
                Console.WriteLine("Usage: --interview-uncertainty <session-path> <topic> <user-input> [selection-index]");
                return 4;
            }

            var sessionPath = args[1];
            var topic = args[2];
            var userInput = args[3];
            var selectionIndex = args.Length > 4 && int.TryParse(args[4], out var parsedIndex) ? parsedIndex : 0;

            await RunInterviewUncertaintyFlowAsync(sessionPath, topic, userInput, selectionIndex);
            return 0;
        }

        var runtimePath = args.Length > 0 ? args[0] : "build/runtime/gameforge_runtime";
        var fullRuntimePath = Path.GetFullPath(runtimePath);

        Console.WriteLine($"Runtime binary path: {fullRuntimePath}");
        Console.WriteLine(File.Exists(fullRuntimePath)
            ? "Runtime build detected."
            : "Runtime build missing (run bootstrap build stage).");

        Console.WriteLine("Editor launcher started successfully.");
        return 0;
    }

    private static async Task RunInterviewPersistenceSmokeAsync(string smokePath)
    {
        var session = new InterviewSession
        {
            Concept = "Smoke test concept",
            Constraints = new ConstraintsSection
            {
                ScopeConstraints = ["single-player only", "no marketplace", "no first-party cloud hosting"],
                TechnicalConstraints = ["vulkan first"],
            },
        };

        await InterviewSessionStore.SaveAsync(smokePath, session);
        var loaded = await InterviewSessionStore.LoadAsync(smokePath);

        Console.WriteLine($"Interview schema version: {loaded.SchemaVersion}");
        Console.WriteLine($"Interview concept: {loaded.Concept}");
        Console.WriteLine("Interview persistence smoke test passed.");
    }

    private static async Task RunInterviewUncertaintyFlowAsync(string sessionPath, string topic, string userInput, int selectionIndex)
    {
        var response = await UncertaintyOptionBridge.GenerateOptionsAsync(userInput, topic);
        if (!response.Ambiguous)
        {
            Console.WriteLine("Input is already clear; no uncertainty options generated.");
            return;
        }

        if (response.Options.Count != 3)
        {
            throw new InvalidOperationException($"Expected exactly 3 options, got {response.Options.Count}.");
        }

        for (var i = 0; i < response.Options.Count; i++)
        {
            var option = response.Options[i];
            Console.WriteLine($"[{i}] {option.Title}: {option.Summary} (Tradeoff: {option.Tradeoff})");
        }

        var safeIndex = Math.Clamp(selectionIndex, 0, response.Options.Count - 1);
        var selected = response.Options[safeIndex];

        var session = File.Exists(sessionPath)
            ? await InterviewSessionStore.LoadAsync(sessionPath)
            : new InterviewSession();

        var decision = new UncertaintyDecision
        {
            Topic = response.Topic,
            SourceInput = response.SourceInput,
            Options = response.Options,
            SelectedOptionId = selected.OptionId,
        };

        var updated = session with
        {
            UncertaintyDecisions = [.. session.UncertaintyDecisions, decision],
        };

        await InterviewSessionStore.SaveAsync(sessionPath, updated);
        Console.WriteLine($"Selected option persisted: {selected.OptionId}");
        Console.WriteLine($"Next question: {InterviewQuestionPlanner.BuildNextQuestion(updated)}");
    }
}
