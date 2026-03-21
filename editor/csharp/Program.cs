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
}
