using System;
using System.IO;

internal static class Program
{
    public static int Main(string[] args)
    {
        Console.WriteLine("GameForge V1 editor launcher (C# app entrypoint)");
        Console.WriteLine("Mode: local-first, single-player, no-code-first");
        Console.WriteLine("Target OS: Windows + Ubuntu");

        var runtimePath = args.Length > 0 ? args[0] : "build/runtime/gameforge_runtime";
        var fullRuntimePath = Path.GetFullPath(runtimePath);

        Console.WriteLine($"Runtime binary path: {fullRuntimePath}");
        Console.WriteLine(File.Exists(fullRuntimePath)
            ? "Runtime build detected."
            : "Runtime build missing (run bootstrap build stage).");

        Console.WriteLine("Editor launcher started successfully.");
        return 0;
    }
}
