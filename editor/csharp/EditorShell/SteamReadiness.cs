using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GameForge.Editor.EditorShell;

public enum ReadinessSeverity
{
    Critical = 0,
    Warning = 1,
}

public sealed record SteamQualityMetrics
{
    public required double CrashFreeSessionRatePercent { get; init; }

    public required double SustainedFpsFloor { get; init; }

    public required double Fps60CompliancePercent { get; init; }

    public required double InitialSceneLoadSeconds { get; init; }

    public required double SafeSavePassRatePercent { get; init; }
}

public sealed record SteamReadinessChecklistItem
{
    public required string ItemId { get; init; }

    public required string Label { get; init; }

    public required ReadinessSeverity Severity { get; init; }

    public required bool Passed { get; init; }

    public required string ResultSummary { get; init; }

    public required string ThresholdSummary { get; init; }
}

public sealed record SteamReadinessReport
{
    public required int Score { get; init; }

    public required IReadOnlyList<SteamReadinessChecklistItem> Checklist { get; init; }

    public int CriticalIssueCount => Checklist.Count(item => item.Severity == ReadinessSeverity.Critical && !item.Passed);

    public int WarningIssueCount => Checklist.Count(item => item.Severity == ReadinessSeverity.Warning && !item.Passed);

    public bool BlocksPublish => CriticalIssueCount > 0;
}

public enum PublishDecision
{
    Ready = 0,
    BlockedByCritical = 1,
    RequiresWarningAcknowledgement = 2,
}

public sealed record PublishGateResult
{
    public required PublishDecision Decision { get; init; }

    public required string Message { get; init; }

    public required bool RequiresConsentBeforeUpload { get; init; }
}

public sealed record PublishAuditTrail
{
    public required string Schema { get; init; }

    public required string GeneratedAtUtc { get; init; }

    public required SteamQualityMetrics Metrics { get; init; }

    public required SteamReadinessReport Readiness { get; init; }

    public required string PayloadSha256 { get; init; }

    public required string SignatureKeyId { get; init; }

    public required string SignatureHex { get; init; }
}

public static class SteamReadinessPolicy
{
    private const string AuditSchema = "gameforge.steam_readiness_audit.v1";

    public static SteamQualityMetrics LoadMetrics(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var root = document.RootElement;

        return new SteamQualityMetrics
        {
            CrashFreeSessionRatePercent = RequiredMetric(root, "crash_free_session_rate_percent"),
            SustainedFpsFloor = RequiredMetric(root, "sustained_fps_floor"),
            Fps60CompliancePercent = RequiredMetric(root, "fps_60_compliance_percent"),
            InitialSceneLoadSeconds = RequiredMetric(root, "initial_scene_load_seconds"),
            SafeSavePassRatePercent = RequiredMetric(root, "safe_save_pass_rate_percent"),
        };
    }

    public static SteamReadinessReport Evaluate(SteamQualityMetrics metrics)
    {
        var checklist = new List<SteamReadinessChecklistItem>
        {
            BuildItem(
                "crash-free-session-rate",
                "Crash-free session rate",
                ReadinessSeverity.Critical,
                metrics.CrashFreeSessionRatePercent >= 97.0,
                $"Observed {metrics.CrashFreeSessionRatePercent:F1}% crash-free sessions.",
                "Threshold: >= 97.0%"),
            BuildItem(
                "sustained-fps-floor",
                "Sustained FPS floor on target validation scenes",
                ReadinessSeverity.Critical,
                metrics.SustainedFpsFloor >= 30.0,
                $"Observed sustained floor {metrics.SustainedFpsFloor:F1} FPS.",
                "Threshold: >= 30 FPS"),
            BuildItem(
                "initial-scene-load",
                "Initial scene load time on target hardware",
                ReadinessSeverity.Critical,
                metrics.InitialSceneLoadSeconds < 20.0,
                $"Observed load time {metrics.InitialSceneLoadSeconds:F2}s.",
                "Threshold: < 20.0s"),
            BuildItem(
                "safe-save-regression",
                "Safe-save regression integrity",
                ReadinessSeverity.Critical,
                metrics.SafeSavePassRatePercent >= 100.0,
                $"Observed safe-save pass rate {metrics.SafeSavePassRatePercent:F1}%.",
                "Threshold: 100.0%"),
            BuildItem(
                "fps-60-target",
                "60 FPS target coverage in core gameplay scenes",
                ReadinessSeverity.Warning,
                metrics.Fps60CompliancePercent >= 95.0,
                $"Observed 60 FPS coverage {metrics.Fps60CompliancePercent:F1}%.",
                "Threshold: >= 95.0%"),
        };

        var criticalFailures = checklist.Count(item => item.Severity == ReadinessSeverity.Critical && !item.Passed);
        var warningFailures = checklist.Count(item => item.Severity == ReadinessSeverity.Warning && !item.Passed);
        var score = Math.Max(0, 100 - (criticalFailures * 20) - (warningFailures * 10));

        return new SteamReadinessReport
        {
            Score = score,
            Checklist = checklist,
        };
    }

