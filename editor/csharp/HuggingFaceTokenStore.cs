using System.Diagnostics;
using System.Text;

namespace GameForge.Editor;

public static class HuggingFaceTokenStore
{
    private const string PrimaryEnvKey = "HF_TOKEN";
    private const string SecondaryEnvKey = "HUGGINGFACE_TOKEN";

    public static HuggingFaceTokenInfo ResolveToken(string repositoryRoot)
    {
        foreach (var envKey in new[] { PrimaryEnvKey, SecondaryEnvKey })
        {
            var envToken = Environment.GetEnvironmentVariable(envKey)?.Trim();
            if (!string.IsNullOrWhiteSpace(envToken))
            {
                return new HuggingFaceTokenInfo(
                    envToken,
                    "environment",
                    $"Using Hugging Face token from your environment for this session ({MaskToken(envToken)}).",
                    true);
            }
        }

        var workspaceToken = TryReadWorkspaceToken(repositoryRoot);
        if (!string.IsNullOrWhiteSpace(workspaceToken))
        {
            return new HuggingFaceTokenInfo(
                workspaceToken,
                "workspace_env",
                $"Workspace token saved locally in ai-orchestration/python/.env ({MaskToken(workspaceToken)}).",
                true);
        }

        return new HuggingFaceTokenInfo(
            null,
            "none",
            "No Hugging Face token configured yet. Add one before downloading ForgeGuard, Free-Will, or Coding.",
            false);
    }

    public static void ApplyToProcessStartInfo(ProcessStartInfo startInfo, string repositoryRoot)
    {
        var tokenInfo = ResolveToken(repositoryRoot);
        if (!tokenInfo.IsConfigured || string.IsNullOrWhiteSpace(tokenInfo.Token))
        {
            return;
        }

        startInfo.Environment[PrimaryEnvKey] = tokenInfo.Token;
        startInfo.Environment[SecondaryEnvKey] = tokenInfo.Token;
    }

    public static void SaveWorkspaceToken(string repositoryRoot, string token)
    {
        var trimmed = token.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("A Hugging Face token is required.", nameof(token));
        }

        if (!trimmed.StartsWith("hf_", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Hugging Face access tokens should start with 'hf_'.", nameof(token));
        }

        var envPath = GetWorkspaceEnvFilePath(repositoryRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(envPath)!);

        var retainedLines = File.Exists(envPath)
            ? File.ReadAllLines(envPath)
                .Where(line => !IsTokenAssignment(line))
                .ToList()
            : new List<string>();

        retainedLines.Add($"{PrimaryEnvKey}={EscapeEnvValue(trimmed)}");
        File.WriteAllLines(envPath, retainedLines, Encoding.UTF8);
    }

    public static void ClearWorkspaceToken(string repositoryRoot)
    {
        var envPath = GetWorkspaceEnvFilePath(repositoryRoot);
        if (!File.Exists(envPath))
        {
            return;
        }

        var retainedLines = File.ReadAllLines(envPath)
            .Where(line => !IsTokenAssignment(line))
            .ToList();

        if (retainedLines.Count == 0)
        {
            File.Delete(envPath);
            return;
        }

        File.WriteAllLines(envPath, retainedLines, Encoding.UTF8);
    }

    public static string GetWorkspaceEnvFilePath(string repositoryRoot)
    {
        return Path.Combine(repositoryRoot, "ai-orchestration", "python", ".env");
    }

    private static string? TryReadWorkspaceToken(string repositoryRoot)
    {
        var envPath = GetWorkspaceEnvFilePath(repositoryRoot);
        if (!File.Exists(envPath))
        {
            return null;
        }

        foreach (var line in File.ReadAllLines(envPath))
        {
            if (!TryParseTokenAssignment(line, out var token))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(token))
            {
                return token;
            }
        }

        return null;
    }

    private static bool IsTokenAssignment(string line)
    {
        return TryParseTokenAssignment(line, out _);
    }

    private static bool TryParseTokenAssignment(string line, out string? token)
    {
        token = null;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmed = line.Trim();
        if (trimmed.StartsWith('#'))
        {
            return false;
        }

        foreach (var key in new[] { PrimaryEnvKey, SecondaryEnvKey })
        {
            if (!trimmed.StartsWith($"{key}=", StringComparison.Ordinal))
            {
                continue;
            }

            var value = trimmed[(key.Length + 1)..].Trim();
            if (value.Length >= 2 &&
                ((value.StartsWith('"') && value.EndsWith('"')) || (value.StartsWith('\'') && value.EndsWith('\''))))
            {
                value = value[1..^1];
            }

            token = value;
            return true;
        }

        return false;
    }

    private static string EscapeEnvValue(string value)
    {
        var escaped = value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }

    private static string MaskToken(string token)
    {
        var trimmed = token.Trim();
        if (trimmed.Length <= 8)
        {
            return trimmed;
        }

        return $"{trimmed[..4]}...{trimmed[^4..]}";
    }
}

public sealed record HuggingFaceTokenInfo(string? Token, string Source, string StatusMessage, bool IsConfigured);
