using System.IO;
using MeshWave.Common.Core;

namespace MeshWave.Services;

/// <summary>
/// Applies supported command-line launch overrides through process-level environment variables.
/// </summary>
public static class CommandLineOverrides
{
    public static void Apply(string[] args)
    {
        if (args == null || args.Length == 0)
            return;

        for (var i = 0; i < args.Length; i++)
        {
            var raw = args[i];
            if (string.IsNullOrWhiteSpace(raw) || !raw.StartsWith("--", StringComparison.Ordinal))
                continue;

            var (option, value, consumedNext) = ParseOption(raw, i + 1 < args.Length ? args[i + 1] : null);
            if (consumedNext)
                i++;

            if (string.IsNullOrWhiteSpace(option))
                continue;

            switch (option)
            {
                case "--settings-root":
                case "--appdata-root":
                    MeshWaveEnvironment.SetAppDataRootOverride(value);
                    break;

                case "--user":
                case "--username":
                case "--display-name":
                    SetIfPresent(MeshWaveEnvironment.DisplayNameEnvironmentVariable, value);
                    break;

                case "--base-folder":
                    SetIfPresent(MeshWaveEnvironment.BaseFolderEnvironmentVariable, value, normalizePath: true);
                    break;

                case "--p2p-port":
                    SetIfPositiveInt(MeshWaveEnvironment.P2PPortEnvironmentVariable, value);
                    break;

                case "--p2p-enabled":
                    SetIfBoolean(MeshWaveEnvironment.P2PEnabledEnvironmentVariable, value, defaultIfMissing: true);
                    break;

                case "--p2p-listener":
                case "--act-as-listener":
                    SetIfBoolean(MeshWaveEnvironment.P2PActAsListenerEnvironmentVariable, value, defaultIfMissing: true);
                    break;

                case "--bootstrap":
                case "--bootstrap-nodes":
                    SetIfPresent(MeshWaveEnvironment.P2PBootstrapNodesEnvironmentVariable, value);
                    break;

                case "--max-peers":
                    SetIfPositiveInt(MeshWaveEnvironment.P2PMaxPeersEnvironmentVariable, value);
                    break;

                case "--upload-limit":
                    SetIfNonNegativeInt(MeshWaveEnvironment.P2PUploadLimitEnvironmentVariable, value);
                    break;

                case "--download-limit":
                    SetIfNonNegativeInt(MeshWaveEnvironment.P2PDownloadLimitEnvironmentVariable, value);
                    break;
            }
        }
    }

    private static (string option, string? value, bool consumedNext) ParseOption(string raw, string? next)
    {
        var splitIndex = raw.IndexOf('=');
        if (splitIndex > 2)
        {
            var option = raw[..splitIndex].ToLowerInvariant();
            var value = raw[(splitIndex + 1)..].Trim();
            return (option, value, false);
        }

        var normalized = raw.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(next) && !next.StartsWith("--", StringComparison.Ordinal))
            return (normalized, next.Trim(), true);

        return (normalized, null, false);
    }

    private static void SetIfPresent(string key, string? value, bool normalizePath = false)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var normalized = normalizePath ? Path.GetFullPath(value) : value.Trim();
        Environment.SetEnvironmentVariable(key, normalized);
    }

    private static void SetIfPositiveInt(string key, string? value)
    {
        if (int.TryParse(value, out var parsed) && parsed > 0)
            Environment.SetEnvironmentVariable(key, parsed.ToString());
    }

    private static void SetIfNonNegativeInt(string key, string? value)
    {
        if (int.TryParse(value, out var parsed) && parsed >= 0)
            Environment.SetEnvironmentVariable(key, parsed.ToString());
    }

    private static void SetIfBoolean(string key, string? value, bool defaultIfMissing)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Environment.SetEnvironmentVariable(key, defaultIfMissing ? bool.TrueString : bool.FalseString);
            return;
        }

        if (TryParseBoolean(value, out var parsed))
            Environment.SetEnvironmentVariable(key, parsed.ToString());
    }

    private static bool TryParseBoolean(string value, out bool result)
    {
        var normalized = value.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "1":
            case "true":
            case "yes":
            case "on":
                result = true;
                return true;

            case "0":
            case "false":
            case "no":
            case "off":
                result = false;
                return true;

            default:
                result = false;
                return false;
        }
    }
}
