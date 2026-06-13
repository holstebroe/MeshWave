using MeshWave.Common.Core;

namespace MeshWave.TestUtilities;

public class DummyEnvironment : IMeshWaveEnvironment
{
    private readonly string _tempDir;

    public DummyEnvironment(string tempDir)
    {
        _tempDir = tempDir;
    }

    public string GetAppDataRoot() => _tempDir;

    public void SetAppDataRootOverride(string? appDataRoot) {}

    public string CombineInAppData(params string[] relativeSegments)
    {
        return relativeSegments.Length == 0 ? _tempDir : Path.Combine([_tempDir, .. relativeSegments]);
    }

    public string DefaultMyMusicBaseFolder => _tempDir;
}
