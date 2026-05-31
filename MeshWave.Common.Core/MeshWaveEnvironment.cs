namespace MeshWave.Common.Core;

/// <summary>
/// Provides process-level runtime path and launch-override configuration for MeshWave.
/// </summary>
public static class MeshWaveEnvironment
{
    public const string AppDataRootEnvironmentVariable = "MESHWAVE_APPDATA_ROOT";
    public const string BaseFolderEnvironmentVariable = "MESHWAVE_BASE_FOLDER";
    public const string DisplayNameEnvironmentVariable = "MESHWAVE_DISPLAY_NAME";
    public const string P2PPortEnvironmentVariable = "MESHWAVE_P2P_PORT";
    public const string P2PEnabledEnvironmentVariable = "MESHWAVE_P2P_ENABLED";
    public const string P2PActAsListenerEnvironmentVariable = "MESHWAVE_P2P_ACT_AS_LISTENER";
    public const string P2PBootstrapNodesEnvironmentVariable = "MESHWAVE_P2P_BOOTSTRAP_NODES";
    public const string P2PMaxPeersEnvironmentVariable = "MESHWAVE_P2P_MAX_PEERS";
    public const string P2PUploadLimitEnvironmentVariable = "MESHWAVE_P2P_UPLOAD_LIMIT";
    public const string P2PDownloadLimitEnvironmentVariable = "MESHWAVE_P2P_DOWNLOAD_LIMIT";

    public static string DefaultAppDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MeshWave");

    public static string GetAppDataRoot()
    {
        var overrideRoot = Environment.GetEnvironmentVariable(AppDataRootEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(overrideRoot))
            return DefaultAppDataRoot;

        return Path.GetFullPath(overrideRoot);
    }

    public static void SetAppDataRootOverride(string? appDataRoot)
    {
        if (string.IsNullOrWhiteSpace(appDataRoot))
        {
            Environment.SetEnvironmentVariable(AppDataRootEnvironmentVariable, null);
            return;
        }

        Environment.SetEnvironmentVariable(AppDataRootEnvironmentVariable, Path.GetFullPath(appDataRoot));
    }

    public static string CombineInAppData(params string[] relativeSegments)
    {
        var root = GetAppDataRoot();
        return relativeSegments.Length == 0
            ? root
            : Path.Combine([root, .. relativeSegments]);
    }
}
