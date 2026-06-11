namespace MeshWave.Common.Core;

/// <summary>
/// Class for providing root folders for MeshWave.
/// </summary>
public class FolderLookup(string contentBaseFolder)
{
    /// <summary>
    /// Gets the folder where the user's own music files are stored.
    /// </summary>
    public string GetLocalMusicFolder() => Path.Combine(contentBaseFolder, "Local Music");

    /// <summary>
    /// Gets the folder where the downloaded music files are stored.
    /// </summary>
    public string GetPeerMusicFolder() => Path.Combine(contentBaseFolder, "Peer Music");
}