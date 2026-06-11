using MeshWave.Common.Core;
using MeshWave.Common.Core.Models;

namespace MeshWave.Wpf.Models;

/// <summary>
/// Application settings model
/// </summary>
public class AppSettings
{
    public string Version { get; set; } = "1.0";
    public string BaseFolder { get; set; } = string.Empty;
    public string Theme { get; set; } = "Dark";
    public string AudioDevice { get; set; } = "Default";
    public List<string> SupportedExtensions { get; set; } = [".mp3", ".flac", ".wav", ".ogg", ".m4a"];
    public P2PSettings P2P { get; set; } = new();
    public PlaybackSettings Playback { get; set; } = new();
    public StorageSettings Storage { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();

    public FolderLookup GetFolderLookup()
    {
        return new FolderLookup(BaseFolder);
    }
}

public class P2PSettings
{
    public bool Enabled { get; set; } = false;
    public int Port { get; set; } = 47474;
    public int MaxPeers { get; set; } = 10;
    public int UploadLimit { get; set; } = 0; // 0 = unlimited
    public int DownloadLimit { get; set; } = 0; // 0 = unlimited

    /// <summary>
    /// When true, this app listens for inbound peer connections and announces itself on LAN.
    /// When false, it behaves as an outbound-only bootstrap client.
    /// </summary>
    public bool ActAsListener { get; set; } = true;

    /// <summary>
    /// Internet bootstrap nodes in "host:port" format.
    /// These are the initial contact points for reaching peers outside the LAN,
    /// similar to BitTorrent bootstrap/tracker nodes.
    /// </summary>
    public List<string> BootstrapNodes { get; set; } = [];
}

public class PlaybackSettings
{
    public double RegisterPlayAt { get; set; } = 0.5; // 50%
    public double Volume { get; set; } = 0.8;
    public double CrossfadeDuration { get; set; } = 2.0; // seconds
    public string WaveformStyle { get; set; } = "Filled";
    public string PreferredAudioQuality { get; set; } = "Original"; // "Original" or "Compressed"
    public PlaybackResumeState ResumeState { get; set; } = new();
}

public class StorageSettings
{
    public double QuotaWarningGb { get; set; } = 10;
}

public class LoggingSettings
{
    public bool Enabled { get; set; } = false;
    public bool Verbose { get; set; } = false;
}