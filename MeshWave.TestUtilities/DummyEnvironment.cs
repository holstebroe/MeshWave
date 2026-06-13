using MeshWave.Common.Core;

namespace MeshWave.TestUtilities;

public class DummyEnvironment(string tempDir) : IMeshWaveEnvironment
{
    public string GetAppDataRoot() => tempDir;

    public void SetAppDataRootOverride(string? appDataRoot) {}

    public string CombineInAppData(params string[] relativeSegments)
    {
        return relativeSegments.Length == 0 ? tempDir : Path.Combine([tempDir, .. relativeSegments]);
    }

    public string DefaultMyMusicBaseFolder => tempDir;
}
