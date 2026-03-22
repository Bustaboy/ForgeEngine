using System.Text.Json;
using GameForge.Editor.EditorShell;

namespace GameForge.Editor.Tests;

public sealed class PlaytestReportViewerTests
{
    [Fact]
    public void Load_ValidReport_IncludesRequiredSectionsAndRenders()
    {
        var reportPath = Path.Combine(Path.GetTempPath(), $"gameforge-report-{Guid.NewGuid():N}.json");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(BuildReportPayload(), new JsonSerializerOptions { WriteIndented = true }));

        var report = PlaytestReportViewer.Load(reportPath);
        var rendered = PlaytestReportViewer.RenderConsole(report);

        Assert.Equal("gameforge.playtest_report.v1", report.Schema);
        Assert.Contains("Overall Status", rendered);
        Assert.Contains("[Progression]", rendered);
        Assert.Contains("[Performance]", rendered);
    }

    [Fact]
    public void Export_WritesMarkdownAndJson()
    {
        var reportPath = Path.Combine(Path.GetTempPath(), $"gameforge-report-{Guid.NewGuid():N}.json");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(BuildReportPayload(), new JsonSerializerOptions { WriteIndented = true }));

        var report = PlaytestReportViewer.Load(reportPath);
        var outputDir = Path.Combine(Path.GetTempPath(), $"gameforge-report-export-{Guid.NewGuid():N}");
        var markdownPath = Path.Combine(outputDir, "playtest.md");
        var jsonPath = Path.Combine(outputDir, "playtest.json");

        PlaytestReportViewer.ExportMarkdown(report, markdownPath);
        PlaytestReportViewer.ExportJson(report, jsonPath);

        Assert.True(File.Exists(markdownPath));
        Assert.True(File.Exists(jsonPath));
        Assert.Contains("## Progression", File.ReadAllText(markdownPath));

        var exported = JsonDocument.Parse(File.ReadAllText(jsonPath));
        Assert.Equal("gameforge.playtest_report.v1", exported.RootElement.GetProperty("schema").GetString());
    }

    private static object BuildReportPayload()
    {
        return new
        {
            schema = "gameforge.playtest_report.v1",
            report_id = "cozy-colony-baseline-20260322T000000Z",
            scenario_id = "cozy-colony-baseline",
            prototype_root = "app/samples/generated-prototype/cozy-colony-tales",
            generated_at_utc = "2026-03-22T00:00:00Z",
            overall_status = "passed",
            summary = "Bot playtest baseline checks passed for generated prototype.",
            sections = new[]
            {
                Section("progression", "Progression"),
                Section("economy", "Economy"),
                Section("dead-end", "Dead End"),
                Section("pacing", "Pacing"),
                Section("performance", "Performance"),
            },
            source_probe_results = new[]
            {
                new
                {
                    probe_id = "performance-frame-budget",
                    status = "passed",
                    details = "Expected frame budget under 16ms, observed 14ms",
                    required = true,
                },
            },
        };
    }

    private static object Section(string id, string title) => new
    {
        section_id = id,
        title,
        status = "healthy",
        findings = new[] { "No required issues detected in this section during bot playtesting." },
        recommendations = new[] { "Keep monitoring this section in subsequent regression runs." },
    };
}
