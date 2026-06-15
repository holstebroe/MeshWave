using System;

namespace MeshWave.Synchronizer;

public class ManifestMergedEventArgs(string userId, int operationsAdded) : EventArgs
{
    public string UserId { get; } = userId;
    public int OperationsAdded { get; } = operationsAdded;
}
