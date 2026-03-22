using System.Text.Json;
using GameForge.Editor.EditorShell;

namespace GameForge.Editor.Tests;

public sealed class SteamReadinessPolicyTests
{
    [Fact]
    public void Evaluate_CriticalFailure_BlocksPublish()
    {
        var report = SteamReadinessPolicy.Evaluate(new SteamQualityMetrics
        {
            CrashFreeSessionRatePercent = 96.4,
            SustainedFpsFloor = 28.0,
            Fps60CompliancePercent = 92.0,
            InitialSceneLoadSeconds = 21.0,
            SafeSavePassRatePercent = 99.0,
        });

        var gate = SteamReadinessPolicy.EvaluatePublishGate(report, warningAcknowledged: true);

        Assert.True(report.Score < 100);
        Assert.True(report.CriticalIssueCount >= 1);
        Assert.Equal(PublishDecision.BlockedByCritical, gate.Decision);
        Assert.Contains("blocked", gate.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_WarningsRequireAcknowledgement_AndAllowOverride()
    {
        var report = SteamReadinessPolicy.Evaluate(new SteamQualityMetrics
        {
            CrashFreeSessionRatePercent = 99.4,
            SustainedFpsFloor = 58.0,
            Fps60CompliancePercent = 91.0,
            InitialSceneLoadSeconds = 11.0,
            SafeSavePassRatePercent = 100.0,
        });

        Assert.Equal(0, report.CriticalIssueCount);
        Assert.Equal(1, report.WarningIssueCount);

        var blockedWithoutAck = SteamReadinessPolicy.EvaluatePublishGate(report, warningAcknowledged: false);
        Assert.Equal(PublishDecision.RequiresWarningAcknowledgement, blockedWithoutAck.Decision);

        var readyWithAck = SteamReadinessPolicy.EvaluatePublishGate(report, warningAcknowledged: true);
        Assert.Equal(PublishDecision.Ready, readyWithAck.Decision);
    }

    [Fact]
    public void RenderChecklistConsole_ShowsSeverityLabels()
    {
        var report = SteamReadinessPolicy.Evaluate(new SteamQualityMetrics
        {
            CrashFreeSessionRatePercent = 98.0,
            SustainedFpsFloor = 45.0,
            Fps60CompliancePercent = 80.0,
            InitialSceneLoadSeconds = 13.0,
            SafeSavePassRatePercent = 100.0,
        });

        var output = SteamReadinessPolicy.RenderChecklistConsole(report);

        Assert.Contains("[CRITICAL]", output);
        Assert.Contains("[WARNING]", output);
        Assert.Contains("Readiness score", output);
    }

    [Fact]
    public void BuildAuditTrail_GeneratesHashSignature_AndRequiresUploadConsent()
    {
        var metrics = new SteamQualityMetrics
        {
            CrashFreeSessionRatePercent = 99.0,
            SustainedFpsFloor = 40.0,
            Fps60CompliancePercent = 96.0,
            InitialSceneLoadSeconds = 9.0,
            SafeSavePassRatePercent = 100.0,
        };
        var report = SteamReadinessPolicy.Evaluate(metrics);

        var tempRoot = Path.Combine(Path.GetTempPath(), $"gameforge-steam-ready-{Guid.NewGuid():N}");
        var homePath = Path.Combine(tempRoot, "home");
        var localAuditPath = Path.Combine(tempRoot, "audit", "steam-readiness-audit.json");
        var externalPath = Path.Combine(tempRoot, "external", "steam-readiness-audit.json");

        var key = SteamReadinessPolicy.EnsureLocalSigningKey(homePath);
        var audit = SteamReadinessPolicy.BuildAuditTrail(metrics, report, key);
        SteamReadinessPolicy.WriteAuditTrail(audit, localAuditPath);

        Assert.True(File.Exists(localAuditPath));
        Assert.Equal(64, audit.PayloadSha256.Length);
        Assert.Equal(64, audit.SignatureHex.Length);

        var uploadedWithoutConsent = SteamReadinessPolicy.ConfirmAndUploadAudit(localAuditPath, externalPath, userConfirmed: false);
        Assert.False(uploadedWithoutConsent);
        Assert.False(File.Exists(externalPath));

        var uploadedWithConsent = SteamReadinessPolicy.ConfirmAndUploadAudit(localAuditPath, externalPath, userConfirmed: true);
        Assert.True(uploadedWithConsent);
        Assert.True(File.Exists(externalPath));

        var payload = JsonDocument.Parse(File.ReadAllText(localAuditPath)).RootElement;
        Assert.Equal("gameforge.steam_readiness_audit.v1", payload.GetProperty("Schema").GetString());
    }
    [Fact]
    public void AuditWriteAndUpload_SupportFilenameOnlyPaths()
    {
        var metrics = new SteamQualityMetrics
        {
            CrashFreeSessionRatePercent = 99.0,
            SustainedFpsFloor = 40.0,
            Fps60CompliancePercent = 96.0,
            InitialSceneLoadSeconds = 9.0,
            SafeSavePassRatePercent = 100.0,
        };
        var report = SteamReadinessPolicy.Evaluate(metrics);

        var tempRoot = Path.Combine(Path.GetTempPath(), $"gameforge-steam-ready-filename-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var originalCwd = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = tempRoot;
            var key = SteamReadinessPolicy.EnsureLocalSigningKey(Path.Combine(tempRoot, "home"));
            var audit = SteamReadinessPolicy.BuildAuditTrail(metrics, report, key);

            SteamReadinessPolicy.WriteAuditTrail(audit, "steam-readiness-audit.json");
            Assert.True(File.Exists(Path.Combine(tempRoot, "steam-readiness-audit.json")));

            var uploaded = SteamReadinessPolicy.ConfirmAndUploadAudit("steam-readiness-audit.json", "steam-upload.json", userConfirmed: true);
            Assert.True(uploaded);
            Assert.True(File.Exists(Path.Combine(tempRoot, "steam-upload.json")));
        }
        finally
        {
            Environment.CurrentDirectory = originalCwd;
        }
    }


    [Fact]
    public void CommercialPolicyText_ContainsCriteriaAndRevenueThreshold()
    {
        var policy = CommercialUsePolicy.BuildPolicyText();

        Assert.Contains("monetized", policy.CriteriaSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("100,000", policy.RevenueShareSummary, StringComparison.OrdinalIgnoreCase);
    }

}
