using MeshWave.Common.Core.Models;

namespace MeshWave.Common.Core.Processors;

public class PlaylistProcessor : BaseCatalogueEntryProcessor
{
    public override string TargetType => CatalogueEntryType.Playlist;
}
