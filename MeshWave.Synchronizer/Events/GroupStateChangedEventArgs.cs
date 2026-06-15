using System;
using System.Collections.Generic;
using MeshWave.Common.Core.Models;

namespace MeshWave.Synchronizer;

public class GroupStateChangedEventArgs(string userId, ManifestOperationType operationType, string targetId, Dictionary<string, string> metadata) : EventArgs
{
    public string UserId { get; } = userId;
    public ManifestOperationType OperationType { get; } = operationType;
    public string TargetId { get; } = targetId;
    public Dictionary<string, string> Metadata { get; } = metadata;
}
