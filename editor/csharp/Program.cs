using System.Text.Json;
using GameForge.Editor.EditorShell;
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

        if (args.Length > 0 && args[0] == "--interview-think-for-me")
        {
            if (args.Length < 4)
            {
                Console.WriteLine("Usage: --interview-think-for-me <session-path> <topic> <user-input> [selection-index] [--confirm]");
                return 5;
            }

            var sessionPath = args[1];
            var topic = args[2];
            var userInput = args[3];
            var selectionIndex = args.Length > 4 && int.TryParse(args[4], out var parsedIndex) ? parsedIndex : 0;
            var confirm = args.Any(arg => string.Equals(arg, "--confirm", StringComparison.OrdinalIgnoreCase));

            await RunInterviewThinkForMeFlowAsync(sessionPath, topic, userInput, selectionIndex, confirm);
            return 0;
        }


        if (args.Length > 0 && args[0] == "--editor-shell-smoke")
        {
            var (projectRoot, declarationArg) = ParseEditorShellSmokeArgs(args);
            await RunEditorShellSmokeAsync(projectRoot, declarationArg);
            return 0;
        }


        if (args.Length > 0 && args[0] == "--steam-readiness")
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: --steam-readiness <metrics-json-path> [--publish] [--acknowledge-warnings] [--confirm-upload] [--audit-output <output>] [--upload-destination <path>]");
                return 7;
            }

            var metricsPath = args[1];
            var requestPublish = args.Any(arg => string.Equals(arg, "--publish", StringComparison.OrdinalIgnoreCase));
            var acknowledgedWarnings = args.Any(arg => string.Equals(arg, "--acknowledge-warnings", StringComparison.OrdinalIgnoreCase));
            var confirmUpload = args.Any(arg => string.Equals(arg, "--confirm-upload", StringComparison.OrdinalIgnoreCase));
            var auditOutput = GetOptionValue(args, "--audit-output")
                ?? Path.Combine("build", "publish", "steam-readiness-audit.json");
            var uploadDestination = GetOptionValue(args, "--upload-destination")
                ?? Path.Combine("build", "publish", "external", "steam-readiness-audit.json");

            return RunSteamReadinessFlow(metricsPath, requestPublish, acknowledgedWarnings, confirmUpload, auditOutput, uploadDestination);
        }

        if (args.Length > 0 && args[0] == "--playtest-report-view")
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: --playtest-report-view <report-json-path> [--export-markdown <output>] [--export-json <output>]");
                return 6;
            }

            var reportPath = args[1];
            var markdownExport = GetOptionValue(args, "--export-markdown");
            var jsonExport = GetOptionValue(args, "--export-json");
            RunPlaytestReportViewer(reportPath, markdownExport, jsonExport);
            return 0;
        }

        if (args.Length > 0 && args[0] == "--first-run-benchmark-example")
        {
            var benchmark = await FirstRunBenchmarkExample.RunAsync();
            FirstRunBenchmarkExample.RenderConsoleFirstRunModal(benchmark);
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

    private static async Task RunInterviewThinkForMeFlowAsync(string sessionPath, string topic, string userInput, int selectionIndex, bool confirm)
    {
        var response = await UncertaintyOptionBridge.GenerateThinkForMeDirectionsAsync(userInput, topic);
        if (!response.Triggered)
        {
            Console.WriteLine("Think-for-me mode was not triggered. Keep interview in normal question mode.");
            return;
        }

        if (response.Proposals.Count != 3)
        {
            throw new InvalidOperationException($"Expected exactly 3 think-for-me proposals, got {response.Proposals.Count}.");
        }

        Console.WriteLine(response.HumanSummaryMarkdown);
        var safeIndex = Math.Clamp(selectionIndex, 0, response.Proposals.Count - 1);
        var selected = response.Proposals[safeIndex];
        var proposalPayload = JsonSerializer.Serialize(selected.PrototypeSeed);

        if (!confirm)
        {
            Console.WriteLine($"Pending direction (not committed): {selected.DirectionId}");
            Console.WriteLine("Confirmation required. Re-run with --confirm to commit this direction.");
            Console.WriteLine($"prototype_seed={proposalPayload}");
            return;
        }

        var session = File.Exists(sessionPath)
            ? await InterviewSessionStore.LoadAsync(sessionPath)
            : new InterviewSession();

        var options = response.Proposals
            .Select(proposal => new UncertaintyOption
            {
                OptionId = proposal.DirectionId,
                Title = proposal.Title,
                Summary = proposal.ElevatorPitch,
                Tradeoff = proposal.Tradeoff,
            })
            .ToList();

        var decision = new UncertaintyDecision
        {
            Topic = response.Topic,
            SourceInput = response.SourceInput,
            Options = options,
            SelectedOptionId = selected.DirectionId,
        };

        var updated = session with
        {
            UncertaintyDecisions = [.. session.UncertaintyDecisions, decision],
        };

        await InterviewSessionStore.SaveAsync(sessionPath, updated);
        Console.WriteLine($"Direction committed after explicit confirmation: {selected.DirectionId}");
        Console.WriteLine($"prototype_seed={proposalPayload}");
    }

    private static async Task RunEditorShellSmokeAsync(string projectRoot, string? declarationArg)
    {
        var snapshot = await EditorProjectLoader.LoadGeneratedProjectAsync(projectRoot);
        var workspace = new EditorWorkspace(snapshot);

        var selectedObjectId = snapshot.SceneObjects.First().ObjectId;
        if (!workspace.SelectObject(selectedObjectId))
        {
            throw new InvalidOperationException($"Failed to select object: {selectedObjectId}");
        }

        Console.WriteLine($"Project opened: {snapshot.ProjectName}");
        Console.WriteLine($"Rendering path: {snapshot.Rendering}");
        Console.WriteLine($"Platforms: {string.Join(", ", snapshot.Platforms)}");
        Console.WriteLine("Docked panels:");

        foreach (var panel in workspace.Layout.Panels.OrderBy(panel => panel.DockZone).ThenBy(panel => panel.Order))
        {
            Console.WriteLine($"- {panel.DisplayName} [{panel.PanelId}] dock={panel.DockZone}");
        }

        Console.WriteLine($"Selected object: {workspace.SelectedObject?.DisplayName}");
        Console.WriteLine($"Inspector simple keys: {string.Join(", ", workspace.Inspector!.SimpleSection.Keys)}");
        Console.WriteLine($"AI context target: {workspace.AiContext!.ObjectLabel} ({workspace.AiContext.ObjectType})");
        var styleView = workspace.GetStylePresetSelectionView();
        Console.WriteLine($"Style preset active: {styleView.ActivePresetDisplayName} ({styleView.ActivePresetId})");
        Console.WriteLine($"Style helper mode: {styleView.HelperMode}");
        Console.WriteLine($"Style preset options: {string.Join(", ", styleView.AvailablePresets.Select(item => item.DisplayName))}");

        if (!string.IsNullOrWhiteSpace(declarationArg))
        {
            if (!CommercialUsePolicy.TryParseDeclaration(declarationArg, out var declaration))
            {
                throw new ArgumentException("Invalid declaration. Use commercial or non-commercial.");
            }

            var changed = workspace.SetCommercialDeclaration(declaration, "settings-flow");
            Console.WriteLine(changed
                ? $"Commercial declaration updated: {workspace.CommercialPolicy.Declaration}"
                : $"Commercial declaration unchanged: {workspace.CommercialPolicy.Declaration}");
        }

        var policyText = CommercialUsePolicy.BuildPolicyText();
        Console.WriteLine("Settings policy: commercial use declaration");
        Console.WriteLine($"- Current declaration: {workspace.CommercialPolicy.Declaration}");
        Console.WriteLine($"- Criteria: {policyText.CriteriaSummary}");
        Console.WriteLine($"- Revenue share: {policyText.RevenueShareSummary}");

        foreach (var audit in workspace.CommercialDeclarationAudit)
        {
            Console.WriteLine($"- Declaration audit: {audit.PreviousDeclaration} -> {audit.NewDeclaration} at {audit.ChangedAtUtc:O} ({audit.Reason})");
        }

        Console.WriteLine("Editor shell smoke passed.");
    }

    private static void RunPlaytestReportViewer(string reportPath, string? markdownExportPath, string? jsonExportPath)
    {
        var report = PlaytestReportViewer.Load(reportPath);
        Console.WriteLine(PlaytestReportViewer.RenderConsole(report));

        if (!string.IsNullOrWhiteSpace(markdownExportPath))
        {
            PlaytestReportViewer.ExportMarkdown(report, markdownExportPath);
            Console.WriteLine($"Markdown export written: {Path.GetFullPath(markdownExportPath)}");
        }

        if (!string.IsNullOrWhiteSpace(jsonExportPath))
        {
            PlaytestReportViewer.ExportJson(report, jsonExportPath);
            Console.WriteLine($"JSON export written: {Path.GetFullPath(jsonExportPath)}");
        }
    }


    private static int RunSteamReadinessFlow(
        string metricsPath,
        bool requestPublish,
        bool warningAcknowledged,
        bool uploadConfirmed,
        string auditOutputPath,
        string uploadDestination)
    {
        var metrics = SteamReadinessPolicy.LoadMetrics(metricsPath);
        var report = SteamReadinessPolicy.Evaluate(metrics);
        Console.WriteLine(SteamReadinessPolicy.RenderChecklistConsole(report));

        var gate = SteamReadinessPolicy.EvaluatePublishGate(report, warningAcknowledged);
        Console.WriteLine(gate.Message);

        var policyText = CommercialUsePolicy.BuildPolicyText();
        Console.WriteLine("Publish policy: commercial criteria + revenue share");
        Console.WriteLine($"- {policyText.CriteriaSummary}");
        Console.WriteLine($"- {policyText.RevenueShareSummary}");

        if (!requestPublish)
        {
            Console.WriteLine("Publish dry-run complete. Re-run with --publish to execute publish gate flow.");
            return 0;
        }

        if (gate.Decision == PublishDecision.BlockedByCritical)
        {
            Console.WriteLine("Publish action not executed.");
            return 10;
        }

        if (gate.Decision == PublishDecision.RequiresWarningAcknowledgement)
        {
            Console.WriteLine("Publish action not executed.");
            return 11;
        }

        var signingKey = SteamReadinessPolicy.EnsureLocalSigningKey();
        var auditTrail = SteamReadinessPolicy.BuildAuditTrail(metrics, report, signingKey);
        SteamReadinessPolicy.WriteAuditTrail(auditTrail, auditOutputPath);

        Console.WriteLine($"Audit trail generated: {Path.GetFullPath(auditOutputPath)}");
        Console.WriteLine("External submission requires explicit user consent.");

        var uploaded = SteamReadinessPolicy.ConfirmAndUploadAudit(auditOutputPath, uploadDestination, uploadConfirmed);
        Console.WriteLine(uploaded
            ? $"Audit uploaded to external destination: {Path.GetFullPath(uploadDestination)}"
            : "Upload skipped. Re-run with --confirm-upload to consent and continue.");

        return 0;
    }


    private static (string ProjectRoot, string? DeclarationArg) ParseEditorShellSmokeArgs(IReadOnlyList<string> args)
    {
        var defaultProjectRoot = Path.Combine("app", "samples", "generated-prototype", "cozy-colony-tales");
        var declarationArg = GetOptionValue(args, "--set-commercial-declaration");

        var positionalArgs = new List<string>();
        for (var i = 1; i < args.Count; i++)
        {
            var current = args[i];
            if (current.StartsWith("--", StringComparison.Ordinal))
            {
                if (string.Equals(current, "--set-commercial-declaration", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                }

                continue;
            }

            positionalArgs.Add(current);
        }

        var projectRoot = positionalArgs.Count > 0 ? positionalArgs[0] : defaultProjectRoot;
        return (projectRoot, declarationArg);
    }

    private static string? GetOptionValue(IReadOnlyList<string> args, string option)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], option, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
