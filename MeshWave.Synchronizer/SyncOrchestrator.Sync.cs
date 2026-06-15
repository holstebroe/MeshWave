using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MeshWave.Common.Core;
using MeshWave.Common.Core.Models;
using MeshWave.Common.Core.P2P;
using MeshWave.Common.Core.Storage;
using MeshWave.Common.Core.Validation;
using NLog;

namespace MeshWave.Synchronizer;

public partial class SyncOrchestrator
{
    public async Task SyncAllPeersAsync(CancellationToken cancellationToken = default)
    {
        foreach (var peer in _router.GetPeers()) await TryFetchAndMergeAsync(peer, cancellationToken);
    }

}
