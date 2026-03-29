using System.Text;
using System.Text.Json;

namespace GameForge.Editor.EditorShell;

public sealed record PlaytestReportSectionView
{
    public required string SectionId { get; init; }

    public required string Title { get; init; }

    public required string Status { get; init; }

    public required IReadOnlyList<string> Findings { get; init; }

    public required IReadOnlyList<string> Recommendations { get; init; }
}

public sealed record PlaytestReportView
{
    public required string Schema { get; init; }

    public required string ReportId { get; init; }

    public required string ScenarioId { get; init; }

    public required string PrototypeRoot { get; init; }

    public required string GeneratedAtUtc { get; init; }

    public required string OverallStatus { get; init; }

    public required string Summary { get; init; }

    public required IReadOnlyList<PlaytestReportSectionView> Sections { get; init; }

    public required JsonElement SourceProbeResults { get; init; }
}

public static class PlaytestReportViewer
{
    private const string ExpectedSchema = "gameforge.playtest_report.v1";

    private static readonly string[] RequiredSectionIds =
    [
        "progression",
        "economy",
        "dead-end",
        "pacing",
        "performance",
    ];

    public static PlaytestReportView Load(string path)
    {
        var payload = JsonNode(path);
        var schema = payload.GetProperty("schema").GetString() ?? string.Empty;
        if (!string.Equals(schema, ExpectedSchema, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported playtest report schema: {schema}");
        }

        var sections = payload
            .GetProperty("sections")
            .EnumerateArray()
            .Select(item => new PlaytestReportSectionView
            {
                SectionId = item.GetProperty("section_id").GetString() ?? string.Empty,
                Title = item.GetProperty("title").GetString() ?? string.Empty,
                Status = item.GetProperty("status").GetString() ?? string.Empty,
                Findings = item.GetProperty("findings").EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToList(),
                Recommendations = item.GetProperty("recommendations").EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToList(),
            })
            .ToList();

        ValidateSections(sections);

        return new PlaytestReportView
        {
            Schema = schema,
            ReportId = payload.GetProperty("report_id").GetString() ?? string.Empty,
            ScenarioId = payload.GetProperty("scenario_id").GetString() ?? string.Empty,
            PrototypeRoot = payload.GetProperty("prototype_root").GetString() ?? string.Empty,
            GeneratedAtUtc = payload.GetProperty("generated_at_utc").GetString() ?? string.Empty,
            OverallStatus = payload.GetProperty("overall_status").GetString() ?? string.Empty,
            Summary = payload.GetProperty("summary").GetString() ?? string.Empty,
            Sections = sections,
            SourceProbeResults = payload.GetProperty("source_probe_results").Clone(),
        };
    }

    public static string RenderConsole(PlaytestReportView report)
    {
        var lines = new List<string>
        {
            "=== Soul Loom Playtest Report Viewer ===",
            $"Report ID: {report.ReportId}",
            $"Scenario: {report.ScenarioId}",
            $"Generated (UTC): {report.GeneratedAtUtc}",
            $"Overall Status: {report.OverallStatus}",
            $"Summary: {report.Summary}",
        };

        foreach (var section in report.Sections)
        {
            lines.Add(string.Empty);
            lines.Add($"[{section.Title}] status={section.Status}");
            lines.Add("Findings:");
            lines.AddRange(section.Findings.Select(finding => $"- {finding}"));
            lines.Add("Recommendations:");
            lines.AddRange(section.Recommendations.Select(recommendation => $"- {recommendation}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static void ExportMarkdown(PlaytestReportView report, string destinationPath)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Soul Loom Playtest Report");
        builder.AppendLine();
        builder.AppendLine($"- Report ID: `{report.ReportId}`");
        builder.AppendLine($"- Scenario: `{report.ScenarioId}`");
        builder.AppendLine($"- Generated (UTC): `{report.GeneratedAtUtc}`");
        builder.AppendLine($"- Overall status: **{report.OverallStatus}**");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine(report.Summary);

        foreach (var section in report.Sections)
        {
            builder.AppendLine();
            builder.AppendLine($"## {section.Title}");
            builder.AppendLine($"- Status: **{section.Status}**");
            builder.AppendLine("- Findings:");
            foreach (var finding in section.Findings)
            {
                builder.AppendLine($"  - {finding}");
            }

            builder.AppendLine("- Recommendations:");
            foreach (var recommendation in section.Recommendations)
            {
                builder.AppendLine($"  - {recommendation}");
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? ".");
        File.WriteAllText(destinationPath, builder.ToString(), Encoding.UTF8);
    }

    public static void ExportJson(PlaytestReportView report, string destinationPath)
    {
        var payload = new
        {
            schema = report.Schema,
            report_id = report.ReportId,
            scenario_id = report.ScenarioId,
            prototype_root = report.PrototypeRoot,
            generated_at_utc = report.GeneratedAtUtc,
            overall_status = report.OverallStatus,
            summary = report.Summary,
            sections = report.Sections.Select(section => new
            {
                section_id = section.SectionId,
                title = section.Title,
                status = section.Status,
                findings = section.Findings,
                recommendations = section.Recommendations,
            }),
            source_probe_results = report.SourceProbeResults,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? ".");
        File.WriteAllText(destinationPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, Encoding.UTF8);
    }

    private static JsonElement JsonNode(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        return document.RootElement.Clone();
    }

    private static void ValidateSections(IReadOnlyCollection<PlaytestReportSectionView> sections)
    {
        var ids = new HashSet<string>(sections.Select(section => section.SectionId), StringComparer.OrdinalIgnoreCase);
        foreach (var required in RequiredSectionIds)
        {
            if (!ids.Contains(required))
            {
                throw new InvalidDataException($"Playtest report missing required section: {required}");
            }
        }
    }
}
