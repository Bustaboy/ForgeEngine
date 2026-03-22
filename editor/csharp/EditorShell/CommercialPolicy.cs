namespace GameForge.Editor.EditorShell;

public enum CommercialUseDeclaration
{
    NonCommercial = 0,
    Commercial = 1,
}

public sealed record CommercialPolicyConfig
{
    public required CommercialUseDeclaration Declaration { get; init; }

    public string? LastUpdatedUtc { get; init; }

    public static CommercialPolicyConfig CreateDefault() => new()
    {
        Declaration = CommercialUseDeclaration.NonCommercial,
        LastUpdatedUtc = null,
    };
}

public sealed record CommercialPolicyText
{
    public required string CriteriaSummary { get; init; }

    public required string RevenueShareSummary { get; init; }
}

public sealed record CommercialDeclarationAuditEntry
{
    public required CommercialUseDeclaration PreviousDeclaration { get; init; }

    public required CommercialUseDeclaration NewDeclaration { get; init; }

    public required DateTimeOffset ChangedAtUtc { get; init; }

    public required string Reason { get; init; }
}

public static class CommercialUsePolicy
{
    public const decimal RevenueShareTriggerUsd = 100_000m;

    public static CommercialPolicyText BuildPolicyText() => new()
    {
        CriteriaSummary = "Commercial use applies when the project is monetized (paid access, ads, sponsorship, paid DLC, or direct business use).",
        RevenueShareSummary = $"Revenue share starts only after lifetime gross revenue exceeds ${RevenueShareTriggerUsd:N0} USD.",
    };

    public static bool TryParseDeclaration(string value, out CommercialUseDeclaration declaration)
    {
        declaration = CommercialUseDeclaration.NonCommercial;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().Replace("_", "-", StringComparison.Ordinal).ToLowerInvariant();
        declaration = normalized switch
        {
            "commercial" => CommercialUseDeclaration.Commercial,
            "non-commercial" => CommercialUseDeclaration.NonCommercial,
            _ => declaration,
        };

        return normalized is "commercial" or "non-commercial";
    }
}