    public static string RenderChecklistConsole(SteamReadinessReport report)
    {
        var lines = new List<string>
        {
            "=== Steam Readiness Checklist (V1) ===",
            $"Readiness score: {report.Score}/100",
            $"Critical blockers: {report.CriticalIssueCount}",
            $"Warnings: {report.WarningIssueCount}",
            string.Empty,
        };

        foreach (var item in report.Checklist)
        {
            var severityLabel = item.Severity == ReadinessSeverity.Critical ? "CRITICAL" : "WARNING";
            var statusLabel = item.Passed ? "PASS" : "FAIL";
            lines.Add($"[{severityLabel}] [{statusLabel}] {item.Label}");
            lines.Add($"  - {item.ResultSummary}");
            lines.Add($"  - {item.ThresholdSummary}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static PublishGateResult EvaluatePublishGate(SteamReadinessReport report, bool warningAcknowledged)
    {
        if (report.CriticalIssueCount > 0)
        {
            return new PublishGateResult
            {
                Decision = PublishDecision.BlockedByCritical,
                RequiresConsentBeforeUpload = false,
                Message = $"Publish blocked: {report.CriticalIssueCount} critical issue(s) must be fixed before publishing.",
            };
        }

        if (report.WarningIssueCount > 0 && !warningAcknowledged)
        {
            return new PublishGateResult
            {
                Decision = PublishDecision.RequiresWarningAcknowledgement,
                RequiresConsentBeforeUpload = false,
                Message = $"Warnings found: {report.WarningIssueCount} warning issue(s) require explicit acknowledgement before publish.",
            };
        }

        return new PublishGateResult
        {
            Decision = PublishDecision.Ready,
            RequiresConsentBeforeUpload = true,
            Message = report.WarningIssueCount > 0
                ? $"Publish allowed with acknowledged warnings ({report.WarningIssueCount})."
                : "Publish allowed: no blocking readiness issues.",
        };
    }

    public static PublishAuditTrail BuildAuditTrail(SteamQualityMetrics metrics, SteamReadinessReport report, string keyMaterial)
    {
        var generatedAtUtc = DateTimeOffset.UtcNow.ToString("O");
        var unsignedPayload = new
        {
            schema = AuditSchema,
            generated_at_utc = generatedAtUtc,
            metrics,
            readiness = new
            {
                report.Score,
                report.CriticalIssueCount,
                report.WarningIssueCount,
                checklist = report.Checklist,
            },
        };

        var payloadJson = JsonSerializer.Serialize(unsignedPayload);
        var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
        var payloadHash = SHA256.HashData(payloadBytes);
        var signature = SignHash(payloadHash, keyMaterial);

        return new PublishAuditTrail
        {
            Schema = AuditSchema,
            GeneratedAtUtc = generatedAtUtc,
            Metrics = metrics,
            Readiness = report,
            PayloadSha256 = Convert.ToHexString(payloadHash).ToLowerInvariant(),
            SignatureKeyId = ComputeKeyId(keyMaterial),
            SignatureHex = Convert.ToHexString(signature).ToLowerInvariant(),
        };
    }

    public static void WriteAuditTrail(PublishAuditTrail auditTrail, string destinationPath)
    {
        EnsureParentDirectory(destinationPath);
        var payload = JsonSerializer.Serialize(auditTrail, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(destinationPath, payload + Environment.NewLine, Encoding.UTF8);
    }

    public static string EnsureLocalSigningKey(string? homeDirectoryOverride = null)
    {
        var homePath = homeDirectoryOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var keyDirectory = Path.Combine(homePath, ".gameforge", "keys");
        var keyPath = Path.Combine(keyDirectory, "publish-audit-signing.key");

        if (File.Exists(keyPath))
        {
            return File.ReadAllText(keyPath, Encoding.UTF8).Trim();
        }

        Directory.CreateDirectory(keyDirectory);
        var newKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        File.WriteAllText(keyPath, newKey + Environment.NewLine, Encoding.UTF8);
        return newKey;
    }

    public static bool ConfirmAndUploadAudit(string localAuditPath, string externalDestinationPath, bool userConfirmed)
    {
        if (!userConfirmed)
        {
            return false;
        }

        EnsureParentDirectory(externalDestinationPath);
        File.Copy(localAuditPath, externalDestinationPath, overwrite: true);
        return true;
    }


    private static void EnsureParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        Directory.CreateDirectory(string.IsNullOrWhiteSpace(directory) ? "." : directory);
    }

    private static SteamReadinessChecklistItem BuildItem(
        string id,
        string label,
        ReadinessSeverity severity,
        bool passed,
        string resultSummary,
        string thresholdSummary)
    {
        return new SteamReadinessChecklistItem
        {
            ItemId = id,
            Label = label,
            Severity = severity,
            Passed = passed,
            ResultSummary = resultSummary,
            ThresholdSummary = thresholdSummary,
        };
    }

    private static double RequiredMetric(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            throw new InvalidDataException($"Missing numeric metric: {propertyName}");
        }

        return value.GetDouble();
    }

    private static byte[] SignHash(byte[] hash, string keyMaterial)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(keyMaterial));
        return hmac.ComputeHash(hash);
    }

    private static string ComputeKeyId(string keyMaterial)
    {
        var keyHash = SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial));
        return Convert.ToHexString(keyHash)[..16].ToLowerInvariant();
    }
}
